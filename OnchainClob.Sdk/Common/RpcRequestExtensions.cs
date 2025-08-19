using Revelium.Evm.Rpc;

namespace OnchainClob.Common;

public static class RpcRequestExtensions
{
    public static IEnumerable<RpcRequest> UseAutoIncrementedIds(
        this IEnumerable<RpcRequest> requests,
        int initialId = 1)
    {
        foreach (var request in requests)
            request.Id = initialId++;

        return requests;
    }
}
