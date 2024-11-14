using Nethereum.ABI.FunctionEncoding.Attributes;
using System.Numerics;

namespace Hanji.Abi.Lob.Events
{
    [Event("Deposited")]
    public class DepositedEventDTO : IEventDTO
    {
        [Parameter("address", "owner", 1, true)]
        public string Owner { get; set; } = default!;

        [Parameter("uint128", "token_x", 2, false)]
        public BigInteger TokenX { get; set; }

        [Parameter("uint128", "token_y", 3, false)]
        public BigInteger TokenY { get; set; }
    }
}
