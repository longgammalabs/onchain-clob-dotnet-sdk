using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace Hanji.Abi.LobFactory
{
    [Function("setDeployer")]
    public class SetDeployer : FunctionMessage
    {
        [Parameter("address", "deployer", 1)]
        public string Deployer { get; set; } = default!;

        [Parameter("bool", "allowed", 2)]
        public bool Allowed { get; set; }
    }
}
