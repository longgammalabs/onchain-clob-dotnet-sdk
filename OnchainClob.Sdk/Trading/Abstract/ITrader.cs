using OnchainClob.Common;
using OnchainClob.Trading.Requests;

namespace OnchainClob.Trading.Abstract
{
    public interface ITrader
    {
        event EventHandler<List<Order>> OrdersChanged;
        event EventHandler<bool>? AvailabilityChanged;

        bool IsAvailable { get; }
        List<Order> GetActiveOrders(bool pending = true);
        List<Order> GetPendingOrders();
        List<Order> GetFilledUnclaimedOrders();
        bool IsOrderCanceled(ulong orderId);

        Task OrderSendAsync(
            decimal price,
            decimal qty,
            Side side,
            bool marketOnly = false,
            bool postOnly = false,
            bool transferExecutedTokens = false,
            CancellationToken cancellationToken = default);

        Task<bool> OrderCancelAsync(
            ulong orderId,
            bool transferTokens = false,
            CancellationToken cancellationToken = default);

        Task<bool> OrderModifyAsync(
            ulong orderId,
            decimal price,
            decimal qty,
            bool postOnly = false,
            bool transferTokens = false,
            CancellationToken cancellationToken = default);

        Task<bool> PendingOrderCancelAsync(
            string placeOrderRequestId,
            CancellationToken cancellationToken = default);

        Task BatchAsync(
            IEnumerable<ITraderRequest> requests,
            bool postOnly = false,
            bool transferTokens = false,
            CancellationToken cancellationToken = default);
    }
}
