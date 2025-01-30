using OnchainClob.Client.Models;

namespace OnchainClob.Client.Events
{
    public class OrderBookEventArgs
    {
        public string MarketId { get; init; } = default!;
        public OrderBook OrderBook { get; init; } = default!;
    }
}
