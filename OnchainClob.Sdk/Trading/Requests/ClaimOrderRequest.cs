using System.Numerics;

namespace OnchainClob.Trading.Requests
{
    public class ClaimOrderRequest : ITraderRequest
    {
        public int Priority => 2;
        public ulong OrderId { get; init; }
        public BigInteger Price => 0;
        public BigInteger Qty => 0;
    }
}
