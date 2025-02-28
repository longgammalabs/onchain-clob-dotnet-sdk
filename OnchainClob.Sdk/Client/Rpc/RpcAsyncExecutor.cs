using Microsoft.Extensions.Logging;
using OnchainClob.Client.Abstract;
using OnchainClob.Trading.Events;
using Revelium.Evm.Common;
using Revelium.Evm.Crypto.Abstract;
using Revelium.Evm.Rpc;
using Revelium.Evm.Rpc.Models;
using ErrorEventArgs = OnchainClob.Trading.Events.ErrorEventArgs;

namespace OnchainClob.Client.Rpc
{
    /// <summary>
    /// Executes transactions asynchronously using RPC and tracks their status.
    /// This executor signs and sends transactions to the blockchain, monitors their progress,
    /// and provides status updates through events.
    /// </summary>
    public class RpcAsyncExecutor : IAsyncExecutor
    {
        private const int TRACKER_UPDATE_INTERVAL_SEC = 3;

        private readonly RpcClient _rpc;
        private readonly ISigner _signer;
        private readonly RpcTransactionTracker _tracker;
        private readonly ILogger<RpcAsyncExecutor>? _logger;

        /// <summary>
        /// Fired when a transaction is successfully added to the mempool.
        /// </summary>
        public event EventHandler<MempooledEventArgs>? TxMempooled;
        /// <summary>
        /// Fired when a transaction is successfully confirmed on the blockchain.
        /// </summary>
        public event EventHandler<ConfirmedEventArgs>? TxSuccessful;
        /// <summary>
        /// Fired when a transaction fails after being confirmed on the blockchain.
        /// </summary>
        public event EventHandler<ConfirmedEventArgs>? TxFailed;
        /// <summary>
        /// Fired when an error occurs while sending a transaction.
        /// </summary>
        public event EventHandler<ErrorEventArgs>? Error;

        /// <summary>
        /// Gets the signer used by the executor.
        /// </summary>
        public ISigner Signer => _signer;

        /// <summary>
        /// Initializes a new instance of the RpcAsyncExecutor class.
        /// </summary>
        /// <param name="rpc">The RPC client.</param>
        /// <param name="signer">The signer for transaction signing.</param>
        /// <param name="tracker">The transaction tracker for monitoring transaction status.</param>
        /// <param name="logger">Optional logger.</param>
        /// <exception cref="ArgumentNullException">Thrown when rpc, signer, or tracker is null.</exception>
        public RpcAsyncExecutor(
            RpcClient rpc,
            ISigner signer,
            RpcTransactionTracker tracker,
            ILogger<RpcAsyncExecutor>? logger = null)
        {
            _signer = signer ?? throw new ArgumentNullException(nameof(signer));
            _logger = logger;

            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _tracker.ReceiptReceived += Tracker_ReceiptReceived;
            _tracker.ErrorReceived += Tracker_ErrorReceived;

            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
        }

        /// <summary>
        /// Executes a blockchain transaction asynchronously.
        /// </summary>
        /// <param name="requestParams">The transaction request parameters.</param>
        /// <param name="cancellationToken">Optional token to cancel the operation.</param>
        public async Task ExecuteAsync(
            TransactionRequestParams requestParams,
            CancellationToken cancellationToken = default)
        {
            var (txId, error) = await _rpc.SignAndSendTransactionAsync(
                requestParams.Tx,
                _signer,
                requestParams.EstimateGas,
                requestParams.EstimateGasReserveInPercent,
                networkId: null,
                logger: _logger,
                cancellationToken);

            _ = Task.Run(() =>
            {
                try
                {
                    if (error != null)
                    {
                        Error?.Invoke(this, new ErrorEventArgs
                        {
                            RequestId = requestParams.RequestId,
                            Error = error
                        });
                    }
                    else
                    {
                        TxMempooled?.Invoke(this, new MempooledEventArgs
                        {
                            RequestId = requestParams.RequestId,
                            TxId = txId
                        });

                        _ = _tracker.TrackTransactionAsync(
                            txId,
                            updateInterval: TimeSpan.FromSeconds(TRACKER_UPDATE_INTERVAL_SEC));
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error notifying result for {@RequestId}", requestParams.RequestId);
                }
            });
        }

        /// <summary>
        /// Tries to cancel a pending transaction request if possible.
        /// </summary>
        /// <param name="requestId">The unique identifier of the request to cancel.</param>
        /// <param name="cancellationToken">Optional token to cancel the operation.</param>
        /// <returns>True if the request was successfully canceled, false otherwise.</returns>
        public Task<bool> TryCancelRequestAsync(
            string requestId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        private void Tracker_ReceiptReceived(object sender, TransactionReceipt e)
        {
            var isTxFailed = e.Status == "0x0";

            if (isTxFailed)
            {
                TxFailed?.Invoke(this, new ConfirmedEventArgs { Receipt = e });
            }
            else
            {
                TxSuccessful?.Invoke(this, new ConfirmedEventArgs { Receipt = e });
            }
        }

        private void Tracker_ErrorReceived(object sender, Revelium.Evm.Rpc.Events.ErrorEventArgs e)
        {
            Error?.Invoke(this, new ErrorEventArgs
            {
                RequestId = e.TxId,
                Error = e.Error
            });
        }
    }
}
