namespace OnchainClob.Trading.Requests
{
    public class CancelPendingOrderRequest : ITraderRequest
    {
        public int Priority => 3;
        public string RequestId { get; init; } = default!;
        public ulong OrderId => 0;
        public decimal Price => 0;
        public decimal Qty => 0;
    }
}
