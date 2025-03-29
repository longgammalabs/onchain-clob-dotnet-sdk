using Microsoft.Extensions.Logging;
using OnchainClob.Client.Abstract;
using OnchainClob.Client.Events;
using OnchainClob.Client.Models;
using OnchainClob.Common;
using System.Net.WebSockets;
using System.Text.Json;
using Websocket.Client;

namespace OnchainClob.Client
{
    public class OnchainClobWsClient(
        string url,
        WsClientOptions? options = null,
        ILogger<OnchainClobWsClient>? logger = null) : IDisposable, IOnchainClobWsClient
    {
        private const string CONNECTION_CHANNEL = "connection";
        private const string SQUID_STATE_CHANNEL = "squidState";
        private const string SUBSCRIPTION_CHANNEL = "subscriptionResponse";
        private const string ORDERBOOK_CHANNEL = "orderbook";
        private const string USER_ORDERS_CHANNEL = "userOrders";
        private const string USER_FILLS_CHANNEL = "userFills";
        private const string VAULT_TOTAL_VALUES_CHANNEL = "vaultTotalValues";
        private const string TRADES_CHANNEL = "trades";

        public event EventHandler<OrderBookEventArgs>? OrderBookUpdated;
        public event EventHandler<UserOrdersEventArgs>? UserOrdersUpdated;
        public event EventHandler<UserFillsEventArgs>? UserFillsReceived;
        public event EventHandler<TradesEventArgs>? TradeReceived;
        public event EventHandler<VaultTotalValuesEventArgs>? VaultTotalValuesUpdated;
        public event EventHandler? Connected;
        public event EventHandler? Disconnected;
        public event EventHandler<WsClientStatus>? StateStatusChanged;

        private readonly record struct SymbolId(string Symbol, string MarketId);

        private readonly string _url = url;
        private readonly WsClientOptions? _options = options;
        private readonly ILogger<OnchainClobWsClient>? _logger = logger;
        private readonly RateLimitControl _rateLimitControl = new(500);
        private WebsocketClient? _ws;
        private bool _disposed;

        public bool IsConnected => _ws != null && _ws.IsRunning;
        public WsClientStatus StateStatus { get; private set; }

        public async Task StartAsync(CancellationToken ct)
        {
            if (IsConnected)
                return;

            TimeSpan? reconnectionTimeout = _options?.ReconnectionTimeoutInSec != null
                ? TimeSpan.FromSeconds(_options.ReconnectionTimeoutInSec.Value)
                : null;

            TimeSpan? errorReconnectionTimeout = _options?.ErrorReconnectionTimeoutInSec != null
                ? TimeSpan.FromSeconds(_options.ErrorReconnectionTimeoutInSec.Value)
                : null;

            _ws = new WebsocketClient(new Uri(_url))
            {
                //Name = _config.Url,
                ReconnectTimeout = reconnectionTimeout,
                ErrorReconnectTimeout = errorReconnectionTimeout
            };

            _ws.ReconnectionHappened.Subscribe(info =>
            {
                _logger?.LogInformation("WebSocket {url} connected", _ws.Url);

                Connected?.Invoke(this, EventArgs.Empty);
            });

            _ws.DisconnectionHappened.Subscribe(info =>
            {
                _logger?.LogInformation("WebSocket {url} disconnected", _ws.Url);

                StateStatus = WsClientStatus.Sync;

                Disconnected?.Invoke(this, EventArgs.Empty);
            });

            _ws.MessageReceived.Subscribe(HandleMessage);

            await _ws.Start();
        }

        public async Task StopAsync(CancellationToken ct)
        {
            if (!IsConnected)
                return;

            await _ws!.Stop(WebSocketCloseStatus.NormalClosure, string.Empty);
        }

        public async void SubscribeToStateChannel()
        {
            _logger?.LogInformation("Subscribe to squid state channel");

            var requestJson = JsonSerializer.Serialize(new
            {
                method = "subscribe",
                subscription = new
                {
                    channel = SQUID_STATE_CHANNEL
                }
            });

            await _rateLimitControl.WaitAsync();

            _ws!.Send(requestJson);
        }

        public async void SubscribeToOrderBookChannel(string marketId)
        {
            _logger?.LogInformation("Subscribe to {marketId} order book channel", marketId);

            var requestJson = JsonSerializer.Serialize(new
            {
                method = "subscribe",
                subscription = new
                {
                    channel = ORDERBOOK_CHANNEL,
                    market = marketId
                }
            });

            await _rateLimitControl.WaitAsync();

            _ws!.Send(requestJson);
        }

        public async void SubscribeUserOrdersChannel(string userAddress, string marketId)
        {
            _logger?.LogInformation("Subscribe to {marketId} user orders channel", marketId);

            var requestJson = JsonSerializer.Serialize(new
            {
                method = "subscribe",
                subscription = new
                {
                    channel = USER_ORDERS_CHANNEL,
                    user = userAddress,
                    market = marketId
                }
            });

            await _rateLimitControl.WaitAsync();

            _ws!.Send(requestJson);
        }

        public async void SubscribeTradeChannel(string marketId)
        {
            _logger?.LogInformation("Subscribe to {marketId} trade channel", marketId);

            var requestJson = JsonSerializer.Serialize(new
            {
                method = "subscribe",
                subscription = new
                {
                    channel = TRADES_CHANNEL,
                    market = marketId
                }
            });

            await _rateLimitControl.WaitAsync();

            _ws!.Send(requestJson);
        }

        public async void SubscribeUserFillsChannel(string userAddress, string marketId)
        {
            _logger?.LogInformation("Subscribe to {marketId} user fills channel", marketId);

            var requestJson = JsonSerializer.Serialize(new
            {
                method = "subscribe",
                subscription = new
                {
                    channel = USER_FILLS_CHANNEL,
                    user = userAddress,
                    market = marketId
                }
            });

            await _rateLimitControl.WaitAsync();

            _ws!.Send(requestJson);
        }

        public async void SubscribeVaultTotalValuesChannel(string vault)
        {
            _logger?.LogInformation("Subscribe to vault total values channel");

            var requestJson = JsonSerializer.Serialize(new
            {
                method = "subscribe",
                subscription = new
                {
                    vault,
                    channel = VAULT_TOTAL_VALUES_CHANNEL
                }
            });

            await _rateLimitControl.WaitAsync();

            _ws!.Send(requestJson);
        }

        private void HandleMessage(ResponseMessage msg)

        {
            try
            {
                if (msg.MessageType != WebSocketMessageType.Text)
                    return;

                if (msg.Text == null)
                {
                    _logger?.LogWarning("Null text received");
                    return;
                }

                _logger?.LogTrace("Message:\n{@text}", msg.Text);

                var message = JsonSerializer.Deserialize<ChannelMessage<JsonElement>>(msg.Text);

                switch (message!.Channel)
                {
                    case CONNECTION_CHANNEL:
                        HandleConnectionMessage(message);
                        break;
                    case SQUID_STATE_CHANNEL:
                        HandleStateMessage(message);
                        break;
                    case SUBSCRIPTION_CHANNEL:
                        HandleSubscriptionMessage(message);
                        break;
                    case ORDERBOOK_CHANNEL:
                        HandleOrderBookMessage(message);
                        break;
                    case USER_ORDERS_CHANNEL:
                        HandleUserOrdersMessage(message);
                        break;
                    case USER_FILLS_CHANNEL:
                        HandleUserFillsMessage(message);
                        break;
                    case TRADES_CHANNEL:
                        HandleTradesMessage(message);
                        break;
                    case VAULT_TOTAL_VALUES_CHANNEL:
                        HandleVaultTotalValuesMessage(message);
                        break;
                }
            }
            catch (Exception e)

            {
                _logger?.LogError(e, "Message event handler error");
            }
        }

        private void HandleConnectionMessage(ChannelMessage<JsonElement> message)
        {
            var data = message.Data.GetString();

            if (data == "Successfully connected!")
            {
                SubscribeToStateChannel();
            }
        }

        private void HandleStateMessage(ChannelMessage<JsonElement> message)
        {
            var state = message.Data.Deserialize<State>();

            if (state == null)
            {
                _logger?.LogError("Invalid channel state message");
                return;
            }

            var prevStatus = StateStatus;

            StateStatus = state.Status.Equals("ok", StringComparison.InvariantCultureIgnoreCase)
                ? WsClientStatus.Ready
                : WsClientStatus.Sync;

            if (prevStatus != StateStatus)
                StateStatusChanged?.Invoke(this, StateStatus);
        }

        private void HandleSubscriptionMessage(ChannelMessage<JsonElement> message)
        {
            _logger?.LogDebug("Subscription message: {@text}", message.Data.ToString());
        }

        private void HandleUserOrdersMessage(ChannelMessage<JsonElement> message)
        {
            var orders = message.Data.Deserialize<UserOrder[]>();

            if (orders == null)
            {
                _logger?.LogError("User orders array is null");
                return;
            }

            UserOrdersUpdated?.Invoke(this, new UserOrdersEventArgs
            {
                MarketId = message.Id,
                UserOrders = orders,
                IsSnapshot = message.IsSnapshot
            });
        }

        private void HandleUserFillsMessage(ChannelMessage<JsonElement> message)
        {
            var fills = message.Data.Deserialize<UserFill[]>();

            if (fills == null)
            {
                _logger?.LogError("User fills array is null");
                return;
            }

            UserFillsReceived?.Invoke(this, new UserFillsEventArgs
            {
                MarketId = message.Id,
                UserFills = fills
            });
        }

        private void HandleTradesMessage(ChannelMessage<JsonElement> message)
        {
            if (message.IsSnapshot)
                return; // skip trades snapshot

            var trades = message.Data.Deserialize<Trade[]>();

            if (trades == null)
            {
                _logger?.LogError("Trades array is null");
                return;
            }

            TradeReceived?.Invoke(this, new TradesEventArgs
            {
                MarketId = message.Id,
                Trades = trades
            });
        }

        private void HandleOrderBookMessage(ChannelMessage<JsonElement> message)
        {
            var orderBook = message.Data.Deserialize<OrderBook>();

            if (orderBook == null)
            {
                _logger?.LogError("Order book is null");
                return;
            }

            OrderBookUpdated?.Invoke(this, new OrderBookEventArgs
            {
                MarketId = message.Id,
                OrderBook = orderBook
            });
        }

        private void HandleVaultTotalValuesMessage(ChannelMessage<JsonElement> message)
        {
            var vaults = message.Data.Deserialize<VaultTotalValues[]>();

            if (vaults == null)
            {
                _logger?.LogError("Vaults total values is null");
                return;
            }

            foreach (var vault in vaults)
            {
                VaultTotalValuesUpdated?.Invoke(this, new VaultTotalValuesEventArgs
                {
                    VaultTotalValues = vault
                });
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _ws?.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
