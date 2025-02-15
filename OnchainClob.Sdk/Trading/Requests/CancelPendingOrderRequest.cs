using System.Numerics;

namespace OnchainClob.Trading.Requests
{
    public class CancelPendingOrderRequest : ITraderRequest
    {
        public int Priority => 3;
        public string RequestId { get; init; } = default!;
        public ulong OrderId => 0;
        public BigInteger Price => 0;
        public BigInteger Qty => 0;
    }
}
