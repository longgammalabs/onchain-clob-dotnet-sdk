using OnchainClob.Client.Models;

namespace OnchainClob.Client.Events
{
    public class TradesEventArgs
    {
        public string MarketId { get; init; } = default!;
        public Trade[] Trades { get; init; } = default!;
    }
}
