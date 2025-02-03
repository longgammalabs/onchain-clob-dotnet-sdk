namespace OnchainClob.Trading.Requests
{
    public class ClaimOrderRequest : ITraderRequest
    {
        public int Priority => 2;
        public ulong OrderId { get; init; }
        public decimal Price => 0;
        public decimal Qty => 0;
    }
}
