namespace OnchainClob.Trading.Requests
{
    public interface ITraderRequest
    {
        public int Priority { get; }
        public ulong OrderId { get; }
        public decimal Price { get; }
        public decimal Qty { get; }
    }
}
