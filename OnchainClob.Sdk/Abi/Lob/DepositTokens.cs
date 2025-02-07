using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace OnchainClob.Abi.Lob
{
    [Function("depositTokens")]
    public class DepositTokens : FunctionMessage
    {
        [Parameter("uint128", "token_x_amount", 1)]
        public BigInteger TokenXAmount { get; set; }

        [Parameter("uint128", "token_y_amount", 2)]
        public BigInteger TokenYAmount { get; set; }
    }

    [Function("depositTokens")]
    public class DepositTokensWithPermit : FunctionMessage
    {
        [Parameter("uint128", "token_x_amount", 1)]
        public BigInteger TokenXAmount { get; set; }

        [Parameter("uint128", "token_y_amount", 2)]
        public BigInteger TokenYAmount { get; set; }

        [Parameter("uint8", "v_x", 3)]
        public byte VX { get; set; }

        [Parameter("bytes32", "r_x", 4)]
        public byte[] RX { get; set; } = default!;

        [Parameter("bytes32", "s_x", 5)]
        public byte[] SX { get; set; } = default!;

        [Parameter("uint8", "v_y", 6)]
        public byte VY { get; set; }

        [Parameter("bytes32", "r_y", 7)]
        public byte[] RY { get; set; } = default!;

        [Parameter("bytes32", "s_y", 8)]
        public byte[] SY { get; set; } = default!;

        [Parameter("uint256", "expires", 9)]
        public BigInteger Expires { get; set; }
    }
}
