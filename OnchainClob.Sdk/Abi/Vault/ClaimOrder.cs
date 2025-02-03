using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace OnchainClob.Abi.Vault
{
    [Function("claimOrder")]
    public class ClaimOrder : FunctionMessage
    {
        [Parameter("uint8", "lobId", 1)]
        public byte LobId { get; set; }

        [Parameter("uint64", "orderId", 2)]
        public ulong OrderId { get; set; }

        [Parameter("bool", "onlyClaim", 3)]
        public bool OnlyClaim { get; set; }

        [Parameter("uint256", "expires", 4)]
        public BigInteger Expires { get; set; }
    }
}
