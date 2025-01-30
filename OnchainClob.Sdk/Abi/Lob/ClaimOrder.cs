using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace OnchainClob.Abi.Lob
{
    [Function("claimOrder")]
    public class ClaimOrder : FunctionMessage
    {
        [Parameter("uint64", "order_id", 1)]
        public ulong OrderId { get; set; }
        [Parameter("bool", "only_claim", 2)]
        public bool OnlyClaim { get; set; }
        [Parameter("bool", "transfer_tokens", 3)]
        public bool TransferTokens { get; set; }
        [Parameter("uint256", "expires", 4)]
        public BigInteger Expires { get; set; }
    }
}
