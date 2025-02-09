using Microsoft.Extensions.Logging;
using OnchainClob.Client;
using OnchainClob.Client.Abstract;
using OnchainClob.Client.Configuration;
using OnchainClob.Client.Events;
using OnchainClob.Common;
using OnchainClob.Trading.Events;
using OnchainClob.Trading.Requests;
using Revelium.Evm.Common;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading.Channels;
using ErrorEventArgs = OnchainClob.Trading.Events.ErrorEventArgs;

namespace OnchainClob.Trading.Abstract
{
    public abstract class Trader : ITrader
    {
        private const string ALL_MARKETS = "allMarkets";
        private const int PENDING_CALLS_CHECK_INTERVAL_MS = 10;
        private const int CANCELED_ORDERS_TTL_MS = 30 * 1000;

        public event EventHandler<List<Order>>? OrdersChanged;
        public event EventHandler<bool>? AvailabilityChanged;

        protected readonly ISymbolConfig _symbolConfig;
        protected readonly ILogger<Trader>? _logger;
        protected readonly WebSocketClient _webSocketClient;
        private readonly RestApi _restApi;
        private readonly IExecutor _executor;
        private Channel<UserOrdersEventArgs>? _userOrdersChannel;
        private CancellationTokenSource? _userOrdersHandlerCts;

        private readonly SemaphoreSlim _ordersSync;
        protected readonly ConcurrentDictionary<ulong, DateTimeOffset> _canceledOrders;
        protected readonly ConcurrentDictionary<string, List<string>> _pendingCancellationRequests;
        protected readonly ConcurrentDictionary<string, List<Order>> _pendingOrders;
        protected readonly ConcurrentDictionary<string, bool> _pendingRequests;
        protected readonly Dictionary<string, Order> _activeOrders;
        protected readonly Dictionary<string, Order> _filledUnclaimedOrders;

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
        protected abstract string UserAddress { get; }

        public Trader(
            ISymbolConfig symbolConfig,
            WebSocketClient webSocketClient,
            RestApi restApi,
            IExecutor executor,
            ILogger<Trader>? logger = null)
        {
            _symbolConfig = symbolConfig ?? throw new ArgumentNullException(nameof(symbolConfig));
            _logger = logger;

            _webSocketClient = webSocketClient ?? throw new ArgumentNullException(nameof(webSocketClient));
            _webSocketClient.Disconnected += WebSocketClient_Disconnected;
            _webSocketClient.StateStatusChanged += WebSocketClient_StateStatusChanged;
            _webSocketClient.UserOrdersUpdated += WebSocketClient_UserOrdersUpdated;

            _restApi = restApi ?? throw new ArgumentNullException(nameof(restApi));

            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _executor.TxMempooled += Executor_TxMempooled;
            _executor.TxFailed += Executor_TxFailed;
            _executor.Error += Executor_Error;

            _ordersSync = new SemaphoreSlim(initialCount: 1, maxCount: 1);
            _canceledOrders = [];
            _pendingCancellationRequests = [];
            _pendingOrders = [];
            _pendingRequests = [];
            _activeOrders = [];
            _filledUnclaimedOrders = [];
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

        public List<Order> GetFilledUnclaimedOrders()
        {
            try
            {
                _ordersSync.Wait();

                return [.. _filledUnclaimedOrders.Values];
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

        public async Task<bool> PendingOrderCancelAsync(
            string placeOrderRequestId,
            CancellationToken cancellationToken = default)
        {
            var isCanceled = await _executor.TryCancelRequestAsync(
                placeOrderRequestId,
                cancellationToken);

            if (isCanceled)
            {
                if (!_pendingOrders.Remove(placeOrderRequestId, out var pendingOrders))
                    return true;

                _logger?.LogDebug(
                    "[{@symbol}] {@count} pending orders removed for request id {@id} before tx sending",
                    Symbol,
                    pendingOrders.Count,
                    placeOrderRequestId);

                pendingOrders = [.. pendingOrders.Select(o => o with { Status = OrderStatus.CanceledAndClaimed })];

                OrdersChanged?.Invoke(this, pendingOrders);
            }
            else
            {
                // the transaction is already in the mempool and we need to wait for the orderId or tx failure
            }

            return isCanceled;
        }

        public abstract Task OrderSendAsync(
            decimal price,
            decimal qty,
            Side side,
            bool marketOnly = false,
            bool postOnly = false,
            bool transferExecutedTokens = false,
            CancellationToken cancellationToken = default);

        public abstract Task<bool> OrderCancelAsync(
            ulong orderId,
            bool transferTokens = false,
            CancellationToken cancellationToken = default);

        public abstract Task<bool> OrderModifyAsync(
            ulong orderId,
            decimal price,
            decimal qty,
            bool postOnly = false,
            bool transferTokens = false,
            CancellationToken cancellationToken = default);

        public abstract Task BatchAsync(
            IEnumerable<ITraderRequest> requests,
            bool postOnly = false,
            bool transferTokens = false,
            CancellationToken cancellationToken = default);

        protected abstract void SubscribeToChannels();

        private async void Executor_TxMempooled(object sender, MempooledEventArgs e)
        {
            _logger?.LogInformation(
                "[{@symbol}] tx with request id {@id} mempooled with tx id {@txId}",
                Symbol,
                e.RequestId,
                e.TxId);

            while (!_pendingRequests.TryRemove(e.RequestId, out _))
                await Task.Delay(PENDING_CALLS_CHECK_INTERVAL_MS);

            try
            {
                await _ordersSync.WaitAsync();

                if (!_pendingOrders.Remove(e.RequestId, out var pendingOrders))
                {
                    _logger?.LogInformation(
                        "[{@symbol}] pending orders for request id {@id} not found, probably it is a cancellation request",
                        Symbol,
                        e.RequestId);
                }
                else
                {
                    _logger?.LogDebug(
                        "[{@symbol}] {@count} pending orders removed for request id {@id}",
                        Symbol,
                        pendingOrders.Count,
                        e.RequestId);

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
                        Symbol,
                        e.TxId);
                }
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
                        _symbolConfig.Symbol,
                        e.TxId);
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
                        _symbolConfig.Symbol,
                        pendingOrders.Count,
                        e.Receipt.TransactionHash);
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
            while (!_pendingRequests.TryRemove(e.RequestId, out _))
                await Task.Delay(PENDING_CALLS_CHECK_INTERVAL_MS);

            try
            {
                await _ordersSync.WaitAsync();

                // if the transaction fails, try removing pending orders if exist
                if (_pendingOrders.Remove(e.RequestId, out var pendingOrders))
                {
                    _logger?.LogDebug(
                        "[{@symbol}] {@count} pending orders removed for request with id {@id} " +
                            "after tx sending fail",
                        _symbolConfig.Symbol,
                        pendingOrders.Count,
                        e.RequestId);
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
            _filledUnclaimedOrders.Clear();
        }

        private void WebSocketClient_StateStatusChanged(object? sender, StateStatus status)
        {
            if (status == StateStatus.Sync)
            {
                _logger?.LogInformation("[{@symbol}] Client syncing...", Symbol);

                IsAvailable = false;

                StopUserOrdersHandlerTask();

                _activeOrders.Clear();
                _filledUnclaimedOrders.Clear();
                return;
            }

            _logger?.LogInformation("[{@symbol}] Client ready. Subscribe to channels", Symbol);

            StartUserOrdersHandlerTask();
            SubscribeToChannels();
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
                    new
                    {
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
                    UserAddress,
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
                    else if (order.Status == OrderStatus.Filled)
                    {
                        // remove history order from active orders
                        _activeOrders.Remove(order.OrderId, out var _);

                        // add filled order to unclaimed orders
                        _filledUnclaimedOrders[order.OrderId] = order;
                    }
                    else
                    {
                        // remove history order from active orders, if exists
                        _activeOrders.Remove(order.OrderId, out _);

                        // remove history order from filled unclaimed orders, if exists
                        _filledUnclaimedOrders.Remove(order.OrderId, out _);

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
                            _symbolConfig.Symbol,
                            pendingOrders.Count,
                            order.TxnHash);
                    }
                }
            }
            finally
            {
                _ordersSync.Release();
            }
        }

        protected bool TryNormalizePrice(decimal price, out BigInteger normalizedPrice)
        {
            normalizedPrice = price.ToNormalizePrice(_symbolConfig.PricePrecision, out var rest);
            return rest == 0;
        }

        protected bool TryNormalizeQty(decimal qty, out BigInteger normalizedQty)
        {
            var multiplier = BigInteger.Pow(10, _symbolConfig.TokenX.Decimals - _symbolConfig.ScallingFactorX);

            normalizedQty = qty.Multiply(multiplier);

            return normalizedQty.Divide(multiplier) == qty;
        }

        protected BigInteger GetInputAmount(
            Side side,
            BigInteger normalizedPrice,
            BigInteger normalizedQty)
        {
            return side == Side.Sell
                ? normalizedQty * BigInteger.Pow(10, _symbolConfig.ScallingFactorX)
                : normalizedQty * normalizedPrice * BigInteger.Pow(10, _symbolConfig.ScallingFactorY);
        }

        protected BigInteger GetInputAmount(
            List<ulong> orderIds,
            List<BigInteger> prices,
            List<BigInteger> qtys,
            Side side)
        {
            return orderIds
                .Where(orderId => orderId.GetSideFromOrderId() == side)
                .Select((orderId, index) => qtys[index] > 0
                    ? GetInputAmount(side, prices[index], qtys[index])
                    : BigInteger.Zero)
                .Aggregate(BigInteger.Zero, (acc, value) => acc + value);
        }

        protected List<BigInteger> GetNormalizedQtys(IEnumerable<ITraderRequest> batchRequests)
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

        protected List<BigInteger> GetNormalizedPrices(IEnumerable<ITraderRequest> batchRequests)
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

        protected BigInteger GetPreviousLeaveAmount(List<ulong> orderIds, Side side)
        {
            return orderIds
                .Where(orderId => orderId > 1)
                .Select(orderId =>
                {
                    return _activeOrders.TryGetValue(orderId.ToString(), out var activeOrder)
                        ? activeOrder
                        : null;
                })
                .Where(order => order != null && order.Side == side)
                .Select(order => GetPreviousLeaveAmount(order!, side))
                .Aggregate(BigInteger.Zero, (acc, value) => acc + value);
        }

        protected BigInteger GetPreviousLeaveAmount(Order order, Side side)
        {
            if (!TryNormalizePrice(order.Price, out var normalizedPrice))
                throw new Exception($"Invalid significant digits count or size for price {order.Price}");

            if (!TryNormalizeQty(order.LeaveQty, out var normalizedQty))
                throw new Exception($"Invalid leaveqty {order.LeaveQty}");

            return GetInputAmount(side, normalizedPrice, normalizedQty);
        }

        protected (List<Order> pendingOrders, List<string> pendingCancellationRequests) CreatePendingOrdersAndCancellationRequests(
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
    }
}
