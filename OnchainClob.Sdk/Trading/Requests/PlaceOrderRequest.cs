using OnchainClob.Common;
using System.Numerics;

namespace OnchainClob.Trading.Requests
{
    public class PlaceOrderRequest : ITraderRequest
    {
        public int Priority => 0;
        public BigInteger Price { get; init; }
        public BigInteger Qty { get; init; }
        public Side Side { get; init; }
        public ulong OrderId => Side == Side.Buy ? 0u : 1u;
    }
}
