using OnchainClob.Client.Events;
using OnchainClob.Common;
using OnchainClob.Trading.Requests;

namespace OnchainClob.Trading.Abstract
{
    public interface IVaultTrader
    {
        event EventHandler<List<Order>> OrdersChanged;
        event EventHandler<bool>? AvailabilityChanged;
        event EventHandler<VaultTotalValuesEventArgs>? VaultTotalValuesChanged;

        List<Order> GetActiveOrders(bool pending = true);
        List<Order> GetPendingOrders();
        bool IsOrderCanceled(ulong orderId);

        Task OrderSendAsync(
            decimal price,
            decimal qty,
            Side side,
            bool marketOnly = false,
            bool postOnly = false,
            CancellationToken cancellationToken = default);

        Task<bool> OrderCancelAsync(
            ulong orderId,
            CancellationToken cancellationToken = default);

        Task<bool> PendingOrderCancelAsync(
            string placeOrderRequestId,
            CancellationToken cancellationToken = default);

        Task BatchAsync(
            IEnumerable<ITraderRequest> requests,
            bool postOnly = false,
            CancellationToken cancellationToken = default);
    }
}
