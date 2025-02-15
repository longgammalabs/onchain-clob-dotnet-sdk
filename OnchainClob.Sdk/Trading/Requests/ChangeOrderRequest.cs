using System.Numerics;

namespace OnchainClob.Trading.Requests
{
    public class ChangeOrderRequest : ITraderRequest
    {
        public int Priority => 1;
        public ulong OrderId { get; init; }
        public BigInteger Price { get; init; }
        public BigInteger Qty { get; init; }
    }
}
