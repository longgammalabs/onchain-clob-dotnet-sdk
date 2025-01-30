using OnchainClob.Abi.Lob;
using OnchainClob.Client;
using OnchainClob.Client.Configuration;
using OnchainClob.Client.Events;
using OnchainClob.Common;
using OnchainClob.Trading.Abstract;
using OnchainClob.Trading.Events;
using Incendium;
using Microsoft.Extensions.Logging;
using Nethereum.Contracts;
using Revelium.Evm.Common;
using Revelium.Evm.Rpc;
using Revelium.Evm.Rpc.Parameters;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading.Channels;
using ErrorEventArgs = OnchainClob.Trading.Events.ErrorEventArgs;

namespace OnchainClob.Trading
{
    public class SpotTrader : ITrader
    {
        private const long BASE_FEE_PER_GAS = 100_000_000_000;
        private const string ALL_MARKETS = "allMarkets";
        private const int PENDING_CALLS_CHECK_INTERVAL_MS = 10;
        private readonly BigInteger UINT128_MAX_VALUE = (BigInteger.One << 128) - 1;
        private const long DEFAULT_EXPIRED_SEC = 60 * 60 * 24;
        private const int CANCELED_ORDERS_TTL_MS = 30 * 1000;
        private const int EIP1559_TRANSACTION_TYPE = 2;
        private const int ESTIMATE_GAS_RESERVE_IN_PERCENTS = 10;

        public event EventHandler<List<Order>>? OrdersChanged;
        public event EventHandler<bool>? AvailabilityChanged;

        private readonly ISymbolConfig _symbolConfig;
        private readonly WebSocketClient _webSocketClient;
        private readonly RestApi _restApi;
        private readonly Spot _spot;
        private readonly RpcClient _rpc;
        private readonly GasLimits? _defaultGasLimits;
        private readonly ILogger<SpotTrader>? _logger;
        private Channel<UserOrdersEventArgs>? _userOrdersChannel;
        private CancellationTokenSource? _userOrdersHandlerCts;

        private readonly SemaphoreSlim _ordersSync;
        private readonly ConcurrentDictionary<ulong, DateTimeOffset> _canceledOrders;
        private readonly ConcurrentDictionary<string, List<string>> _pendingCancellationRequests;
        private readonly ConcurrentDictionary<string, List<Order>> _pendingOrders;
        private readonly ConcurrentDictionary<string, bool> _pendingRequests;
        private readonly Dictionary<string, Order> _activeOrders;

        private bool _isAvailable;
        public bool IsAvailable
        {
            get => _isAvailable;
            private set
            {
                if (_isAvailable != value)
                {
                    _isAvailable = value;
                    AvailabilityChanged?.Invoke(this, _isAvailable);
                }
            }
        }

        public string Symbol => _symbolConfig.Symbol;

        public SpotTrader(
            ISymbolConfig symbolConfig,
            WebSocketClient webSocketClient,
            RestApi restApi,
            Spot spot,
            RpcClient rpc,
            GasLimits? defaultGasLimits = null,
            ILogger<SpotTrader>? logger = null)
        {
            _symbolConfig = symbolConfig ?? throw new ArgumentNullException(nameof(symbolConfig));
            _defaultGasLimits = defaultGasLimits;
            _logger = logger;

            _webSocketClient = webSocketClient ?? throw new ArgumentNullException(nameof(webSocketClient));
            _webSocketClient.Disconnected += WebSocketClient_Disconnected;
            _webSocketClient.StateStatusChanged += WebSocketClient_StateStatusChanged;
            _webSocketClient.UserOrdersUpdated += WebSocketClient_UserOrdersUpdated;

            _restApi = restApi ?? throw new ArgumentNullException(nameof(restApi));

            _spot = spot ?? throw new ArgumentNullException(nameof(spot));
            _spot.Executor.TxMempooled += Executor_TxMempooled;
            _spot.Executor.TxFailed += Executor_TxFailed;
            _spot.Executor.Error += Executor_Error;

            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));

            _ordersSync = new SemaphoreSlim(initialCount: 1, maxCount: 1);
            _canceledOrders = [];
            _pendingCancellationRequests = [];
            _pendingOrders = [];
            _pendingRequests = [];
            _activeOrders = [];
        }

        public List<Order> GetActiveOrders(bool pending = true)
        {
            try
            {
                _ordersSync.Wait();

                return pending
                    ? [.. _activeOrders.Values, .. _pendingOrders.Values.SelectMany(o => o)]
                    : [.. _activeOrders.Values];
            }
            finally
            {
                _ordersSync.Release();
            }
        }

        public List<Order> GetPendingOrders()
        {
            try
            {
                _ordersSync.Wait();

                return [.. _pendingOrders.Values.SelectMany(o => o)];
            }
            finally
            {
                _ordersSync.Release();
            }
        }

        public bool IsOrderCanceled(ulong orderId)
        {
            return _canceledOrders.ContainsKey(orderId);
        }

        public async Task OrderSendAsync(
            decimal price,
            decimal qty,
            Side side,
            bool marketOnly = false,
            bool postOnly = false,
            bool transferExecutedTokens = false,
            CancellationToken cancellationToken = default)
        {
            var fromAddress = _spot.Executor.Signer.GetAddress();

            // get native balance
            var (balance, balanceError) = await _rpc.GetBalanceAsync(
                fromAddress,
                BlockNumber.Pending,
                cancellationToken);

            if (balanceError != null)
            {
                _logger?.LogError(balanceError, "Get balance error");
                return;
            }

            // get max priority fee per gas
            var (maxPriorityFeePerGas, maxPriorityFeeError) = await _rpc.GetMaxPriorityFeePerGasAsync(
                cancellationToken);

            if (maxPriorityFeeError != null)
            {
                _logger?.LogError(maxPriorityFeeError, "Get max prirority fee per gas error");
                return;
            }

            var maxFeePerGas = maxPriorityFeePerGas + BASE_FEE_PER_GAS;
            var maxFee = maxFeePerGas * (_defaultGasLimits?.PlaceOrder ?? 0);

            if (balance < maxFee)
            {
                _logger?.LogError(
                    "[{@symbol}] Insufficient native token balance for PlaceOrder fee. " +
                        "Balance: {@balance}. " +
                        "Max fee: {@fee}.",
                    Symbol,
                    balance,
                    maxFee);
                return;
            }

            // get trader balance
            var (traderBalance, traderBalanceError) = await GetTraderBalanceAsync(fromAddress, cancellationToken);

            if (traderBalanceError != null)
            {
                _logger?.LogError(traderBalanceError, "Get trader balance error");
                return;
            }

            var contractBalance = side == Side.Sell
                ? traderBalance.TokenX * BigInteger.Pow(10, _symbolConfig.ScallingFactorX)
                : traderBalance.TokenY * BigInteger.Pow(10, _symbolConfig.ScallingFactorY);

            if (!TryNormalizePrice(price, out var normalizedPrice))
                throw new Exception($"Invalid significant digits count or size for price {price}");

            if (!TryNormalizeQty(qty, out var normalizedQty))
                throw new Exception($"Invalid qty {qty}");

            var inputAmount = side == Side.Sell
                ? normalizedQty * BigInteger.Pow(10, _symbolConfig.ScallingFactorX)
                : normalizedQty * normalizedPrice * BigInteger.Pow(10, _symbolConfig.ScallingFactorY);

            var fromToken = side == Side.Sell
                ? _symbolConfig.TokenX
                : _symbolConfig.TokenY;

            var isFromNative = _symbolConfig.UseNative && fromToken.IsNative;

            if (isFromNative)
            {
                if (balance + contractBalance < inputAmount + maxFee)
                {
                    _logger?.LogError(
                        "[{@symbol}] Insufficient native token balance for PlaceOrder. " +
                        "Balance: {@balance}. " +
                        "Contract balance: {@contractBalance}. " +
                        "Input amount: {@inputAmount}. " +
                        "Max fee: {@maxFee}.",
                        Symbol,
                        balance,
                        contractBalance,
                        inputAmount,
                        maxFee);
                    return;
                }
            }
            else
            {
                var (tokenBalance, tokenBalanceError) = await _rpc.GetErc20TokenBalanceAsync(
                    fromToken.ContractAddress,
                    fromAddress,
                    BlockNumber.Pending,
                    cancellationToken);

                if (tokenBalanceError != null)
                {
                    _logger?.LogError(tokenBalanceError, "Get token balance error");
                    return;
                }

                if (tokenBalance + contractBalance < inputAmount)
                {
                    _logger?.LogError(
                        "[{@symbol}] Insufficient token balance for PlaceOrder. " +
                        "Token balance: {@balance}. " +
                        "Contract balance: {@contractBalance}. " +
                        "Input amount: {@inputAmount}.",
                        Symbol,
                        tokenBalance,
                        contractBalance,
                        inputAmount);
                    return;
                }
            }

            var expiration = DateTimeOffset.UtcNow.AddSeconds(DEFAULT_EXPIRED_SEC).ToUnixTimeSeconds();

            var nativeTokenValue = isFromNative
                ? inputAmount > contractBalance
                    ? inputAmount - contractBalance
                    : 0
                : 0;

            var placeOrderRequestId = await _spot.PlaceOrderAsync(new PlaceOrderParams
            {
                IsAsk = side == Side.Sell,
                Price = normalizedPrice,
                Quantity = normalizedQty,
                MaxCommission = UINT128_MAX_VALUE,
                MarketOnly = marketOnly,
                PostOnly = postOnly,
                TransferExecutedTokens = transferExecutedTokens,
                Expires = expiration,

                Value = nativeTokenValue,
                ContractAddress = _symbolConfig.ContractAddress.ToLowerInvariant(),
                MaxFeePerGas = maxFeePerGas,
                MaxPriorityFeePerGas = maxPriorityFeePerGas,
                GasLimit = _defaultGasLimits?.PlaceOrder,
                EstimateGas = true,
                EstimateGasReserveInPercent = ESTIMATE_GAS_RESERVE_IN_PERCENTS,
                TransactionType = EIP1559_TRANSACTION_TYPE
            }, cancellationToken);

            var pendingOrder = new Order(
                OrderId: placeOrderRequestId,
                Price: price,
                Qty: qty,
                LeaveQty: qty,
                ClaimedQty: 0,
                Side: side,
                Symbol: Symbol,
                Status: OrderStatus.Pending,
                Type: OrderType.Return,
                Created: DateTimeOffset.UtcNow,
                LastChanged: DateTimeOffset.UtcNow,
                TxnHash: null);

            _pendingOrders.TryAdd(placeOrderRequestId, [pendingOrder]);
            _pendingRequests.TryAdd(placeOrderRequestId, true);

            _logger?.LogDebug(
                "[{@symbol}] Add {@side} pending order request with id {@id}",
                Symbol, side, placeOrderRequestId);
        }

        public async Task<bool> OrderCancelAsync(
            ulong orderId,
            bool transferTokens = false,
            CancellationToken cancellationToken = default)
        {
            // return false if order has already been canceled
            if (!_canceledOrders.TryAdd(orderId, DateTimeOffset.UtcNow))
                return false;

            var fromAddress = _spot.Executor.Signer.GetAddress();

            // get native balance
            var (balance, balanceError) = await _rpc.GetBalanceAsync(
                fromAddress,
                BlockNumber.Pending,
                cancellationToken);

            if (balanceError != null)
            {
                _logger?.LogError(balanceError, "Get balance error");
                return false;
            }

            // get max priority fee per gas
            var (maxPriorityFeePerGas, maxPriorityFeeError) = await _rpc.GetMaxPriorityFeePerGasAsync(
                cancellationToken);

            if (maxPriorityFeeError != null)
            {
                _logger?.LogError(maxPriorityFeeError, "Get max prirority fee per gas error");
                return false;
            }

            var maxFeePerGas = maxPriorityFeePerGas + BASE_FEE_PER_GAS;
            var maxFee = maxFeePerGas * (_defaultGasLimits?.ClaimOrder ?? 0);

            if (balance < maxFee)
            {
                _logger?.LogError(
                    "[{@symbol}] Insufficient native token balance for ClaimOrder fee. " +
                        "Balance: {@balance}. " +
                        "Max fee: {@fee}.",
                    Symbol,
                    balance,
                    maxFee);
                return false;
            }

            var expiration = DateTimeOffset.UtcNow.AddSeconds(DEFAULT_EXPIRED_SEC).ToUnixTimeSeconds();

            var claimOrderRequestId = await _spot.ClaimOrderAsync(new ClaimOrderParams
            {
                OrderId = orderId,
                TransferTokens = transferTokens,
                Expires = expiration,

                ContractAddress = _symbolConfig.ContractAddress.ToLowerInvariant(),
                MaxFeePerGas = maxFeePerGas,
                MaxPriorityFeePerGas = maxPriorityFeePerGas,
                GasLimit = _defaultGasLimits?.ClaimOrder,
                EstimateGas = true,
                EstimateGasReserveInPercent = ESTIMATE_GAS_RESERVE_IN_PERCENTS
            }, cancellationToken);

            _pendingCancellationRequests.TryAdd(claimOrderRequestId, [orderId.ToString()]);
            _pendingRequests.TryAdd(claimOrderRequestId, true);

            _logger?.LogDebug(
                "[{@symbol}] Add cancellation request with id {@id} and orderId {@orderId}",
                Symbol,
                claimOrderRequestId,
                orderId);

            return true;
        }

        public async Task<bool> OrderModifyAsync(
            ulong orderId,
            decimal price,
            decimal qty,
            bool postOnly = false,
            bool transferTokens = false,
            CancellationToken cancellationToken = default)
        {
            // return false if order has already been canceled
            if (orderId > 1 && !_canceledOrders.TryAdd(orderId, DateTimeOffset.UtcNow))
                return false;

            var fromAddress = _spot.Executor.Signer.GetAddress();

            // get native balance
            var (balance, balanceError) = await _rpc.GetBalanceAsync(
                fromAddress,
                BlockNumber.Pending,
                cancellationToken);

            if (balanceError != null)
            {
                _logger?.LogError(balanceError, "Get balance error");
                return false;
            }

            // get max priority fee per gas
            var (maxPriorityFeePerGas, maxPriorityFeeError) = await _rpc.GetMaxPriorityFeePerGasAsync(
                cancellationToken);

            if (maxPriorityFeeError != null)
            {
                _logger?.LogError(maxPriorityFeeError, "Get max prirority fee per gas error");
                return false;
            }

            var maxFeePerGas = maxPriorityFeePerGas + BASE_FEE_PER_GAS;
            var maxFee = maxFeePerGas * (_defaultGasLimits?.ChangeOrder ?? 0);

            if (balance < maxFee)
            {
                _logger?.LogError(
                    "[{@symbol}] Insufficient native token balance for ChangeOrder fee. " +
                        "Balance: {@balance}. " +
                        "Max fee: {@fee}.",
                    Symbol,
                    balance,
                    maxFee);
                return false;
            }

            if (!TryNormalizePrice(price, out var normalizedPrice))
                throw new Exception($"Invalid significant digits count or size for price {price}");

            if (!TryNormalizeQty(qty, out var normalizedQty))
                throw new Exception($"Invalid qty {qty}");

            var side = orderId.GetSideFromOrderId();
            var nativeTokenValue = BigInteger.Zero;

            if (qty > 0)
            {
                var previousLeaveAmount = BigInteger.Zero;

                if (orderId > 1)
                {
                    var order = _activeOrders[orderId.ToString()];

                    if (!TryNormalizePrice(order.Price, out var previousNormalizedPrice))
                        throw new Exception($"Invalid significant digits count or size for price {order.Price}");

                    if (!TryNormalizeQty(order.LeaveQty, out var previousNormalizedQty))
                        throw new Exception($"Invalid leaveqty {order.LeaveQty}");

                    previousLeaveAmount = side == Side.Sell
                        ? previousNormalizedQty * BigInteger.Pow(10, _symbolConfig.ScallingFactorX)
                        : previousNormalizedQty * previousNormalizedPrice * BigInteger.Pow(10, _symbolConfig.ScallingFactorY);
                }

                // get trader balance
                var (traderBalance, traderBalanceError) = await GetTraderBalanceAsync(fromAddress, cancellationToken);

                if (traderBalanceError != null)
                {
                    _logger?.LogError(traderBalanceError, "Get trader balance error");
                    return false;
                }

                var contractBalance = side == Side.Sell
                    ? traderBalance.TokenX * BigInteger.Pow(10, _symbolConfig.ScallingFactorX)
                    : traderBalance.TokenY * BigInteger.Pow(10, _symbolConfig.ScallingFactorY);

                var inputAmount = side == Side.Sell
                    ? normalizedQty * BigInteger.Pow(10, _symbolConfig.ScallingFactorX)
                    : normalizedQty * normalizedPrice * BigInteger.Pow(10, _symbolConfig.ScallingFactorY);

                var fromToken = side == Side.Sell
                    ? _symbolConfig.TokenX
                    : _symbolConfig.TokenY;

                var isFromNative = _symbolConfig.UseNative && fromToken.IsNative;

                if (isFromNative)
                {
                    if (balance + contractBalance + previousLeaveAmount < inputAmount + maxFee)
                    {
                        _logger?.LogError(
                            "[{@symbol}] Insufficient native token balance for ChangeOrder. " +
                            "Balance: {@balance}. " +
                            "Contract balance: {@contractBalance}. " +
                            "Previous leave amount: {@previousLeaveAmount}. " +
                            "Input amount: {@inputAmount}. " +
                            "Max fee: {@maxFee}.",
                            Symbol,
                            balance,
                            contractBalance,
                            previousLeaveAmount,
                            inputAmount,
                            maxFee);
                        return false;
                    }
                }
                else
                {
                    var (tokenBalance, tokenBalanceError) = await _rpc.GetErc20TokenBalanceAsync(
                        fromToken.ContractAddress,
                        fromAddress,
                        BlockNumber.Pending,
                        cancellationToken);

                    if (tokenBalanceError != null)
                    {
                        _logger?.LogError(tokenBalanceError, "Get token balance error");
                        return false;
                    }

                    if (tokenBalance + contractBalance + previousLeaveAmount < inputAmount)
                    {
                        _logger?.LogError(
                            "[{@symbol}] Insufficient token balance for ChangeOrder. " +
                            "Token balance: {@balance}. " +
                            "Contract balance: {@contractBalance}. " +
                            "Previous leave amount: {@previousLeaveAmount}. " +
                            "Input amount: {@inputAmount}.",
                            Symbol,
                            tokenBalance,
                            contractBalance,
                            previousLeaveAmount,
                            inputAmount);
                        return false;
                    }
                }

                nativeTokenValue = isFromNative
                    ? inputAmount > contractBalance + previousLeaveAmount
                        ? inputAmount - contractBalance - previousLeaveAmount
                        : 0
                    : 0;
            }

            var expiration = DateTimeOffset.UtcNow.AddSeconds(DEFAULT_EXPIRED_SEC).ToUnixTimeSeconds();

            var changeOrderRequestId = await _spot.ChangeOrderAsync(new ChangeOrderParams
            {
                OldOrderId = orderId,
                NewPrice = normalizedPrice,
                NewQuantity = normalizedQty,
                PostOnly = postOnly,
                TransferTokens = transferTokens,
                Expires = expiration,

                Value = nativeTokenValue,
                ContractAddress = _symbolConfig.ContractAddress.ToLowerInvariant(),
                MaxFeePerGas = maxFeePerGas,
                MaxPriorityFeePerGas = maxPriorityFeePerGas,
                GasLimit = _defaultGasLimits?.ChangeOrder,
                EstimateGas = true,
                EstimateGasReserveInPercent = ESTIMATE_GAS_RESERVE_IN_PERCENTS,
                TransactionType = EIP1559_TRANSACTION_TYPE
            }, cancellationToken);

            if (orderId > 1)
                _pendingCancellationRequests.TryAdd(changeOrderRequestId, [orderId.ToString()]);

            if (qty > 0)
            {
                var pendingOrder = new Order(
                    OrderId: changeOrderRequestId,
                    Price: price,
                    Qty: qty,
                    LeaveQty: qty,
                    ClaimedQty: 0,
                    Side: side,
                    Symbol: Symbol,
                    Status: OrderStatus.Pending,
                    Type: OrderType.Return,
                    Created: DateTimeOffset.UtcNow,
                    LastChanged: DateTimeOffset.UtcNow,
                    TxnHash: null);

                _pendingOrders.TryAdd(changeOrderRequestId, [pendingOrder]);
            }

            _pendingRequests.TryAdd(changeOrderRequestId, true);

            _logger?.LogDebug(
                "[{@symbol}] Add change order request with id {@id} and orderId {@orderId}",
                Symbol, changeOrderRequestId, orderId);

            return true;
        }

        public async Task<bool> PendingOrderCancelAsync(
            string placeOrderRequestId,
            CancellationToken cancellationToken = default)
        {
            var isCanceled = await _spot.Executor.TryCancelRequestAsync(
                placeOrderRequestId,
                cancellationToken);

            if (isCanceled)
            {
                if (!_pendingOrders.Remove(placeOrderRequestId, out var pendingOrders))
                    return true;

                _logger?.LogDebug(
                    "[{@symbol}] {@count} pending orders removed for request id {@id} before tx sending",
                    Symbol, pendingOrders.Count, placeOrderRequestId);

                pendingOrders = [.. pendingOrders.Select(o => o with { Status = OrderStatus.CanceledAndClaimed })];

                OrdersChanged?.Invoke(this, pendingOrders);
            }
            else
            {
                // the transaction is already in the mempool and we need to wait for the orderId or tx failure
            }

            return isCanceled;
        }

        public async Task BatchAsync(
            IEnumerable<IOnchainClobRequest> requests,
            bool postOnly = false,
            bool transferTokens = false,
            CancellationToken cancellationToken = default)
        {
            // try cancel pending orders
            foreach (var request in requests.OfType<CancelPendingOrderRequest>())
                await PendingOrderCancelAsync(request.RequestId, cancellationToken);

            // remove pending order cancellation requests and sort by priority
            requests = requests
                .Where(r => r is not CancelPendingOrderRequest)
                .OrderByDescending(r => r.Priority);

            var fromAddress = _spot.Executor.Signer.GetAddress();

            // get max priority fee per gas
            var (maxPriorityFeePerGas, maxPriorityFeeError) = await _rpc.GetMaxPriorityFeePerGasAsync(
                cancellationToken);

            if (maxPriorityFeeError != null)
            {
                _logger?.LogError(maxPriorityFeeError, "Get max prirority fee per gas error");
                return;
            }

            var maxFeePerGas = maxPriorityFeePerGas + BASE_FEE_PER_GAS;

            foreach (var (batchGasLimit, batchRequests) in SplitIntoSeveralBatches(requests))
            {
                var orderIds = batchRequests.Select(r => r.OrderId).ToList();

                var prices = batchRequests
                    .Select(r =>
                    {
                        if (!TryNormalizePrice(r.Price, out var normalizedPrice))
                            throw new Exception($"Invalid significant digits count or size for price {r.Price}");
                        return (BigInteger)normalizedPrice;
                    })
                    .ToList();

                var qtys = batchRequests
                    .Select(r =>
                    {
                        if (!TryNormalizeQty(r.Qty, out var normalizedQty))
                            throw new Exception($"Invalid qty {r.Qty}");
                        return normalizedQty;
                    })
                    .ToList();

                var maxFee = maxFeePerGas * batchGasLimit ?? 0;

                // get native balance
                var (balance, balanceError) = await _rpc.GetBalanceAsync(
                    fromAddress,
                    BlockNumber.Pending,
                    cancellationToken);

                if (balanceError != null)
                {
                    _logger?.LogError(balanceError, "Get balance error");
                    return;
                }

                if (balance < maxFee)
                {
                    _logger?.LogError(
                        "[{@symbol}] Insufficient native token balance for BatchChangeOrder fee. " +
                            "Balance: {@balance}. " +
                            "Max fee: {@fee}.",
                        Symbol,
                        balance,
                        maxFee);
                    return;
                }

                var nativeTokenValue = BigInteger.Zero;

                if (qtys.Any(q => q > 0))
                {
                    // get trader balance
                    var (traderBalance, traderBalanceError) = await GetTraderBalanceAsync(fromAddress, cancellationToken);

                    if (traderBalanceError != null)
                    {
                        _logger?.LogError(traderBalanceError, "Get trader balance error");
                        return;
                    }

                    // check balances for both sides and calculate native token value
                    foreach (var side in new Side[]{Side.Buy, Side.Sell})
                    {
                        var previousLeaveAmount = orderIds
                            .Where(orderId => orderId > 0)
                            .Select(orderId => _activeOrders[orderId.ToString()])
                            .Where(order => order.Side == side)
                            .Select(order => {
                                if (!TryNormalizePrice(order.Price, out var normalizedPrice))
                                    throw new Exception($"Invalid significant digits count or size for price {order.Price}");

                                if (!TryNormalizeQty(order.LeaveQty, out var normalizedQty))
                                    throw new Exception($"Invalid leave qty {order.LeaveQty}");

                                return side == Side.Sell
                                    ? normalizedQty * BigInteger.Pow(10, _symbolConfig.ScallingFactorX)
                                    : normalizedQty * normalizedPrice * BigInteger.Pow(10, _symbolConfig.ScallingFactorY);
                            })
                            .Aggregate(BigInteger.Zero, (acc, value) => acc + value);

                        var contractBalance = side == Side.Sell
                            ? traderBalance.TokenX * BigInteger.Pow(10, _symbolConfig.ScallingFactorX)
                            : traderBalance.TokenY * BigInteger.Pow(10, _symbolConfig.ScallingFactorY);

                        var inputAmount = orderIds
                            .Select((orderId, index) => {
                                if (qtys[index] > 0) {
                                    return side == Side.Sell
                                        ? qtys[index] * BigInteger.Pow(10, _symbolConfig.ScallingFactorX)
                                        : qtys[index] * prices[index] * BigInteger.Pow(10, _symbolConfig.ScallingFactorY);
                                }
                                else return BigInteger.Zero;
                            }).Aggregate(BigInteger.Zero, (acc, value) => acc + value);

                        // if there is no input amount for this side, skip it
                        if (inputAmount == 0)
                            continue;

                        var fromToken = side == Side.Sell
                            ? _symbolConfig.TokenX
                            : _symbolConfig.TokenY;

                        var isFromNative = _symbolConfig.UseNative && fromToken.IsNative;

                        if (isFromNative)
                        {
                            if (balance + contractBalance + previousLeaveAmount < inputAmount + maxFee)
                            {
                                _logger?.LogError(
                                    "[{@symbol}] Insufficient native token balance for BatchChangeOrder. " +
                                    "Balance: {@balance}. " +
                                    "Contract balance: {@contractBalance}. " +
                                    "Previous leave amount: {@previousLeaveAmount}. " +
                                    "Input amount: {@inputAmount}. " +
                                    "Max fee: {@maxFee}.",
                                    Symbol,
                                    balance,
                                    contractBalance,
                                    previousLeaveAmount,
                                    inputAmount,
                                    maxFee);
                                return;
                            }
                        }
                        else
                        {
                            var (tokenBalance, tokenBalanceError) = await _rpc.GetErc20TokenBalanceAsync(
                                fromToken.ContractAddress,
                                fromAddress,
                                BlockNumber.Pending,
                                cancellationToken);

                            if (tokenBalanceError != null)
                            {
                                _logger?.LogError(tokenBalanceError, "Get token balance error");
                                return;
                            }

                            if (tokenBalance + contractBalance + previousLeaveAmount < inputAmount)
                            {
                                _logger?.LogError(
                                    "[{@symbol}] Insufficient token balance for ChangeOrder. " +
                                    "Token balance: {@balance}. " +
                                    "Contract balance: {@contractBalance}. " +
                                    "Previous leave amount: {@previousLeaveAmount}. " +
                                    "Input amount: {@inputAmount}.",
                                    Symbol,
                                    tokenBalance,
                                    contractBalance,
                                    previousLeaveAmount,
                                    inputAmount);
                                return;
                            }
                        }

                        nativeTokenValue += isFromNative
                            ? inputAmount > contractBalance + previousLeaveAmount
                                ? inputAmount - contractBalance - previousLeaveAmount
                                : 0
                            : 0;
                    }
                }

                var expiration = DateTimeOffset.UtcNow.AddSeconds(DEFAULT_EXPIRED_SEC).ToUnixTimeSeconds();

                var batchChangeOrderRequestId = await _spot.BatchChangeOrderAsync(new BatchChangeOrderParams
                {
                    OrderIds = orderIds,
                    Prices = prices,
                    Quantities = qtys,
                    MaxCommissionPerOrder = UINT128_MAX_VALUE,
                    PostOnly = postOnly,
                    TransferTokens = transferTokens,
                    Expires = expiration,

                    Value = nativeTokenValue,
                    ContractAddress = _symbolConfig.ContractAddress.ToLowerInvariant(),
                    MaxFeePerGas = maxFeePerGas,
                    MaxPriorityFeePerGas = maxPriorityFeePerGas,
                    GasLimit = batchGasLimit,
                    EstimateGas = true,
                    EstimateGasReserveInPercent = ESTIMATE_GAS_RESERVE_IN_PERCENTS,
                    TransactionType = EIP1559_TRANSACTION_TYPE
                }, cancellationToken);

                var pendingOrders = new List<Order>();
                var pendingCancellationRequests = new List<string>();

                foreach (var request in batchRequests)
                {
                    if (request is PlaceOrderRequest placeOrderRequest)
                    {
                        var pendingOrder = new Order(
                            OrderId: batchChangeOrderRequestId,
                            Price: placeOrderRequest.Price,
                            Qty: placeOrderRequest.Qty,
                            LeaveQty: placeOrderRequest.Qty,
                            ClaimedQty: 0,
                            Side: placeOrderRequest.Side,
                            Symbol: Symbol,
                            Status: OrderStatus.Pending,
                            Type: OrderType.Return,
                            Created: DateTimeOffset.UtcNow,
                            LastChanged: DateTimeOffset.UtcNow,
                            TxnHash: null);

                        pendingOrders.Add(pendingOrder);
                    }
                    else if (request is ChangeOrderRequest changeOrderRequest)
                    {
                        if (changeOrderRequest.Qty > 0)
                        {
                            var pendingOrder = new Order(
                                OrderId: batchChangeOrderRequestId,
                                Price: changeOrderRequest.Price,
                                Qty: changeOrderRequest.Qty,
                                LeaveQty: changeOrderRequest.Qty,
                                ClaimedQty: 0,
                                Side: changeOrderRequest.OrderId.GetSideFromOrderId(),
                                Symbol: Symbol,
                                Status: OrderStatus.Pending,
                                Type: OrderType.Return,
                                Created: DateTimeOffset.UtcNow,
                                LastChanged: DateTimeOffset.UtcNow,
                                TxnHash: null);

                            pendingOrders.Add(pendingOrder);
                        }

                        if (changeOrderRequest.OrderId > 1)
                            pendingCancellationRequests.Add(changeOrderRequest.OrderId.ToString());
                    }
                    else if (request is ClaimOrderRequest claimOrderRequest)
                    {
                        pendingCancellationRequests.Add(claimOrderRequest.OrderId.ToString());
                    }
                }

                if (pendingOrders.Count > 0)
                    _pendingOrders.TryAdd(batchChangeOrderRequestId, pendingOrders);

                if (pendingCancellationRequests.Count > 0)
                    _pendingCancellationRequests.TryAdd(batchChangeOrderRequestId, pendingCancellationRequests);

                _pendingRequests.TryAdd(batchChangeOrderRequestId, true);

                _logger?.LogDebug(
                    "[{@symbol}] Add batch change order request with id {@id}",
                    Symbol, batchChangeOrderRequestId);
            }
        }

        public async Task<Result<GetTraderBalanceOutput>> GetTraderBalanceAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            var getTraderBalance = new GetTraderBalance()
            {
                Address = address
            };

            var (hexResult, error) = await _rpc.CallAsync<string>(
                to: _symbolConfig.ContractAddress,
                from: address,
                input: getTraderBalance.CreateTransactionInput(_symbolConfig.ContractAddress).Data,
                cancellationToken: cancellationToken);

            if (error != null)
                return error;

            return new GetTraderBalanceOutput().DecodeOutput(hexResult);
        }

        private async void Executor_TxMempooled(object sender, MempooledEventArgs e)
        {
            while (!_pendingRequests.TryRemove(e.RequestId, out _))
                await Task.Delay(PENDING_CALLS_CHECK_INTERVAL_MS);

            try
            {
                await _ordersSync.WaitAsync();

                if (!_pendingOrders.Remove(e.RequestId, out var pendingOrders))
                {
                    _logger?.LogError(
                        "[{@symbol}] pending orders for request id {@id} should have " +
                            "been removed but are missing",
                        Symbol, e.RequestId);

                    return;
                }

                _logger?.LogDebug(
                    "[{@symbol}] {@count} pending orders removed for request id {@id}",
                    Symbol, pendingOrders.Count, e.RequestId);

                pendingOrders = [.. pendingOrders
                    .Select(o => o with
                    {
                        OrderId = e.TxId,
                        TxnHash = e.TxId,
                        Status = OrderStatus.Mempooled
                    })];

                _pendingOrders.TryAdd(e.TxId, pendingOrders);

                _logger?.LogDebug(
                    "[{@symbol}] Add pending orders for txId {@txId}",
                    Symbol, e.TxId);
            }
            finally
            {
                _ordersSync.Release();
            }

            if (_pendingCancellationRequests.TryRemove(e.RequestId, out var orderIds))
            {
                if (!_pendingCancellationRequests.TryAdd(e.TxId, orderIds))
                {
                    _logger?.LogError(
                        "[{@symbol}] Cannot add cancellation request with txId {id}",
                        _symbolConfig.Symbol, e.TxId);
                }
            }
        }

        private async void Executor_TxFailed(object sender, ConfirmedEventArgs e)
        {
            try
            {
                await _ordersSync.WaitAsync();

                // if the transaction fails, try removing pending orders if exist
                if (_pendingOrders.TryRemove(e.Receipt.TransactionHash, out var pendingOrders))
                {
                    _logger?.LogDebug(
                        "[{@symbol}] {@count} pending orders removed for txId {@txId} after tx failing",
                        _symbolConfig.Symbol, pendingOrders.Count, e.Receipt.TransactionHash);
                }
            }
            finally
            {
                _ordersSync.Release();
            }

            // if the transaction fails, try removing pending cancellation requests if exist
            if (_pendingCancellationRequests.TryRemove(e.Receipt.TransactionHash, out var orderIds))
            {
                // delete the entry that the order has already been canceled
                foreach (var orderId in orderIds)
                    _canceledOrders.TryRemove(ulong.Parse(orderId), out var _);
            }
        }

        private async void Executor_Error(object sender, ErrorEventArgs e)
        {
            try
            {
                await _ordersSync.WaitAsync();

                // if the transaction fails, try removing pending orders if exist
                if (_pendingOrders.Remove(e.RequestId, out var pendingOrders))
                {
                    _logger?.LogDebug(
                        "[{@symbol}] {@count} pending orders removed for request with id {@id} " +
                            "after tx sending fail",
                        _symbolConfig.Symbol, pendingOrders.Count, e.RequestId);
                }
            }
            finally
            {
                _ordersSync.Release();
            }

            // if the transaction fails, try removing pending cancellation requests if exist
            if (_pendingCancellationRequests.TryRemove(e.RequestId, out var orderIds))
            {
                // delete the entry that the order has already been canceled
                foreach (var orderId in orderIds)
                    _canceledOrders.TryRemove(ulong.Parse(orderId), out var _);
            }
        }

        private void WebSocketClient_Disconnected(object? sender, EventArgs e)
        {
            IsAvailable = false;

            StopUserOrdersHandlerTask();
            _activeOrders.Clear();
        }

        private void WebSocketClient_StateStatusChanged(object? sender, StateStatus status)
        {
            if (status == StateStatus.Sync)
            {
                _logger?.LogInformation("Client syncing...");

                IsAvailable = false;

                StopUserOrdersHandlerTask();
                _activeOrders.Clear();
                return;
            }

            _logger?.LogInformation("Client ready. Subscribe to channels");

            StartUserOrdersHandlerTask();

            _webSocketClient.SubscribeUserOrdersChannel(
                userAddress: _spot.Executor.Signer.GetAddress(),
                marketId: _symbolConfig.ContractAddress.ToLowerInvariant());
        }

        private void WebSocketClient_UserOrdersUpdated(
            object? sender,
            UserOrdersEventArgs args)
        {
            if (_userOrdersChannel == null)
            {
                _logger?.LogError(
                    "[{@symbol}] User orders channel not initialized",
                    _symbolConfig.Symbol);

                return;
            }

            if (args.MarketId != ALL_MARKETS &&
                !args.MarketId.Equals(_symbolConfig.ContractAddress, StringComparison.InvariantCultureIgnoreCase))
            {
                // skip updates for other markets
                return;
            }

            if (!_userOrdersChannel.Writer.TryWrite(args))
            {
                _logger?.LogError("Can't write user orders events to channel");
            }
        }

        private void StopUserOrdersHandlerTask()
        {
            try
            {
                if (_userOrdersHandlerCts != null && !_userOrdersHandlerCts.IsCancellationRequested)
                    _userOrdersHandlerCts.Cancel();
            }
            catch
            {
                // nothing to do...
            }
        }

        private void StartUserOrdersHandlerTask()
        {
            _userOrdersChannel = Channel.CreateUnbounded<UserOrdersEventArgs>(new UnboundedChannelOptions
            {
                SingleWriter = true,
                SingleReader = true
            });

            _userOrdersHandlerCts = new CancellationTokenSource();

            // run user orders handler task in background
            _ = Task.Run(() => UserOrdersHandlerLoopAsync(
                _userOrdersChannel,
                _userOrdersHandlerCts.Token), _userOrdersHandlerCts.Token);
        }

        private async Task UserOrdersHandlerLoopAsync(
            Channel<UserOrdersEventArgs> channel,
            CancellationToken cancellationToken)
        {
            try
            {
                while (await channel.Reader.WaitToReadAsync(cancellationToken))
                {
                    while (channel.Reader.TryRead(out var args))
                    {
                        await UserOrdersHandlerAsync(args, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // task canceled
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "User orders events handler error");
            }
        }

        private async Task UserOrdersHandlerAsync(
            UserOrdersEventArgs args,
            CancellationToken cancellationToken)
        {
            var orders = args.UserOrders
                .Where(o => o.Market.Id.Equals(
                    _symbolConfig.ContractAddress,
                    StringComparison.InvariantCultureIgnoreCase))
                .Select(o => o.ToOrder(_symbolConfig.Symbol, _symbolConfig.PricePrecision))
                .ToList();

            foreach (var order in orders)
            {
                _logger?.LogDebug("[{@symbol}] Receive order update:\n{@order}",
                    _symbolConfig.Symbol,
                    new {
                        orderId = order.OrderId,
                        txId = order.TxnHash,
                        status = order.Status,
                        side = order.Side,
                        price = order.Price
                    });
            }

            await SaveOrdersAsync(orders, cancellationToken);

            // if snapshot
            if (args.IsSnapshot)
            {
                // request active orders from api
                var (activeOrders, error) = await _restApi.GetActiveOrdersAsync(
                    _spot.Executor.Signer.GetAddress(),
                    _symbolConfig.ContractAddress.ToLowerInvariant(),
                    cancellationToken: cancellationToken);

                if (error != null)
                {
                    _logger?.LogError(
                        error,
                        "[{@symbol}] Get active orders snapshot error",
                        _symbolConfig.Symbol);

                    return;
                }

                if (activeOrders == null)
                {
                    _logger?.LogError(
                        "[{@symbol}] Get active orders snapshot error",
                        _symbolConfig.Symbol);

                    return;
                }

                await SaveOrdersAsync(
                    activeOrders.Select(
                        o => o.ToOrder(_symbolConfig.Symbol, _symbolConfig.PricePrecision)),
                    cancellationToken);

                IsAvailable = _webSocketClient.IsConnected && _webSocketClient.StateStatus == StateStatus.Ready;
            }

            OrdersChanged?.Invoke(this, orders);
        }

        private async Task SaveOrdersAsync(
            IEnumerable<Order> orders,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await _ordersSync.WaitAsync(cancellationToken);

                foreach (var order in orders)
                {
                    if (order.IsActive)
                    {
                        _activeOrders[order.OrderId] = order;
                    }
                    else
                    {
                        // remove history order from active orders
                        _activeOrders.Remove(order.OrderId, out var _);

                        // try to remove canceled order entry after some delay
                        _ = Task.Delay(CANCELED_ORDERS_TTL_MS)
                            .ContinueWith(t =>
                            {
                                if (ulong.TryParse(order.OrderId, out var orderId))
                                    _canceledOrders.TryRemove(orderId, out _);
                            });
                    }

                    // try remove pending orders if exists
                    if (_pendingOrders.Remove(order.TxnHash!, out var pendingOrders))
                    {
                        _logger?.LogInformation(
                            "[{@symbol}] {@count} pending orders removed for txId: {@txId}",
                            _symbolConfig.Symbol, pendingOrders.Count, order.TxnHash);
                    }
                }
            }
            finally
            {
                _ordersSync.Release();
            }
        }

        private bool TryNormalizePrice(decimal price, out ulong normalizedPrice)
        {
            normalizedPrice = price.ToNormalizePrice(_symbolConfig.PricePrecision, out var rest);

            return rest == 0;
        }

        private bool TryNormalizeQty(decimal qty, out BigInteger normalizedQty)
        {
            var multiplier = BigInteger.Pow(10, _symbolConfig.TokenX.Decimals - _symbolConfig.ScallingFactorX);

            normalizedQty = qty.Multiply(multiplier);

            return normalizedQty.Divide(multiplier) == qty;
        }

        private IEnumerable<(ulong?, IEnumerable<IOnchainClobRequest>)> SplitIntoSeveralBatches(IEnumerable<IOnchainClobRequest> requests)
        {
            if (_defaultGasLimits == null)
            {
                yield return (null, requests);
                yield break;
            }

            var totalGasLimit = 0ul;
            var batch = new List<IOnchainClobRequest>();

            foreach (var request in requests)
            {
                var gasLimit = request switch
                {
                    PlaceOrderRequest => _defaultGasLimits.PlaceOrder,
                    ClaimOrderRequest => _defaultGasLimits.ClaimOrder,
                    ChangeOrderRequest => _defaultGasLimits.ChangeOrder,
                    _ => 0ul
                };

                if (totalGasLimit + gasLimit > _defaultGasLimits.MaxPerTransaction)
                {
                    yield return (totalGasLimit, batch);

                    totalGasLimit = gasLimit;
                    batch = [request];
                }
                else
                {
                    totalGasLimit += gasLimit;
                    batch.Add(request);
                }
            }

            yield return (totalGasLimit, batch);
        }
    }
}
