using Hanji.Client;
using Hanji.Client.Configuration;
using Hanji.Client.Events;
using Hanji.Common;
using Hanji.Trading.Abstract;
using Hanji.Trading.Events;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading.Channels;
using ErrorEventArgs = Hanji.Trading.Events.ErrorEventArgs;

namespace Hanji.Trading
{
    public class HanjiSpotTrader : ITrader
    {
        private const long GAS_PRICE = 1000000000;
        private const long PLACE_ORDER_GAS = 2000000;
        private const long CLAIM_ORDER_GAS = 2000000;
        private const string ALL_MARKETS = "allMarkets";
        private const int PENDING_CALLS_CHECK_INTERVAL_MS = 10;
        private readonly BigInteger UINT128_MAX_VALUE = (BigInteger.One << 128) - 1;

        public event EventHandler<List<Order>>? OrdersChanged;
        public event EventHandler<bool>? AvailabilityChanged;

        private readonly ISymbolConfig _symbolConfig;
        private readonly HanjiWebSocketClient _webSocketClient;
        private readonly HanjiRestApi _restApi;
        private readonly HanjiSpot _hanjiSpot;
        private readonly ILogger<HanjiSpotTrader>? _logger;
        private Channel<UserOrdersEventArgs>? _userOrdersChannel;
        private CancellationTokenSource? _userOrdersHandlerCts;

        private readonly SemaphoreSlim _ordersSync;
        private readonly ConcurrentDictionary<string, (string orderId, DateTimeOffset timeStamp)> _pendingCancels;
        private readonly ConcurrentDictionary<string, List<Order>> _pendingOrders;
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

        public HanjiSpotTrader(
            ISymbolConfig symbolConfig,
            HanjiWebSocketClient webSocketClient,
            HanjiRestApi restApi,
            HanjiSpot hanjiSpot,
            ILogger<HanjiSpotTrader>? logger = null)
        {
            _symbolConfig = symbolConfig ?? throw new ArgumentNullException(nameof(symbolConfig));
            _logger = logger;

            _webSocketClient = webSocketClient ?? throw new ArgumentNullException(nameof(webSocketClient));
            _webSocketClient.Disconnected += HanjiClient_Disconnected;
            _webSocketClient.StateStatusChanged += HanjiClient_StateStatusChanged;
            _webSocketClient.UserOrdersUpdated += HanjiClient_UserOrdersUpdated;

            _restApi = restApi ?? throw new ArgumentNullException(nameof(restApi));

            _hanjiSpot = hanjiSpot ?? throw new ArgumentNullException(nameof(hanjiSpot));
            _hanjiSpot.Executor.TxMempooled += Executor_TxMempooled;
            _hanjiSpot.Executor.TxFailed += Executor_TxFailed;
            _hanjiSpot.Executor.Error += Executor_Error;

            _ordersSync = new SemaphoreSlim(initialCount: 1, maxCount: 1);
            _pendingCancels = [];
            _pendingOrders = [];
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

        public bool IsOrderCanceled(string orderId)
        {
            if (_pendingCancels.ContainsKey(orderId))
                return true;

            try
            {
                _ordersSync.Wait();

                if (_activeOrders.ContainsKey(orderId))
                    return false;

                if (_pendingOrders.ContainsKey(orderId))
                    return false;

                return true;
            }
            finally
            {
                _ordersSync.Release();
            }
        }

        public async Task OrderSendAsync(
            decimal price,
            decimal qty,
            Side side,
            bool marketOnly = false,
            bool postOnly = false,
            CancellationToken cancellationToken = default)
        {
            var hanjiPrice = price.ToHanjiPrice(_symbolConfig.PricePrecision);

            var requestId = await _hanjiSpot.PlaceOrderAsync(new PlaceOrderParams
            {
                IsAsk                       = side == Side.Sell,
                Price                       = hanjiPrice,
                Quantity                    = (BigInteger)qty, // todo: check convertion to BigInteger
                MaxCommission               = UINT128_MAX_VALUE,
                MarketOnly                  = marketOnly,
                PostOnly                    = postOnly,
                TransferExecutedTokens      = false,
                Expires                     = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),

                ContractAddress             = _symbolConfig.ContractAddress.ToLowerInvariant(),
                GasPrice                    = GAS_PRICE,
                GasLimit                    = PLACE_ORDER_GAS,
                EstimateGas                 = true,
                EstimateGasReserveInPercent = 10
            }, cancellationToken);

            var pendingOrder = new Order(
                OrderId:     requestId,
                Price:       hanjiPrice.FromHanjiPrice(_symbolConfig.PricePrecision),
                Qty:         qty,
                LeaveQty:    qty,
                ClaimedQty:  0,
                Side:        side,
                Symbol:      Symbol,
                Status:      OrderStatus.Pending,
                Type:        OrderType.Return,
                Created:     DateTimeOffset.UtcNow,
                LastChanged: DateTimeOffset.UtcNow,
                TxnHash:     null);

            _pendingOrders.TryAdd(requestId, [pendingOrder]);

            _logger?.LogDebug(
                "[{@symbol}] Add {@side} pending order request with id {@id}",
                Symbol,
                side,
                requestId);
        }

        public async Task<bool> OrderCancelAsync(
            string orderId,
            CancellationToken cancellationToken = default)
        {
            // return if order is already canceled
            if (!_pendingCancels.TryAdd(orderId, (orderId, DateTimeOffset.UtcNow)))
                return false;

            if (_pendingOrders.TryGetValue(orderId, out var pendingOrders))
            {
                if (pendingOrders[0].Status == OrderStatus.Pending)
                {
                    return await TryCancelPendingOrdersAsync(orderId, cancellationToken);
                }
                else if (pendingOrders[0].Status == OrderStatus.Mempooled)
                {
                    // tx is already in mempool and we need wait for orderId or tx failure
                    return false;
                }
            }

            var requestId = await _hanjiSpot.ClaimOrderAsync(new ClaimOrderParams
            {
                OrderId                     = ulong.Parse(orderId),
                TransferTokens              = false,
                Expires                     = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),

                ContractAddress             = _symbolConfig.ContractAddress.ToLowerInvariant(),
                GasPrice                    = GAS_PRICE,
                GasLimit                    = CLAIM_ORDER_GAS,
                EstimateGas                 = true,
                EstimateGasReserveInPercent = 10
            }, cancellationToken);

            _pendingCancels.TryAdd(requestId, (orderId, DateTimeOffset.UtcNow));

            _logger?.LogDebug(
                "[{@symbol}] Add cancellation request with id {@id} and orderId {@orderId}",
                Symbol,
                requestId,
                orderId);

            return true;
        }

        private async Task<bool> TryCancelPendingOrdersAsync(
            string requestId,
            CancellationToken cancellationToken)
        {
            var isCanceled = await _hanjiSpot.Executor.TryCancelRequestAsync(
                requestId,
                cancellationToken);

            if (isCanceled)
            {
                if (!_pendingOrders.Remove(requestId, out var pendingOrders))
                    return isCanceled;

                _logger?.LogDebug(
                    message: "[{@symbol}] {@count} pending orders removed for request id {@id} before tx sending",
                    args: [Symbol, pendingOrders.Count, requestId]);

                pendingOrders = pendingOrders
                    .Select(o => o with { Status = OrderStatus.CanceledAndClaimed })
                    .ToList();

                OrdersChanged?.Invoke(this, pendingOrders);
            }
            else
            {
                // tx is already in mempool and we need wait for orderId or tx failure
            }

            return isCanceled;
        }

        private async void Executor_TxMempooled(object sender, MempooledEventArgs e)
        {
            var requestType = await TryResolveRequestTypeAsync(e.RequestId);

            if (requestType == RequestType.OrderSend)
            {
                try
                {
                    await _ordersSync.WaitAsync();

                    if (!_pendingOrders.Remove(e.RequestId, out var pendingOrders))
                    {
                        _logger?.LogError(
                            "[{@symbol}] pending orders for request id {@id} should have been removed but are missing",
                            Symbol,
                            e.RequestId);

                        return;
                    }

                    _logger?.LogDebug(
                        message: "[{@symbol}] {@count} pending orders removed for request id {@id}",
                        args: [Symbol, pendingOrders.Count, e.RequestId]);

                    pendingOrders = pendingOrders
                        .Select(o => o with
                        {
                            OrderId = e.TxId,
                            TxnHash = e.TxId,
                            Status = OrderStatus.Mempooled
                        })
                        .ToList();

                    _pendingOrders.TryAdd(e.TxId, pendingOrders);

                    _logger?.LogDebug(
                        "[{@symbol}] Add pending orders for txId {@txId}",
                        Symbol,
                        e.TxId);
                }
                finally
                {
                    _ordersSync.Release();
                }
            }
            else if (requestType == RequestType.OrderCancel)
            {
                _pendingCancels.TryGetValue(e.RequestId, out (string orderId, DateTimeOffset timeStamp) @params);

                if (!_pendingCancels.TryAdd(e.TxId, @params))
                {
                    _logger?.LogError(
                        "[{@symbol}] Cannot add cancellation request with id {id} with orderId {@orderId}",
                        _symbolConfig.Symbol,
                        e.TxId,
                        @params.orderId);
                }
            }
        }

        private async void Executor_TxFailed(object sender, ConfirmedEventArgs e)
        {
            try
            {
                await _ordersSync.WaitAsync();

                // if transaction failed, try remove pending orders
                if (_pendingOrders.TryRemove(e.Receipt.TransactionHash, out var pendingOrders))
                {
                    _logger?.LogDebug(
                        message: "[{@symbol}] {@count} pending orders removed for txId {@txId} after tx failing",
                        args: [_symbolConfig.Symbol, pendingOrders.Count, e.Receipt.TransactionHash]);
                }
            }
            finally
            {
                _ordersSync.Release();
            }

            // if transaction failed, try remove pending cancels
            if (_pendingCancels.TryRemove(e.Receipt.TransactionHash, out var @params))
            {
                // restore cancelling flag for order
                _pendingCancels.TryRemove(@params.orderId, out var _);
            }
        }

        private async void Executor_Error(object sender, ErrorEventArgs e)
        {
            try
            {
                await _ordersSync.WaitAsync();

                // transaction failed, try remove pending orders
                if (_pendingOrders.Remove(e.RequestId, out var pendingOrders))
                {
                    _logger?.LogDebug(
                        message: "[{@symbol}] {@count} pending orders removed for request with id {@id} after tx sending fail",
                        args: [_symbolConfig.Symbol, pendingOrders.Count, e.RequestId]);
                }
            }
            finally
            {
                _ordersSync.Release();
            }

            // transaction failed, try remove pending cancels
            if (_pendingCancels.TryRemove(e.RequestId, out var @params))
            {
                // restore cancelling flag for order
                _pendingCancels.TryRemove(@params.orderId, out var _);
            }
        }

        private void HanjiClient_Disconnected(object? sender, EventArgs e)
        {
            IsAvailable = false;

            StopUserOrdersHandlerTask();
            _activeOrders.Clear();
        }

        private void HanjiClient_StateStatusChanged(object? sender, StateStatus status)
        {
            if (status == StateStatus.Sync)
            {
                _logger?.LogInformation("HanjiClient syncing...");

                IsAvailable = false;

                StopUserOrdersHandlerTask();
                _activeOrders.Clear();
                return;
            }

            _logger?.LogInformation("HanjiClient ready. Subscribe to channels");

            StartUserOrdersHandlerTask();

            _webSocketClient.SubscribeUserOrdersChannel(
                userAddress: _hanjiSpot.Executor.Signer.GetAddress(),
                marketId: _symbolConfig.ContractAddress.ToLowerInvariant());
        }

        private void HanjiClient_UserOrdersUpdated(
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

        private enum RequestType
        {
            Unknown,
            OrderSend,
            OrderCancel
        }

        private async Task<RequestType> TryResolveRequestTypeAsync(
            string requestId,
            CancellationToken cancellationToken = default)
        {
            var startTimeStamp = DateTimeOffset.UtcNow;
            var timeOut = TimeSpan.FromSeconds(1);

            while (true)
            {
                if (DateTimeOffset.UtcNow - startTimeStamp >= timeOut)
                    return RequestType.Unknown;

                if (_pendingOrders.ContainsKey(requestId))
                    return RequestType.OrderSend;

                if (_pendingCancels.ContainsKey(requestId))
                    return RequestType.OrderCancel;

                await Task.Delay(
                    millisecondsDelay: PENDING_CALLS_CHECK_INTERVAL_MS,
                    cancellationToken: cancellationToken);
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
                channel: _userOrdersChannel,
                cancellationToken: _userOrdersHandlerCts.Token), _userOrdersHandlerCts.Token);
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
                        await UserOrdersHandlerAsync(
                            args: args,
                            cancellationToken: cancellationToken);
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
                .Where(o => o.Market.Id.Equals(_symbolConfig.ContractAddress, StringComparison.InvariantCultureIgnoreCase))
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
                    userAddress: _hanjiSpot.Executor.Signer.GetAddress(),
                    marketId: _symbolConfig.ContractAddress.ToLowerInvariant(),
                    cancellationToken: cancellationToken);

                if (error != null)
                {
                    _logger?.LogError(
                        error: error,
                        message: "[{@symbol}] Get active orders snapshot error", _symbolConfig.Symbol);

                    return;
                }

                if (activeOrders == null)
                {
                    _logger?.LogError("[{@symbol}] Get active orders snapshot error", _symbolConfig.Symbol);
                    return;
                }

                await SaveOrdersAsync(
                    orders: activeOrders.Select(o => o.ToOrder(_symbolConfig.Symbol, _symbolConfig.PricePrecision)),
                    cancellationToken: cancellationToken);

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

                        //// try remove cancelled order by id, if exists
                        //_pendingCancels.TryRemove(order.OrderId, out _);
                    }

                    // try remove pending orders if exists
                    if (_pendingOrders.Remove(order.TxnHash!, out var pendingOrders))
                    {
                        _logger?.LogInformation(
                            message: "[{@symbol}] {@count} pending orders removed for txId: {@txId}",
                            args: [_symbolConfig.Symbol, pendingOrders.Count, order.TxnHash]);
                    }
                }
            }
            finally
            {
                _ordersSync.Release();
            }
        }

        //private void RemoveOldPendingCancels()
        //{
        //    var currentTimeStamp = DateTimeOffset.UtcNow;

        //    foreach (var (id, (_, timeStamp)) in _pendingCancels)
        //        if (currentTimeStamp - timeStamp > TimeSpan.FromSeconds(10 * 60))
        //            _pendingCancels.TryRemove(id, out _);
        //}
    }
}
