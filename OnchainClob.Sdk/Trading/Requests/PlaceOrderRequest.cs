using OnchainClob.Common;

namespace OnchainClob.Trading.Requests
{
    public class PlaceOrderRequest : ITraderRequest
    {
        public int Priority => 0;
        public decimal Price { get; init; }
        public decimal Qty { get; init; }
        public Side Side { get; init; }
        public ulong OrderId => Side == Side.Buy ? 0u : 1u;
    }
}
