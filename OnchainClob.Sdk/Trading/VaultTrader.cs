﻿using Microsoft.Extensions.Logging;
using OnchainClob.Client;
using OnchainClob.Client.Configuration;
using OnchainClob.Client.Events;
using OnchainClob.Client.Vault;
using OnchainClob.Common;
using OnchainClob.Trading.Abstract;
using OnchainClob.Trading.Events;
using OnchainClob.Trading.Requests;
using Revelium.Evm.Common;
using Revelium.Evm.Rpc;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading.Channels;
using ErrorEventArgs = OnchainClob.Trading.Events.ErrorEventArgs;

namespace OnchainClob.Trading
{
    public class VaultTrader : IVaultTrader
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

        private readonly string _vaultContractAddress;
        private readonly string _batchContractAddress;
        private readonly IVaultSymbolConfig _symbolConfig;
        private readonly WebSocketClient _webSocketClient;
        private readonly RestApi _restApi;
        private readonly Vault _vault;
        private readonly BalanceManager _balanceManager;
        private readonly RpcClient _rpc;
        private readonly GasLimits? _defaultGasLimits;
        private readonly ILogger<VaultTrader>? _logger;
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

        public VaultTrader(
            string vaultContractAddress,
            string batchContractAddress,
            IVaultSymbolConfig symbolConfig,
            WebSocketClient webSocketClient,
            RestApi restApi,
            Vault vault,
            BalanceManager balanceManager,
            RpcClient rpc,
            GasLimits? defaultGasLimits = null,
            ILogger<VaultTrader>? logger = null)
        {
            _vaultContractAddress = vaultContractAddress
                ?? throw new ArgumentNullException(nameof(vaultContractAddress));
            _batchContractAddress = batchContractAddress
                ?? throw new ArgumentNullException(nameof(batchContractAddress));
            _symbolConfig = symbolConfig ?? throw new ArgumentNullException(nameof(symbolConfig));
            _defaultGasLimits = defaultGasLimits;
            _logger = logger;

            _webSocketClient = webSocketClient ?? throw new ArgumentNullException(nameof(webSocketClient));
            _webSocketClient.Disconnected += WebSocketClient_Disconnected;
            _webSocketClient.StateStatusChanged += WebSocketClient_StateStatusChanged;
            _webSocketClient.UserOrdersUpdated += WebSocketClient_UserOrdersUpdated;

            _restApi = restApi ?? throw new ArgumentNullException(nameof(restApi));

            _vault = vault ?? throw new ArgumentNullException(nameof(vault));
            _vault.Executor.TxMempooled += Executor_TxMempooled;
            _vault.Executor.TxFailed += Executor_TxFailed;
            _vault.Executor.Error += Executor_Error;

            _balanceManager = balanceManager ?? throw new ArgumentNullException(nameof(balanceManager));
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
            CancellationToken cancellationToken = default)
        {
            var fromToken = side == Side.Sell
                ? _symbolConfig.TokenX
                : _symbolConfig.TokenY;

            var (balances, balancesError) = await _balanceManager.GetAvailableBalancesAsync(
                _symbolConfig.ContractAddress,
                fromToken.ContractAddress,
                forceUpdate: true,
                cancellationToken);

            if (balancesError != null)
            {
                _logger?.LogError(balancesError, "[{@symbol}] Get available balances error", Symbol);
                return;
            }

            // get max priority fee per gas
            var (maxPriorityFeePerGas, maxPriorityFeeError) = await _rpc.GetMaxPriorityFeePerGasAsync(
                cancellationToken);

            if (maxPriorityFeeError != null)
            {
                _logger?.LogError(maxPriorityFeeError, "[{@symbol}] Get max prirority fee per gas error", Symbol);
                return;
            }

            var maxFeePerGas = maxPriorityFeePerGas + BASE_FEE_PER_GAS;
            var maxFee = maxFeePerGas * (_defaultGasLimits?.PlaceOrder ?? 0);

            // TODO: also check pyth price update fee
            if (balances.NativeBalance < maxFee)
            {
                _logger?.LogError(
                    "[{@symbol}] Insufficient native token balance for PlaceOrder fee. " +
                        "Native balance: {@nativeBalance}. " +
                        "Max fee: {@fee}.",
                    Symbol,
                    balances.NativeBalance,
                    maxFee);
                return;
            }

            if (!TryNormalizePrice(price, out var normalizedPrice))
                throw new Exception($"Invalid significant digits count or size for price {price}");

            if (!TryNormalizeQty(qty, out var normalizedQty))
                throw new Exception($"Invalid qty {qty}");

            var inputAmount = GetInputAmount(side, normalizedPrice, normalizedQty);

            if (!CheckBalance(side, balances, BigInteger.Zero, inputAmount))
                return;

            var expiration = DateTimeOffset.UtcNow.AddSeconds(DEFAULT_EXPIRED_SEC).ToUnixTimeSeconds();

            var placeOrderRequestId = await _vault.PlaceOrderAsync(new PlaceOrderParams
            {
                LobId = _symbolConfig.LobId,
                IsAsk = side == Side.Sell,
                Price = normalizedPrice,
                Quantity = normalizedQty,
                MaxCommission = UINT128_MAX_VALUE,
                MarketOnly = marketOnly,
                PostOnly = postOnly,
                Expires = expiration,
                PriceUpdateData = [], // TODO: get price update data (if needed)

                Value = 0, // TODO: value for pyth price update fee (if needed)
                ContractAddress = _vaultContractAddress.ToLowerInvariant(),
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
            CancellationToken cancellationToken = default)
        {
            // return false if order has already been canceled
            if (!_canceledOrders.TryAdd(orderId, DateTimeOffset.UtcNow))
                return false;

            // get native balance
            var (balance, balanceError) = await _balanceManager.GetNativeBalanceAsync(
                forceUpdate: true,
                cancellationToken);

            if (balanceError != null)
            {
                _logger?.LogError(balanceError, "[{@symbol}] Get native balance error", Symbol);
                return false;
            }

            // get max priority fee per gas
            var (maxPriorityFeePerGas, maxPriorityFeeError) = await _rpc.GetMaxPriorityFeePerGasAsync(
                cancellationToken);

            if (maxPriorityFeeError != null)
            {
                _logger?.LogError(maxPriorityFeeError, "[{@symbol}] Get max prirority fee per gas error", Symbol);
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

            var claimOrderRequestId = await _vault.ClaimOrderAsync(new ClaimOrderParams
            {
                LobId = _symbolConfig.LobId,
                OrderId = orderId,
                Expires = expiration,

                ContractAddress = _vaultContractAddress.ToLowerInvariant(),
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

        public async Task BatchAsync(
            IEnumerable<ITraderRequest> requests,
            bool postOnly = false,
            CancellationToken cancellationToken = default)
        {
            // try cancel pending orders
            foreach (var request in requests.OfType<CancelPendingOrderRequest>())
                await PendingOrderCancelAsync(request.RequestId, cancellationToken);

            // remove pending order cancellation requests and sort by priority
            requests = requests
                .Where(r => r is not CancelPendingOrderRequest)
                .OrderByDescending(r => r.Priority);

            // get max priority fee per gas
            var (maxPriorityFeePerGas, maxPriorityFeeError) = await _rpc.GetMaxPriorityFeePerGasAsync(
                cancellationToken);

            if (maxPriorityFeeError != null)
            {
                _logger?.LogError(maxPriorityFeeError, "[{@symbol}] Get max prirority fee per gas error", Symbol);
                return;
            }

            var maxFeePerGas = maxPriorityFeePerGas + BASE_FEE_PER_GAS;

            foreach (var (batchGasLimit, batchRequests) in SplitIntoSeveralBatches(requests))
            {
                var orderIds = batchRequests.Select(r => r.OrderId).ToList();
                var prices = GetNormalizedPrices(batchRequests);
                var qtys = GetNormalizedQtys(batchRequests);
                var maxFee = maxFeePerGas * batchGasLimit ?? 0;

                var (balances, balancesError) = await _balanceManager.GetAvailableBalancesAsync(
                    _symbolConfig.ContractAddress,
                    tokenContractAddress: null,
                    forceUpdate: true,
                    cancellationToken);

                if (balancesError != null)
                {
                    _logger?.LogError(balancesError, "[{@symbol}] Get available balances error", Symbol);
                    return;
                }

                if (balances.NativeBalance < maxFee)
                {
                    _logger?.LogError(
                        "[{@symbol}] Insufficient native token balance for BatchChangeOrder fee. " +
                            "Native balance: {@balance}. " +
                            "Max fee: {@fee}.",
                        Symbol,
                        balances.NativeBalance,
                        maxFee);
                    return;
                }

                if (qtys.Any(q => q > 0))
                {
                    // check balances for both sides
                    foreach (var side in new Side[] { Side.Buy, Side.Sell })
                    {
                        var previousLeaveAmount = GetPreviousLeaveAmount(orderIds, side);
                        var inputAmount = GetInputAmount(orderIds, prices, qtys, side);

                        // if there is no input amount for this side, skip it
                        if (inputAmount == 0)
                            continue;

                        if (!CheckBalance(side, balances, previousLeaveAmount, inputAmount))
                            return;
                    }
                }

                var expiration = DateTimeOffset.UtcNow.AddSeconds(DEFAULT_EXPIRED_SEC).ToUnixTimeSeconds();

                var batchChangeOrderRequestId = await _vault.BatchChangeOrderAsync(new BatchChangeOrderParams
                {
                    LpManagerAddress = _vaultContractAddress.ToLowerInvariant(),
                    LobId = _symbolConfig.LobId,
                    OrderIds = orderIds,
                    Prices = prices,
                    Quantities = qtys,
                    MaxCommissionPerOrder = UINT128_MAX_VALUE,
                    PostOnly = postOnly,
                    Expires = expiration,
                    PriceUpdateData = [], // TODO: get price update data (if needed)

                    Value = 0, // TODO: value for pyth price update fee (if needed)
                    ContractAddress = _batchContractAddress.ToLowerInvariant(),
                    MaxFeePerGas = maxFeePerGas,
                    MaxPriorityFeePerGas = maxPriorityFeePerGas,
                    GasLimit = batchGasLimit,
                    EstimateGas = true,
                    EstimateGasReserveInPercent = ESTIMATE_GAS_RESERVE_IN_PERCENTS,
                    TransactionType = EIP1559_TRANSACTION_TYPE
                }, cancellationToken);

                var (pendingOrders, pendingCancellationRequests) = CreatePendingOrdersAndCancellationRequests(
                    batchRequests,
                    batchChangeOrderRequestId);

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

        public async Task<bool> PendingOrderCancelAsync(
            string placeOrderRequestId,
            CancellationToken cancellationToken = default)
        {
            var isCanceled = await _vault.Executor.TryCancelRequestAsync(
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

        private bool CheckBalance(
            Side side,
            Balances balances,
            BigInteger previousLeaveAmount,
            BigInteger inputAmount)
        {
            if (balances.GetTokenBalanceBySide(side) + previousLeaveAmount < inputAmount)
            {
                _logger?.LogError(
                    "[{@symbol}] Insufficient token balance for operation. " +
                    "Token balance: {@balance}. " +
                    "Previous leave amount: {@previousLeaveAmount}. " +
                    "Input amount: {@inputAmount}.",
                    Symbol,
                    balances.GetTokenBalanceBySide(side),
                    previousLeaveAmount,
                    inputAmount);

                return false;
            }

            return true;
        }

        private BigInteger GetInputAmount(Side side, BigInteger normalizedPrice, BigInteger normalizedQty)
        {
            return side == Side.Sell
                ? normalizedQty * BigInteger.Pow(10, _symbolConfig.ScallingFactorX)
                : normalizedQty * normalizedPrice * BigInteger.Pow(10, _symbolConfig.ScallingFactorY);
        }

        private BigInteger GetInputAmount(
            List<ulong> orderIds,
            List<BigInteger> prices,
            List<BigInteger> qtys,
            Side side)
        {
            return orderIds
                .Select((orderId, index) => qtys[index] > 0
                    ? GetInputAmount(side, prices[index], qtys[index])
                    : BigInteger.Zero)
                .Aggregate(BigInteger.Zero, (acc, value) => acc + value);
        }

        private List<BigInteger> GetNormalizedQtys(IEnumerable<ITraderRequest> batchRequests)
        {
            return batchRequests
                .Select(r =>
                {
                    if (!TryNormalizeQty(r.Qty, out var normalizedQty))
                        throw new Exception($"Invalid qty {r.Qty}");
                    return normalizedQty;
                })
                .ToList();
        }

        private List<BigInteger> GetNormalizedPrices(IEnumerable<ITraderRequest> batchRequests)
        {
            return batchRequests
                .Select(r =>
                {
                    if (!TryNormalizePrice(r.Price, out var normalizedPrice))
                        throw new Exception($"Invalid significant digits count or size for price {r.Price}");
                    return normalizedPrice;
                })
                .ToList();
        }

        private BigInteger GetPreviousLeaveAmount(List<ulong> orderIds, Side side)
        {
            return orderIds
                .Where(orderId => orderId > 1)
                .Select(orderId => _activeOrders[orderId.ToString()])
                .Where(order => order.Side == side)
                .Select(order => GetPreviousLeaveAmount(order, side))
                .Aggregate(BigInteger.Zero, (acc, value) => acc + value);
        }

        private BigInteger GetPreviousLeaveAmount(Order order, Side side)
        {
            if (!TryNormalizePrice(order.Price, out var normalizedPrice))
                throw new Exception($"Invalid significant digits count or size for price {order.Price}");

            if (!TryNormalizeQty(order.LeaveQty, out var normalizedQty))
                throw new Exception($"Invalid leaveqty {order.LeaveQty}");

            return GetInputAmount(side, normalizedPrice, normalizedQty);
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

        private (List<Order> pendingOrders, List<string> pendingCancellationRequests) CreatePendingOrdersAndCancellationRequests(
            IEnumerable<ITraderRequest> batchRequests,
            string batchChangeOrderRequestId)
        {
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

            return (pendingOrders, pendingCancellationRequests);
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
                _logger?.LogInformation("[{@symbol}] Client syncing...", Symbol);

                IsAvailable = false;

                StopUserOrdersHandlerTask();
                _activeOrders.Clear();
                return;
            }

            _logger?.LogInformation("[{@symbol}] Client ready. Subscribe to channels", Symbol);

            StartUserOrdersHandlerTask();

            _webSocketClient.SubscribeUserOrdersChannel(
                userAddress: _vaultContractAddress.ToLowerInvariant(),
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
                _logger?.LogError("[{@symbol}] Can't write user orders events to channel", Symbol);
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
                _logger?.LogError(e, "[{@symbol}] User orders events handler error", Symbol);
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
                    _vault.Executor.Signer.GetAddress(),
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

        private bool TryNormalizePrice(decimal price, out BigInteger normalizedPrice)
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

        private IEnumerable<(ulong?, IEnumerable<ITraderRequest>)> SplitIntoSeveralBatches(
            IEnumerable<ITraderRequest> requests)
        {
            if (_defaultGasLimits == null)
            {
                yield return (null, requests);
                yield break;
            }

            var totalGasLimit = 0ul;
            var batch = new List<ITraderRequest>();

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
