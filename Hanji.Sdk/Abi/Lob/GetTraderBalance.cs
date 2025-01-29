using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace Hanji.Abi.Lob
{
    [FunctionOutput]
    public class GetTraderBalanceOutput : IFunctionOutputDTO
    {
        [Parameter("uint128", 1)]
        public BigInteger TokenX { get; set; }
        [Parameter("uint128", 2)]
        public BigInteger TokenY { get; set; }
        [Parameter("bool", 3)]
        public bool ClaimableStatus { get; set; }
    }

    [Function("getTraderBalance", typeof(GetTraderBalanceOutput))]
    public class GetTraderBalance : FunctionMessage
    {
        [Parameter("address", "address_", 1)]
        public string Address { get; init; } = default!;
    }
}
