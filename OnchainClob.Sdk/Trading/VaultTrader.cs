using Microsoft.Extensions.Logging;
using OnchainClob.Client;
using OnchainClob.Client.Configuration;
using OnchainClob.Client.Events;
using OnchainClob.Client.Vault;
using OnchainClob.Common;
using OnchainClob.MarketData.PythHermes;
using OnchainClob.Trading.Abstract;
using OnchainClob.Trading.Requests;
using Revelium.Evm.Rpc;
using System.Numerics;

namespace OnchainClob.Trading
{
    public class VaultTrader : Trader, IVaultTrader
    {
        private const long BASE_FEE_PER_GAS = 200_000_000_000;
        private readonly BigInteger UINT128_MAX_VALUE = (BigInteger.One << 128) - 1;
        private const long DEFAULT_EXPIRED_SEC = 60 * 60 * 24;
        private const int EIP1559_TRANSACTION_TYPE = 2;
        private const int ESTIMATE_GAS_RESERVE_IN_PERCENTS = 10;

        public event EventHandler<VaultTotalValuesEventArgs>? VaultTotalValuesChanged;

        private readonly string _vaultContractAddress;
        private readonly string _batchContractAddress;
        private readonly Vault _vault;
        private readonly BalanceManager _balanceManager;
        private readonly RpcClient _rpc;
        private readonly Pyth _pyth;
        private readonly GasLimits? _defaultGasLimits;

        protected override string UserAddress => _vaultContractAddress;
        protected IVaultSymbolConfig VaultSymbolConfig => (IVaultSymbolConfig)_symbolConfig;

        public VaultTrader(
            string vaultContractAddress,
            string batchContractAddress,
            IVaultSymbolConfig symbolConfig,
            OnchainClobWsClient webSocketClient,
            OnchainClobRestApi restApi,
            Vault vault,
            BalanceManager balanceManager,
            RpcClient rpc,
            Pyth pyth,
            GasLimits? defaultGasLimits = null,
            ILogger<VaultTrader>? logger = null)
            : base(
                symbolConfig,
                webSocketClient,
                restApi,
                vault.Executor,
                logger)
        {
            _vaultContractAddress = vaultContractAddress
                ?? throw new ArgumentNullException(nameof(vaultContractAddress));
            _batchContractAddress = batchContractAddress
                ?? throw new ArgumentNullException(nameof(batchContractAddress));
            _defaultGasLimits = defaultGasLimits;

            _webSocketClient.VaultTotalValuesUpdated += WebSocketClient_VaultTotalValuesUpdated;

            _vault = vault ?? throw new ArgumentNullException(nameof(vault));
            _balanceManager = balanceManager ?? throw new ArgumentNullException(nameof(balanceManager));
            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
            _pyth = pyth ?? throw new ArgumentNullException(nameof(pyth));
        }

        public override async Task OrderSendAsync(
            BigInteger price,
            BigInteger qty,
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

            if (balances.NativeBalance < maxFee + _pyth.PriceUpdateFee)
            {
                _logger?.LogError(
                    "[{@symbol}] Insufficient native token balance for PlaceOrder fee. " +
                        "Native balance: {@nativeBalance}. " +
                        "Max fee: {@fee}. " +
                        "Price update fee: {@priceUpdateFee}.",
                    Symbol,
                    balances.NativeBalance.ToString(),
                    maxFee.ToString(),
                    _pyth.PriceUpdateFee.ToString());

                return;
            }

            var inputAmount = GetInputAmount(side, price, qty);

            if (!CheckBalance(side, balances, BigInteger.Zero, inputAmount))
                return;

            var expiration = DateTimeOffset.UtcNow.AddSeconds(DEFAULT_EXPIRED_SEC).ToUnixTimeSeconds();

            var (priceUpdateData, priceUpdateDataError) = await _pyth.GetPriceUpdateDataAsync();

            if (priceUpdateDataError != null)
            {
                _logger?.LogWarning(
                    priceUpdateDataError,
                    "[{@symbol}] Get price update data error",
                    Symbol);
            }

            var priceUpdateFee = priceUpdateData != null ? _pyth.PriceUpdateFee : 0;

            var placeOrderRequestId = await _vault.PlaceOrderAsync(new PlaceOrderParams
            {
                LobId = VaultSymbolConfig.LobId,
                IsAsk = side == Side.Sell,
                Price = price,
                Quantity = qty,
                MaxCommission = UINT128_MAX_VALUE,
                MarketOnly = marketOnly,
                PostOnly = postOnly,
                Expires = expiration,
                PriceUpdateData = priceUpdateData ?? [],

                Value = priceUpdateFee,
                ContractAddress = _vaultContractAddress.ToLowerInvariant(),
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

            var claimOrderRequestId = await _vault.ClaimOrderAsync(new ClaimOrderParams
            {
                LobId = VaultSymbolConfig.LobId,
                OrderId = orderId,
                Expires = expiration,

                ContractAddress = _vaultContractAddress.ToLowerInvariant(),
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

        public override Task<bool> OrderModifyAsync(
            ulong orderId,
            BigInteger price,
            BigInteger qty,
            bool postOnly = false,
            bool transferTokens = false,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("OrderModify not supported");
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
                var selectedRequests = batchRequests.ToList();

                var orderIds = selectedRequests.Select(r => r.OrderId).ToList();
                var prices = selectedRequests.Select(r => r.Price).ToList();
                var qtys = selectedRequests.Select(r => r.Qty).ToList();
                var maxFee = maxFeePerGas * batchGasLimit ?? 0;

                _logger?.LogDebug("[{@symbol}] Batching {@count} requests. " +
                    "Order ids: {@orderIds}, " +
                    "Prices: {@prices}, " +
                    "Qtys: {@qtys}, " +
                    "Max fee: {@maxFee}",
                    Symbol,
                    selectedRequests.Count,
                    string.Join(", ", orderIds),
                    string.Join(", ", prices),
                    string.Join(", ", qtys),
                    maxFee.ToString());

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

                if (balances.NativeBalance < maxFee + _pyth.PriceUpdateFee)
                {
                    _logger?.LogError(
                        "[{@symbol}] Insufficient native token balance for BatchChangeOrder fee. " +
                            "Native balance: {@balance}. " +
                            "Max fee: {@fee}. " +
                            "Price update fee: {@priceUpdateFee}.",
                        Symbol,
                        balances.NativeBalance.ToString(),
                        maxFee.ToString(),
                        _pyth.PriceUpdateFee.ToString());

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
                        {
                            // skip order places for side
                            for (var i = selectedRequests.Count - 1; i >= 0; i--)
                            {
                                if (selectedRequests[i].Qty > 0 &&
                                    selectedRequests[i].OrderId.GetSideFromOrderId() == side)
                                {
                                    _logger?.LogDebug("[{@symbol}] Remove from batch {@side} order place with " +
                                        "price {@price} and qty {@qty} due to insufficient token balance",
                                        Symbol,
                                        side,
                                        prices[i].ToString(),
                                        qtys[i].ToString());

                                    selectedRequests.RemoveAt(i);
                                    orderIds.RemoveAt(i);
                                    qtys.RemoveAt(i);
                                    prices.RemoveAt(i);
                                }
                            }
                        }
                    }
                }

                if (selectedRequests.Count == 0)
                {
                    _logger?.LogDebug("[{@symbol}] All requests filtered after balance check", Symbol);
                    return;
                }

                var expiration = DateTimeOffset.UtcNow.AddSeconds(DEFAULT_EXPIRED_SEC).ToUnixTimeSeconds();

                var (priceUpdateData, priceUpdateDataError) = await _pyth.GetPriceUpdateDataAsync();

                if (priceUpdateDataError != null)
                {
                    _logger?.LogWarning(
                        priceUpdateDataError,
                        "[{@symbol}] Get price update data error",
                        Symbol);
                }

                var priceUpdateFee = priceUpdateData != null ? _pyth.PriceUpdateFee : 0;

                var batchChangeOrderRequestId = await _vault.BatchChangeOrderAsync(new BatchChangeOrderParams
                {
                    LpManagerAddress = _vaultContractAddress.ToLowerInvariant(),
                    LobId = VaultSymbolConfig.LobId,
                    OrderIds = orderIds,
                    Prices = prices,
                    Quantities = qtys,
                    MaxCommissionPerOrder = UINT128_MAX_VALUE,
                    PostOnly = postOnly,
                    Expires = expiration,
                    PriceUpdateData = priceUpdateData ?? [],

                    Value = priceUpdateFee,
                    ContractAddress = _batchContractAddress.ToLowerInvariant(),
                    MaxFeePerGas = maxFeePerGas,
                    MaxPriorityFeePerGas = maxPriorityFeePerGas,
                    GasLimit = batchGasLimit,
                    EstimateGas = true,

                    EstimateGasReserveInPercent = ESTIMATE_GAS_RESERVE_IN_PERCENTS,
                    TransactionType = EIP1559_TRANSACTION_TYPE,
                    ChainId = _rpc.ChainId
                }, cancellationToken);

                var (pendingOrders, pendingCancellationRequests) = CreatePendingOrdersAndCancellationRequests(
                    selectedRequests,
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
                    balances.GetTokenBalanceBySide(side).ToString(),
                    previousLeaveAmount.ToString(),
                    inputAmount.ToString());

                return false;
            }

            return true;
        }

        protected override void SubscribeToChannels()
        {
            _webSocketClient.SubscribeUserOrdersChannel(
                _vaultContractAddress.ToLowerInvariant(),
                _symbolConfig.ContractAddress.ToLowerInvariant());

            _webSocketClient.SubscribeVaultTotalValuesChannel();
        }

        private void WebSocketClient_VaultTotalValuesUpdated(object sender, VaultTotalValuesEventArgs e)
        {
            VaultTotalValuesChanged?.Invoke(this, e);
        }
    }
}
