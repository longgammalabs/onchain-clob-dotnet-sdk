using OnchainClob.Client.Events;
using OnchainClob.Client.Models;
using OnchainClob.Common;
using Microsoft.Extensions.Logging;
using System.Net.WebSockets;
using System.Text.Json;
using Websocket.Client;

namespace OnchainClob.Client
{
    public enum StateStatus
    {
        Sync,
        Ready
    }

    public class WebSocketClientOptions
    {
        public int? ReconnectionTimeoutInSec { get; init; }
        public int? ErrorReconnectionTimeoutInSec { get; init; }
    }

    public class WebSocketClient(
        string url,
        WebSocketClientOptions? options = null,
        ILogger<WebSocketClient>? logger = null) : IDisposable
    {
        public event EventHandler<OrderBookEventArgs>? OrderBookUpdated;
        public event EventHandler<UserOrdersEventArgs>? UserOrdersUpdated;
        public event EventHandler<UserFillsEventArgs>? UserFillsUpdated;
        public event EventHandler<VaultTotalValuesEventArgs>? VaultTotalValuesUpdated;
        public event EventHandler? Connected;
        public event EventHandler? Disconnected;
        public event EventHandler<StateStatus>? StateStatusChanged;

        private readonly record struct SymbolId(string Symbol, string MarketId);

        private readonly string _url = url;
        private readonly WebSocketClientOptions? _options = options;
        private readonly ILogger<WebSocketClient>? _logger = logger;
        private readonly RateLimitControl _rateLimitControl = new(500);
        private WebsocketClient? _ws;
        private bool _disposed;

        public bool IsConnected => _ws != null && _ws.IsRunning;
        public StateStatus StateStatus { get; private set; }

        public async Task StartAsync()
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
                _logger?.LogInformation("WebSocket {@url} connected", _ws.Url);

                Connected?.Invoke(this, EventArgs.Empty);
            });

            _ws.DisconnectionHappened.Subscribe(info =>
            {
                _logger?.LogInformation("WebSocket {@url} disconnected", _ws.Url);

                StateStatus = StateStatus.Sync;

                Disconnected?.Invoke(this, EventArgs.Empty);
            });

            _ws.MessageReceived.Subscribe(HandleMessage);

            await _ws.Start();
        }

        public async Task StopAsync()
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
                    channel = "squidState"
                }
            });

            await _rateLimitControl.WaitAsync();

            _ws!.Send(requestJson);
        }

        public async void SubscribeToOrderBookChannel(string marketId)
        {
            _logger?.LogInformation("Subscribe to {@marketId} order book channel", marketId);

            var requestJson = JsonSerializer.Serialize(new
            {
                method = "subscribe",
                subscription = new
                {
                    channel = "orderbook",
                    market = marketId
                }
            });

            await _rateLimitControl.WaitAsync();

            _ws!.Send(requestJson);
        }

        public async void SubscribeUserOrdersChannel(string userAddress, string marketId)
        {
            _logger?.LogInformation("Subscribe to {@marketId} user orders channel", marketId);

            var requestJson = JsonSerializer.Serialize(new
            {
                method = "subscribe",
                subscription = new
                {
                    channel = "userOrders",
                    user = userAddress,
                    market = marketId
                }
            });

            await _rateLimitControl.WaitAsync();

            _ws!.Send(requestJson);
        }

        public async void SubscribeUserFillsChannel(string userAddress, string marketId)
        {
            _logger?.LogInformation("Subscribe to {@marketId} user fills channel", marketId);

            var requestJson = JsonSerializer.Serialize(new
            {
                method = "subscribe",
                subscription = new
                {
                    channel = "userFills",
                    user = userAddress,
                    market = marketId
                }
            });

            await _rateLimitControl.WaitAsync();

            _ws!.Send(requestJson);
        }

        public async void SubscribeVaultTotalValuesChannel()
        {
            _logger?.LogInformation("Subscribe to vault total values channel");

            var requestJson = JsonSerializer.Serialize(new
            {
                method = "subscribe",
                subscription = new
                {
                    channel = "vaultTotalValues",
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

                _logger?.LogTrace("Message:\n{text}", msg.Text);

                var message = JsonSerializer.Deserialize<ChannelMessage<JsonElement>>(msg.Text);

                switch (message!.Channel)
                {
                    case "connection":
                        HandleConnectionMessage(message);
                        break;
                    case "squidState":
                        HandleStateMessage(message);
                        break;
                    case "subscriptionResponse":
                        HandleSubscriptionMessage(message);
                        break;
                    case "orderbook":
                        HandleOrderBookMessage(message);
                        break;
                    case "userOrders":
                        HandleUserOrdersMessage(message);
                        break;
                    case "userFills":
                        HandleUserFillsMessage(message);
                        break;
                    case "vaultTotalValues":
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
                ? StateStatus.Ready
                : StateStatus.Sync;

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

            UserFillsUpdated?.Invoke(this, new UserFillsEventArgs
            {
                MarketId = message.Id,
                UserFills = fills
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
            var vaultTotalValues = message.Data.Deserialize<VaultTotalValues>();

            if (vaultTotalValues == null)
            {
                _logger?.LogError("Vault total values is null");
                return;
            }

            VaultTotalValuesUpdated?.Invoke(this, new VaultTotalValuesEventArgs
            {
                VaultTotalValues = vaultTotalValues
            });
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
