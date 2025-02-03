namespace OnchainClob.Trading.Requests
{
    public class ChangeOrderRequest : ITraderRequest
    {
        public int Priority => 1;
        public ulong OrderId { get; init; }
        public decimal Price { get; init; }
        public decimal Qty { get; init; }
    }
}
