using OnchainClob.Client.Models;

namespace OnchainClob.Client.Events
{
    public class UserFillsEventArgs
    {
        public string MarketId { get; init; } = default!;
        public UserFill[] UserFills { get; init; } = default!;
    }
}
