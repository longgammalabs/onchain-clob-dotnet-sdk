using OnchainClob.Common;

namespace OnchainClob.Trading.Abstract
{
    public interface IOnchainClobRequest
    {
        public int Priority { get; }
        public ulong OrderId { get; }
        public decimal Price { get; }
        public decimal Qty { get; }
    }

    public class PlaceOrderRequest : IOnchainClobRequest
    {
        public int Priority => 0;
        public decimal Price { get; init; }
        public decimal Qty { get; init; }
        public Side Side { get; init; }
        public ulong OrderId => 0;
    }

    public class ChangeOrderRequest : IOnchainClobRequest
    {
        public int Priority => 1;
        public ulong OrderId { get; init; }
        public decimal Price { get; init; }
        public decimal Qty { get; init; }
    }

    public class ClaimOrderRequest : IOnchainClobRequest
    {
        public int Priority => 2;
        public ulong OrderId { get; init; }
        public decimal Price => 0;
        public decimal Qty => 0;
    }

    public class CancelPendingOrderRequest: IOnchainClobRequest
    {
        public int Priority => 3;
        public string RequestId { get; init; } = default!;
        public ulong OrderId => 0;
        public decimal Price => 0;
        public decimal Qty => 0;
    }

    public interface ITrader
    {
        event EventHandler<List<Order>> OrdersChanged;
        event EventHandler<bool>? AvailabilityChanged;

        List<Order> GetActiveOrders(bool pending = true);
        List<Order> GetPendingOrders();
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
            IEnumerable<IOnchainClobRequest> requests,
            bool postOnly = false,
            bool transferTokens = false,
            CancellationToken cancellationToken = default);
    }
}
