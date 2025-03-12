using Microsoft.Extensions.Logging;
using Nethereum.Contracts;
using OnchainClob.Abi.Lob.Events;
using OnchainClob.Abi.Pyth;
using OnchainClob.Client;
using OnchainClob.Client.Configuration;
using OnchainClob.Client.Events;
using OnchainClob.Client.Vault;
using OnchainClob.Common;
using OnchainClob.Services;
using OnchainClob.Services.Pyth;
using OnchainClob.Trading.Abstract;
using OnchainClob.Trading.Events;
using OnchainClob.Trading.Requests;
using Revelium.Evm.Rpc;
using System.Numerics;

namespace OnchainClob.Trading
{
    public class VaultTrader : Trader, IVaultTrader
    {
        private const long MIN_BASE_FEE_PER_GAS = 200_000_000_000;
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
        private readonly GasStation _gasStation;
        private readonly GasLimits? _defaultGasLimits;

        protected override string UserAddress => _vaultContractAddress;
        protected IVaultSymbolConfig VaultSymbolConfig => (IVaultSymbolConfig)_symbolConfig;
        protected OnchainClobWsClient WsClient => (OnchainClobWsClient)_wsClient;
        public string VaultContractAddress => _vaultContractAddress.ToLowerInvariant();

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
            GasStation gasStation,
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

            WsClient.VaultTotalValuesUpdated += WebSocketClient_VaultTotalValuesUpdated;

            _vault = vault ?? throw new ArgumentNullException(nameof(vault));
            _vault.Executor.TxSuccessful += Executor_TxSuccessful;

            _balanceManager = balanceManager ?? throw new ArgumentNullException(nameof(balanceManager));
            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
            _pyth = pyth ?? throw new ArgumentNullException(nameof(pyth));
            _gasStation = gasStation ?? throw new ArgumentNullException(nameof(gasStation));
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
            var balances = _balanceManager.GetAvailableBalances(_symbolConfig);

            // get max priority fee per gas
            var (maxPriorityFeePerGas, maxPriorityFeeError) = _gasStation.GetMaxPriorityFeePerGas();

            if (maxPriorityFeeError != null)
            {
                _logger?.LogError(maxPriorityFeeError, "[{symbol}] Get max prirority fee per gas error", Symbol);
                return;
            }

            // get base fee per gas
            var (baseFeePerGas, baseFeePerGasError) = _gasStation.GetBaseFeePerGas();

            if (baseFeePerGasError != null)
            {
                _logger?.LogError(maxPriorityFeeError, "[{symbol}] Get base fee per gas error", Symbol);
                return;
            }

            var maxFeePerGas = maxPriorityFeePerGas + 2 * BigInteger.Max(baseFeePerGas, MIN_BASE_FEE_PER_GAS);
            var maxFee = maxFeePerGas * (_defaultGasLimits?.PlaceOrder ?? 0);

            if (balances.NativeBalance < maxFee + _pyth.PriceUpdateFee)
            {
                _logger?.LogError(
                    "[{symbol}] Insufficient native token balance for PlaceOrder fee. " +
                        "Native balance: {nativeBalance}. " +
                        "Max fee: {fee}. " +
                        "Price update fee: {priceUpdateFee}.",
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

            var priceUpdateData = _pyth.GetPriceUpdateData();
            var priceUpdateFee = priceUpdateData != null ? _pyth.PriceUpdateFee : 0;

            var requestId = Guid.NewGuid().ToString();
            _pendingRequests.TryAdd(requestId, new TaskCompletionSource<bool>());

            await _vault.PlaceOrderAsync(new PlaceOrderParams
            {
                RequestId = requestId,

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
                OrderId: requestId,
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

            _pendingOrders.TryAdd(requestId, [pendingOrder]);
            _pendingRequests[requestId].SetResult(true);

            _logger?.LogDebug(
                "[{symbol}] Add {side} pending order request with id {id}",
                Symbol,
                side,
                requestId);
        }

        public override async Task<bool> OrderCancelAsync(
            ulong orderId,
            bool transferTokens = false,
            CancellationToken cancellationToken = default)
        {
            var success = false;

            try
            {
                // return false if order has already been canceled
                if (!_canceledOrders.TryAdd(orderId, DateTimeOffset.UtcNow))
                {
                    success = true;
                    return false;
                }

                // get native balance
                var balance = _balanceManager.GetNativeBalance();

                // get max priority fee per gas
                var (maxPriorityFeePerGas, maxPriorityFeeError) = _gasStation.GetMaxPriorityFeePerGas();

                if (maxPriorityFeeError != null)
                {
                    _logger?.LogError(maxPriorityFeeError, "[{symbol}] Get max prirority fee per gas error", Symbol);
                    return false;
                }

                // get base fee per gas
                var (baseFeePerGas, baseFeePerGasError) = _gasStation.GetBaseFeePerGas();

                if (baseFeePerGasError != null)
                {
                    _logger?.LogError(maxPriorityFeeError, "[{symbol}] Get base fee per gas error", Symbol);
                    return false;
                }

                var maxFeePerGas = maxPriorityFeePerGas + 2 * BigInteger.Max(baseFeePerGas, MIN_BASE_FEE_PER_GAS);
                var maxFee = maxFeePerGas * (_defaultGasLimits?.ClaimOrder ?? 0);

                if (balance < maxFee)
                {
                    _logger?.LogError(
                        "[{symbol}] Insufficient native token balance for ClaimOrder fee. " +
                            "Balance: {balance}. " +
                            "Max fee: {fee}.",
                        Symbol,
                        balance.ToString(),
                        maxFee.ToString());

                    return false;
                }

                var expiration = DateTimeOffset.UtcNow.AddSeconds(DEFAULT_EXPIRED_SEC).ToUnixTimeSeconds();

                var requestId = Guid.NewGuid().ToString();
                _pendingRequests.TryAdd(requestId, new TaskCompletionSource<bool>());

                await _vault.ClaimOrderAsync(new ClaimOrderParams
                {
                    RequestId = requestId,

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

                success = true;

                _pendingCancellationRequests.TryAdd(requestId, [orderId.ToString()]);
                _pendingRequests[requestId].SetResult(true);

                _logger?.LogDebug(
                    "[{symbol}] Add cancellation request with id {id} and orderId {orderId}",
                    Symbol,
                    requestId,
                    orderId);

                return true;
            }
            catch
            {
                throw;
            }
            finally
            {
                if (!success)
                {
                    // mark order as not cancelled
                    _canceledOrders.TryRemove(orderId, out _);
                }
            }
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
            var success = false;

            try
            {
                // try cancel pending orders
                foreach (var request in requests.OfType<CancelPendingOrderRequest>())
                    await PendingOrderCancelAsync(request.RequestId, cancellationToken);

                // filter out pending cancellation requests and already canceled orders, then sort by priority
                requests = [.. requests
                    .Where(r => r is not CancelPendingOrderRequest)
                    .Where(r => r is not ClaimOrderRequest || _canceledOrders.TryAdd(r.OrderId, DateTimeOffset.UtcNow))
                    .OrderByDescending(r => r.Priority)];

                if (!requests.Any())
                {
                    _logger?.LogInformation("All requests filtered");
                    return;
                }

                // get max priority fee per gas
                var (maxPriorityFeePerGas, maxPriorityFeeError) = _gasStation.GetMaxPriorityFeePerGas();

                if (maxPriorityFeeError != null)
                {
                    _logger?.LogError(maxPriorityFeeError, "[{symbol}] Get max prirority fee per gas error", Symbol);
                    return;
                }

                // get base fee per gas
                var (baseFeePerGas, baseFeePerGasError) = _gasStation.GetBaseFeePerGas();

                if (baseFeePerGasError != null)
                {
                    _logger?.LogError(maxPriorityFeeError, "[{symbol}] Get base fee per gas error", Symbol);
                    return;
                }

                var maxFeePerGas = maxPriorityFeePerGas + 2 * BigInteger.Max(baseFeePerGas, MIN_BASE_FEE_PER_GAS);

                foreach (var (batchGasLimit, batchRequests) in requests.SplitIntoSeveralBatches(_defaultGasLimits))
                {
                    var selectedRequests = batchRequests.ToList();

                    var orderIds = selectedRequests.Select(r => r.OrderId).ToList();
                    var prices = selectedRequests.Select(r => r.Price).ToList();
                    var qtys = selectedRequests.Select(r => r.Qty).ToList();
                    var maxFee = maxFeePerGas * batchGasLimit ?? 0;

                    _logger?.LogDebug("[{symbol}] Batching {count} requests. " +
                        "Order ids: {orderIds}, " +
                        "Prices: {prices}, " +
                        "Qtys: {qtys}, " +
                        "Max fee: {maxFee}",
                        Symbol,
                        selectedRequests.Count,
                        string.Join(", ", orderIds),
                        string.Join(", ", prices),
                        string.Join(", ", qtys),
                        maxFee.ToString());

                    var balances = _balanceManager.GetAvailableBalances(_symbolConfig);

                    if (balances.NativeBalance < maxFee + _pyth.PriceUpdateFee)
                    {
                        _logger?.LogError(
                            "[{symbol}] Insufficient native token balance for BatchChangeOrder fee. " +
                                "Native balance: {balance}. " +
                                "Max fee: {fee}. " +
                                "Price update fee: {priceUpdateFee}.",
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
                                        _logger?.LogDebug("[{symbol}] Remove from batch {side} order place with " +
                                            "price {price} and qty {qty} due to insufficient token balance",
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
                        _logger?.LogDebug("[{symbol}] All requests filtered after balance check", Symbol);
                        return;
                    }

                    var expiration = DateTimeOffset.UtcNow.AddSeconds(DEFAULT_EXPIRED_SEC).ToUnixTimeSeconds();

                    var priceUpdateData = _pyth.GetPriceUpdateData();
                    var priceUpdateFee = priceUpdateData != null ? _pyth.PriceUpdateFee : 0;

                    var requestId = Guid.NewGuid().ToString();
                    _pendingRequests.TryAdd(requestId, new TaskCompletionSource<bool>());

                    await _vault.BatchChangeOrderAsync(new BatchChangeOrderParams
                    {
                        RequestId = requestId,

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

                    success = true;

                    var (pendingOrders, pendingCancellationRequests) = CreatePendingOrdersAndCancellationRequests(
                        selectedRequests,
                        requestId);

                    if (pendingOrders.Count > 0)
                        _pendingOrders.TryAdd(requestId, pendingOrders);

                    if (pendingCancellationRequests.Count > 0)
                        _pendingCancellationRequests.TryAdd(requestId, pendingCancellationRequests);

                    _pendingRequests[requestId].SetResult(true);

                    _logger?.LogDebug(
                        "[{symbol}] Add batch change order request with id {id}",
                        Symbol,
                        requestId);
                }
            }
            catch
            {
                throw;
            }
            finally
            {
                if (!success)
                {
                    // in case of an error, return the orders to the status of not cancelled
                    foreach (var request in requests)
                        if (request is ClaimOrderRequest claimOrderRequest)
                            _canceledOrders.TryRemove(claimOrderRequest.OrderId, out _);
                }
            }
        }

        private void Executor_TxSuccessful(object sender, ConfirmedEventArgs e)
        {
            var hasPriceFeedUpdates = e.Receipt.Logs
                .Where(l => l.Address.Equals(_pyth.PythContract, StringComparison.InvariantCultureIgnoreCase))
                .Where(l =>
                    l.Topics[0].Equals($"0x{PriceFeedUpdateEventDTO.SignatureHash}", StringComparison.InvariantCultureIgnoreCase))
                .ToList()
                .Any();

            if (hasPriceFeedUpdates)
            {
                const int TX_CONFIRMATION_DELAY_SEC = 5;
                _pyth.LastContractUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - TX_CONFIRMATION_DELAY_SEC;
                _logger?.LogInformation("Update Pyth LastContractUpdateTime to {time}", _pyth.LastContractUpdateTime);
            }

            var symbolEvents = e.Receipt.Logs
                .Where(l =>
                    l.Address.Equals(_symbolConfig.ContractAddress, StringComparison.InvariantCultureIgnoreCase))
                .ToList();

            if (symbolEvents.Count == 0)
                return;

            // update balances in background
            _ = Task.Run(async () =>
            {
                try
                {
                    var (balances, error) = await _balanceManager.UpdateBalancesAsync(_symbolConfig);

                    if (error != null)
                    {
                        _logger?.LogError(error, "[{symbol}] Update balances error", Symbol);
                        return;
                    }

                    _logger?.LogInformation(
                        "[{symbol}] Balances updated. " +
                        "Native balance: {nativeBalance}. " +
                        "Token X balance: {tokenXBalance}. " +
                        "Token Y balance: {tokenYBalance}",
                        Symbol,
                        balances.NativeBalance.ToString(),
                        balances.TokenBalanceX.ToString(),
                        balances.TokenBalanceY.ToString());
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[{symbol}] Update balances error", Symbol);
                }
            });
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
                    "[{symbol}] Insufficient token balance for operation. " +
                    "Token balance: {balance}. " +
                    "Previous leave amount: {previousLeaveAmount}. " +
                    "Input amount: {inputAmount}.",
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
            WsClient.SubscribeUserOrdersChannel(
                _vaultContractAddress.ToLowerInvariant(),
                _symbolConfig.ContractAddress.ToLowerInvariant());

            WsClient.SubscribeVaultTotalValuesChannel(
                _vaultContractAddress.ToLowerInvariant());
        }

        private void WebSocketClient_VaultTotalValuesUpdated(object sender, VaultTotalValuesEventArgs e)
        {
            if (!e.VaultTotalValues.VaultAddress.Equals(
                _vaultContractAddress,
                StringComparison.InvariantCultureIgnoreCase))
            {
                return;
            }

            VaultTotalValuesChanged?.Invoke(this, e);
        }
    }
}
