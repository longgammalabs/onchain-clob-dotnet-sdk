using Hanji.Client.Configuration;
using Revelium.Evm.Rpc;

namespace Hanji.Trading.Configuration
{
    public interface IHanjiSpotTraderConfig
    {
        ISymbolConfig GetSymbol(string symbol);
        ISymbolConfig? GetSymbolByContract(string contractAddress);
        RpcConfig GetRpcConfig(string networkId);
    }
}
