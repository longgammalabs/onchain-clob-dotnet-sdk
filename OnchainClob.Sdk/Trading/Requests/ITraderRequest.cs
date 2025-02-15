using System.Numerics;

namespace OnchainClob.Trading.Requests
{
    public interface ITraderRequest
    {
        public int Priority { get; }
        public ulong OrderId { get; }
        public BigInteger Price { get; }
        public BigInteger Qty { get; }
    }
}
