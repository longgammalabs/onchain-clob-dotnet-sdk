using Hanji.Common;

namespace Hanji.Trading.Abstract
{
    public interface ITrader
    {
        event EventHandler<List<Order>> OrdersChanged;
        event EventHandler<bool>? AvailabilityChanged;

        List<Order> GetActiveOrders(bool pending = true);
        List<Order> GetPendingOrders();
        bool IsOrderCanceled(string orderId);

        Task OrderSendAsync(
            decimal price,
            decimal qty,
            Side side,
            bool marketOnly = false,
            bool postOnly = false,
            CancellationToken cancellationToken = default);

        Task<bool> OrderCancelAsync(
            string orderId,
            CancellationToken cancellationToken = default);
    }
}
