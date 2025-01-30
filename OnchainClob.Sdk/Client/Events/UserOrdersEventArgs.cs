using OnchainClob.Client.Models;

namespace OnchainClob.Client.Events
{
    public class UserOrdersEventArgs
    {
        public string MarketId { get; init; } = default!;
        public UserOrder[] UserOrders { get; init; } = default!;
        public bool IsSnapshot { get; init; }
    }
}
