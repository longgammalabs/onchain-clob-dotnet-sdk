using Hanji.Client.Models;

namespace Hanji.Client.Events
{
    public class UserOrdersEventArgs
    {
        public string MarketId { get; init; } = default!;
        public UserOrder[] UserOrders { get; init; } = default!;
        public bool IsSnapshot { get; init; }
    }
}
