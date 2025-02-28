using OnchainClob.Trading.Events;
using Revelium.Evm.Crypto.Abstract;
using Revelium.Evm.Rpc;
using ErrorEventArgs = OnchainClob.Trading.Events.ErrorEventArgs;

namespace OnchainClob.Client.Abstract
{
    public interface IAsyncExecutor
    {
        event EventHandler<MempooledEventArgs>? TxMempooled;
        event EventHandler<ConfirmedEventArgs>? TxSuccessful;
        event EventHandler<ConfirmedEventArgs>? TxFailed;
        event EventHandler<ErrorEventArgs>? Error;

        ISigner Signer { get; }

        Task ExecuteAsync(
            TransactionRequestParams requestParams,
            CancellationToken cancellationToken = default);

        Task<bool> TryCancelRequestAsync(
            string requestId,
            CancellationToken cancellationToken = default);
    }
}
