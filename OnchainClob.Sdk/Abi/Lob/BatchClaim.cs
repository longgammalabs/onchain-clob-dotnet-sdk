using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace OnchainClob.Abi.Lob
{
    [Function("batchClaim")]
    public class BatchClaim : FunctionMessage
    {
        [Parameter("address[]", "addresses", 1)]
        public List<string> Addresses { get; set; } = default!;

        [Parameter("uint64[]", "order_ids", 2)]
        public List<ulong> OrderIds { get; set; } = default!;
        [Parameter("bool", "only_claim", 3)]
        public bool OnlyClaim { get; set; }

        [Parameter("uint256", "expires", 4)]
        public BigInteger Expires { get; set; }
    }
}
