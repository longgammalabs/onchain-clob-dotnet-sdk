using Hanji.Common;

namespace Hanji.Trading.Abstract
{
    public interface IHanjiRequest
    {
        public int Priority { get; }
        public ulong OrderId { get; }
        public decimal Price { get; }
        public decimal Qty { get; }
    }

    public class HanjiPlaceOrderRequest : IHanjiRequest
    {
        public int Priority => 0;
        public decimal Price { get; init; }
        public decimal Qty { get; init; }
        public Side Side { get; init; }
        public ulong OrderId => 0;
    }

    public class HanjiChangeOrderRequest : IHanjiRequest
    {
        public int Priority => 1;
        public ulong OrderId { get; init; }
        public decimal Price { get; init; }
        public decimal Qty { get; init; }
    }

    public class HanjiClaimOrderRequest : IHanjiRequest
    {
        public int Priority => 2;
        public ulong OrderId { get; init; }
        public decimal Price => 0;
        public decimal Qty => 0;
    }

    public class HanjiCancelPendingOrderRequest: IHanjiRequest
    {
        public int Priority => 3;
        public string RequestId { get; init; } = default!;
        public ulong OrderId => 0;
        public decimal Price => 0;
        public decimal Qty => 0;
    }

    public interface IHanjiTrader
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
            IEnumerable<IHanjiRequest> requests,
            bool postOnly = false,
            bool transferTokens = false,
            CancellationToken cancellationToken = default);
    }
}
