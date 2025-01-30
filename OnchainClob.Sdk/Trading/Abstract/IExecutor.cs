using OnchainClob.Trading.Events;
using Revelium.Evm.Crypto.Abstract;
using Revelium.Evm.Rpc;
using ErrorEventArgs = OnchainClob.Trading.Events.ErrorEventArgs;

namespace OnchainClob.Trading.Abstract
{
    public interface IExecutor
    {
        event EventHandler<MempooledEventArgs>? TxMempooled;
        event EventHandler<ConfirmedEventArgs>? TxSuccessful;
        event EventHandler<ConfirmedEventArgs>? TxFailed;
        event EventHandler<ErrorEventArgs>? Error;

        ISigner Signer { get; }

        Task<string> ExecuteAsync(
            TransactionRequestParams requestParams,
            CancellationToken cancellationToken = default);

        Task<bool> TryCancelRequestAsync(
            string requestId,
            CancellationToken cancellationToken = default);
    }
}
