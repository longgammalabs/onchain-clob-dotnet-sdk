using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace OnchainClob.Abi.Helper
{
    [FunctionOutput]
    public class FirstLevelOutputDTO : IFunctionOutputDTO
    {
        [Parameter("uint56", "bid", 1)]
        public ulong Bid { get; set; }
        [Parameter("uint56", "ask", 2)]
        public ulong Ask { get; set; }
    }


    [Function("firstLevel", typeof(FirstLevelOutputDTO))]
    public class FirstLevel : FunctionMessage
    {
        [Parameter("address", "lob_address", 1)]
        public string LobAddress { get; set; } = default!;
    }
}
