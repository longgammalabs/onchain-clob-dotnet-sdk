using Nethereum.ABI.FunctionEncoding.Attributes;
using System.Numerics;

namespace OnchainClob.Abi.Lob.Events
{
    [Event("Withdrawn")]
    public class WithdrawnEventDTO : IEventDTO
    {
        [Parameter("address", "owner", 1, true)]
        public string Owner { get; set; } = default!;

        [Parameter("uint128", "token_x", 2, false)]
        public BigInteger TokenX { get; set; }

        [Parameter("uint128", "token_y", 3, false)]
        public BigInteger TokenY { get; set; }
    }
}
