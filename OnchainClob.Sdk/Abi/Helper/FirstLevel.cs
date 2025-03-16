using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using System.Numerics;

namespace OnchainClob.Abi.Helper
{
    [FunctionOutput]
    public class FirstLevelOutputDTO : IFunctionOutputDTO
    {
        [Parameter("uint72", "bid", 1)]
        public BigInteger Bid { get; set; }
        [Parameter("uint72", "ask", 2)]
        public BigInteger Ask { get; set; }
    }


    [Function("firstLevel", typeof(FirstLevelOutputDTO))]
    public class FirstLevel : FunctionMessage
    {
        [Parameter("address", "lob_address", 1)]
        public string LobAddress { get; set; } = default!;
    }
}
