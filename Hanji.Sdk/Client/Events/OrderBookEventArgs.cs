using Hanji.Client.Models;

namespace Hanji.Client.Events
{
    public class OrderBookEventArgs
    {
        public string MarketId { get; init; } = default!;
        public OrderBook OrderBook { get; init; } = default!;
    }
}
