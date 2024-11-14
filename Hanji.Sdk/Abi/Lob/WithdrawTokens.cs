using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace Hanji.Abi.Lob
{
    [Function("withdrawTokens")]
    public class WithdrawTokensFunction : FunctionMessage
    {
        [Parameter("bool", "withdraw_all", 1)]
        public bool WithdrawAll { get; set; }

        [Parameter("uint128", "token_x_amount", 2)]
        public BigInteger TokenXAmount { get; set; }

        [Parameter("uint128", "token_y_amount", 3)]
        public BigInteger TokenYAmount { get; set; }
    }
}
