using Microsoft.Extensions.Logging;
using OnchainClob.Client;
using OnchainClob.Client.Configuration;
using OnchainClob.Client.Lob;
using OnchainClob.Common;
using OnchainClob.Trading.Abstract;
using OnchainClob.Trading.Requests;
using Revelium.Evm.Rpc;
using System.Numerics;

namespace OnchainClob.Trading
{
    public class LobTrader(
        ISymbolConfig symbolConfig,
        WebSocketClient webSocketClient,
        RestApi restApi,
        Lob lob,
        BalanceManager balanceManager,
        RpcClient rpc,
        GasLimits? defaultGasLimits = null,
        ILogger<LobTrader>? logger = null) : Trader(
            symbolConfig,
            webSocketClient,
            restApi,
            lob.Executor,
            logger)
    {
        private const long BASE_FEE_PER_GAS = 200_000_000_000;
        private readonly BigInteger UINT128_MAX_VALUE = (BigInteger.One << 128) - 1;
        private const long DEFAULT_EXPIRED_SEC = 60 * 60 * 24;
        private const int EIP1559_TRANSACTION_TYPE = 2;
        private const int ESTIMATE_GAS_RESERVE_IN_PERCENTS = 10;

        private readonly Lob _lob = lob ?? throw new ArgumentNullException(nameof(lob));
        private readonly BalanceManager _balanceManager = balanceManager ?? throw new ArgumentNullException(nameof(balanceManager));
        private readonly RpcClient _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
        private readonly GasLimits? _defaultGasLimits = defaultGasLimits;

        protected override string UserAddress => _lob.Executor.Signer.GetAddress();

        public override async Task OrderSendAsync(
            decimal price,
            decimal qty,
            Side side,
            bool marketOnly = false,
            bool postOnly = false,
            bool transferExecutedTokens = false,
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

            if (balances.NativeBalance < maxFee)
            {
                _logger?.LogError(
                    "[{@symbol}] Insufficient native token balance for PlaceOrder fee. " +
                        "Native balance: {@nativeBalance}. " +
                        "Max fee: {@fee}.",
                    Symbol,
                    balances.NativeBalance.ToString(),
                    maxFee.ToString());
                return;
            }

            if (!TryNormalizePrice(price, out var normalizedPrice))
                throw new Exception($"Invalid significant digits count or size for price {price}");

            if (!TryNormalizeQty(qty, out var normalizedQty))
                throw new Exception($"Invalid qty {qty}");

            var inputAmount = GetInputAmount(side, normalizedPrice, normalizedQty);
            var isFromNative = _symbolConfig.UseNative && fromToken.IsNative;

            if (!CheckBalance(side, balances, maxFee, BigInteger.Zero, inputAmount, isFromNative))
                return;

            var expiration = DateTimeOffset.UtcNow.AddSeconds(DEFAULT_EXPIRED_SEC).ToUnixTimeSeconds();

            var nativeTokenValue = isFromNative
                ? inputAmount > balances.GetLobBalanceBySide(side)
                    ? inputAmount - balances.GetLobBalanceBySide(side)
                    : 0
                : 0;

            var placeOrderRequestId = await _lob.PlaceOrderAsync(new PlaceOrderParams
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
                TransactionType = EIP1559_TRANSACTION_TYPE,
                ChainId = _rpc.ChainId
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
                Symbol,
                side,
                placeOrderRequestId);
        }

        public override async Task<bool> OrderCancelAsync(
            ulong orderId,
            bool transferTokens = false,
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
                    balance.ToString(),
                    maxFee.ToString());
                return false;
            }

            var expiration = DateTimeOffset.UtcNow.AddSeconds(DEFAULT_EXPIRED_SEC).ToUnixTimeSeconds();

            var claimOrderRequestId = await _lob.ClaimOrderAsync(new ClaimOrderParams
            {
                OrderId = orderId,
                TransferTokens = transferTokens,
                Expires = expiration,

                ContractAddress = _symbolConfig.ContractAddress.ToLowerInvariant(),
                MaxFeePerGas = maxFeePerGas,
                MaxPriorityFeePerGas = maxPriorityFeePerGas,
                GasLimit = _defaultGasLimits?.ClaimOrder,
                EstimateGas = true,
                EstimateGasReserveInPercent = ESTIMATE_GAS_RESERVE_IN_PERCENTS,
                TransactionType = EIP1559_TRANSACTION_TYPE,
                ChainId = _rpc.ChainId
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

        public override async Task<bool> OrderModifyAsync(
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

            var side = orderId.GetSideFromOrderId();
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
            var maxFee = maxFeePerGas * (_defaultGasLimits?.ChangeOrder ?? 0);

            if (balances.NativeBalance < maxFee)
            {
                _logger?.LogError(
                    "[{@symbol}] Insufficient native token balance for ChangeOrder fee. " +
                        "Native balance: {@nativeBalance}. " +
                        "Max fee: {@fee}.",
                    Symbol,
                    balances.NativeBalance.ToString(),
                    maxFee.ToString());
                return false;
            }

            if (!TryNormalizePrice(price, out var normalizedPrice))
                throw new Exception($"Invalid significant digits count or size for price {price}");

            if (!TryNormalizeQty(qty, out var normalizedQty))
                throw new Exception($"Invalid qty {qty}");

            var nativeTokenValue = BigInteger.Zero;

            if (qty > 0)
            {
                var previousLeaveAmount = orderId > 1
                    ? GetPreviousLeaveAmount(_activeOrders[orderId.ToString()], side)
                    : BigInteger.Zero;

                var inputAmount = GetInputAmount(side, normalizedPrice, normalizedQty);
                var isFromNative = _symbolConfig.UseNative && fromToken.IsNative;

                if (!CheckBalance(side, balances, maxFee, previousLeaveAmount, inputAmount, isFromNative))
                    return false;

                nativeTokenValue = isFromNative
                    ? inputAmount > balances.GetLobBalanceBySide(side) + previousLeaveAmount
                        ? inputAmount - balances.GetLobBalanceBySide(side) - previousLeaveAmount
                        : 0
                    : 0;
            }

            var expiration = DateTimeOffset.UtcNow.AddSeconds(DEFAULT_EXPIRED_SEC).ToUnixTimeSeconds();

            var changeOrderRequestId = await _lob.ChangeOrderAsync(new ChangeOrderParams
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
                TransactionType = EIP1559_TRANSACTION_TYPE,
                ChainId = _rpc.ChainId
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
                Symbol,
                changeOrderRequestId,
                orderId);

            return true;
        }

        public override async Task BatchAsync(
            IEnumerable<ITraderRequest> requests,
            bool postOnly = false,
            bool transferTokens = false,
            CancellationToken cancellationToken = default)
        {
            // try cancel pending orders
            foreach (var request in requests.OfType<CancelPendingOrderRequest>())
                await PendingOrderCancelAsync(request.RequestId, cancellationToken);

            // filter out pending cancellation requests and already canceled orders, then sort by priority
            requests = [.. requests
                .Where(r =>
                    r is not CancelPendingOrderRequest &&
                    (r is not ClaimOrderRequest || _canceledOrders.TryAdd(r.OrderId, DateTimeOffset.UtcNow)))
                .OrderByDescending(r => r.Priority)];

            if (!requests.Any())
            {
                _logger?.LogInformation("All requests filtered");
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

            foreach (var (batchGasLimit, batchRequests) in requests.SplitIntoSeveralBatches(_defaultGasLimits))
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
                    _logger?.LogError(balancesError, "Get available balances error");
                    return;
                }

                if (balances.NativeBalance < maxFee)
                {
                    _logger?.LogError(
                        "[{@symbol}] Insufficient native token balance for BatchChangeOrder fee. " +
                            "Native balance: {@balance}. " +
                            "Max fee: {@fee}.",
                        Symbol,
                        balances.NativeBalance.ToString(),
                        maxFee.ToString());
                    return;
                }

                var nativeTokenValue = BigInteger.Zero;

                if (qtys.Any(q => q > 0))
                {
                    // check balances for both sides and calculate native token value
                    foreach (var side in new Side[] { Side.Buy, Side.Sell })
                    {
                        var previousLeaveAmount = GetPreviousLeaveAmount(orderIds, side);
                        var inputAmount = GetInputAmount(orderIds, prices, qtys, side);

                        // if there is no input amount for this side, skip it
                        if (inputAmount == 0)
                            continue;

                        var fromToken = side == Side.Sell
                            ? _symbolConfig.TokenX
                            : _symbolConfig.TokenY;

                        var isFromNative = _symbolConfig.UseNative && fromToken.IsNative;

                        if (!CheckBalance(side, balances, maxFee, previousLeaveAmount, inputAmount, isFromNative))
                            return;

                        nativeTokenValue += isFromNative
                            ? inputAmount > balances.GetLobBalanceBySide(side) + previousLeaveAmount
                                ? inputAmount - balances.GetLobBalanceBySide(side) - previousLeaveAmount
                                : 0
                            : 0;
                    }
                }

                var expiration = DateTimeOffset.UtcNow.AddSeconds(DEFAULT_EXPIRED_SEC).ToUnixTimeSeconds();

                var batchChangeOrderRequestId = await _lob.BatchChangeOrderAsync(new BatchChangeOrderParams
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
                    TransactionType = EIP1559_TRANSACTION_TYPE,
                    ChainId = _rpc.ChainId
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
                    Symbol,
                    batchChangeOrderRequestId);
            }
        }

        public async Task DepositAsync(
            BigInteger amountTokenX,
            BigInteger amountTokenY,
            CancellationToken cancellationToken = default)
        {
            // get native balance
            var (balance, balanceError) = await _balanceManager.GetNativeBalanceAsync(
                forceUpdate: true,
                cancellationToken);

            if (balanceError != null)
            {
                _logger?.LogError(balanceError, "[{@symbol}] Get native balance error", Symbol);
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
            var maxFee = maxFeePerGas * (_defaultGasLimits?.Deposit ?? 0);

            if (balance < maxFee)
            {
                _logger?.LogError(
                    "[{@symbol}] Insufficient native token balance for Deposit fee. " +
                        "Balance: {@balance}. " +
                        "Max fee: {@fee}.",
                    Symbol,
                    balance.ToString(),
                    maxFee.ToString());
                return;
            }

            var _ = await _lob.DepositTokensAsync(new DepositTokensParams
            {
                TokenXAmount = amountTokenX,
                TokenYAmount = amountTokenY,

                ContractAddress = _symbolConfig.ContractAddress.ToLowerInvariant(),
                MaxFeePerGas = maxFeePerGas,
                MaxPriorityFeePerGas = maxPriorityFeePerGas,
                GasLimit = _defaultGasLimits?.Deposit,
                EstimateGas = true,
                EstimateGasReserveInPercent = ESTIMATE_GAS_RESERVE_IN_PERCENTS,
                TransactionType = EIP1559_TRANSACTION_TYPE,
                ChainId = _rpc.ChainId
            }, cancellationToken);
        }

        protected override void SubscribeToChannels()
        {
            _webSocketClient.SubscribeUserOrdersChannel(
                userAddress: _lob.Executor.Signer.GetAddress(),
                marketId: _symbolConfig.ContractAddress.ToLowerInvariant());
        }

        private bool CheckBalance(
            Side side,
            Balances balances,
            BigInteger maxFee,
            BigInteger previousLeaveAmount,
            BigInteger inputAmount,
            bool isFromNative)
        {
            if (isFromNative && (balances.NativeBalance + balances.GetLobBalanceBySide(side) + previousLeaveAmount < inputAmount + maxFee))
            {
                _logger?.LogError(
                    "[{@symbol}] Insufficient native token balance for operation. " +
                    "Native balance: {@nativeBalance}. " +
                    "Lob balance: {@lobBalance}. " +
                    "Previous leave amount: {@previousLeaveAmount}. " +
                    "Input amount: {@inputAmount}. " +
                    "Max fee: {@maxFee}.",
                    Symbol,
                    balances.NativeBalance.ToString(),
                    balances.GetLobBalanceBySide(side).ToString(),
                    previousLeaveAmount.ToString(),
                    inputAmount.ToString(),
                    maxFee.ToString());

                return false;
            }
            else if (!isFromNative && (balances.GetTokenBalanceBySide(side) + balances.GetLobBalanceBySide(side) + previousLeaveAmount < inputAmount))
            {
                _logger?.LogError(
                    "[{@symbol}] Insufficient token balance for operation. " +
                    "Token balance: {@balance}. " +
                    "Lob balance: {@lobBalance}. " +
                    "Previous leave amount: {@previousLeaveAmount}. " +
                    "Input amount: {@inputAmount}.",
                    Symbol,
                    balances.GetTokenBalanceBySide(side).ToString(),
                    balances.GetLobBalanceBySide(side).ToString(),
                    previousLeaveAmount.ToString(),
                    inputAmount.ToString());

                return false;
            }

            return true;
        }
    }
}
