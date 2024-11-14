using Hanji.Client.Models;

namespace Hanji.Client.Events
{
    public class UserFillsEventArgs
    {
        public string MarketId { get; init; } = default!;
        public UserFill[] UserFills { get; init; } = default!;
    }
}
