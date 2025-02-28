using Microsoft.Extensions.Logging;
using OnchainClob.Client.Abstract;
using OnchainClob.Trading.Events;
using Revelium.Evm.Common.Events;
using Revelium.Evm.Crypto.Abstract;
using Revelium.Evm.Rpc;
using System.Collections.Concurrent;
using ErrorEventArgs = OnchainClob.Trading.Events.ErrorEventArgs;
using TransactionReceipt = Revelium.Evm.Rpc.Models.TransactionReceipt;

namespace OnchainClob.Client.Rpc
{
    /// <summary>
    /// Executes blockchain transactions asynchronously with local request queuing and rate limiting.
    /// Provides ordered transaction execution with configurable local queue to prevent RPC server overload.
    /// </summary>
    public class RpcQueuedAsyncExecutor : IAsyncExecutor
    {
        private const int RPC_QUEUE_SIZE = 16;
        private const int TRACKER_UPDATE_INTERVAL_SEC = 3;

        private readonly RpcClient _rpc;
        private readonly RpcCallSequencer _sequencer;
        private readonly ISigner _signer;
        private readonly RpcTransactionTracker _tracker;
        private readonly ILogger<RpcQueuedAsyncExecutor>? _logger;
        private readonly ConcurrentDictionary<string, string> _txIdToCallId = [];

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
        /// Initializes a new instance of the RpcQueuedAsyncExecutor class.
        /// </summary>
        /// <param name="rpc">The RPC client.</param>
        /// <param name="signer">The signer for transaction signing.</param>
        /// <param name="tracker">The transaction tracker for monitoring transaction status.</param>
        /// <param name="logger">Optional logger.</param>
        /// <exception cref="ArgumentNullException">Thrown when rpc, signer, or tracker is null.</exception>
        public RpcQueuedAsyncExecutor(
            RpcClient rpc,
            ISigner signer,
            RpcTransactionTracker tracker,
            ILogger<RpcQueuedAsyncExecutor>? logger = null)
        {
            _signer = signer ?? throw new ArgumentNullException(nameof(signer));
            _logger = logger;

            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _tracker.ReceiptReceived += Tracker_ReceiptReceived;
            _tracker.ErrorReceived += Tracker_ErrorReceived;

            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
            _sequencer = RpcCallSequencer.GetOrAddInstance(_rpc, _signer, RPC_QUEUE_SIZE);
        }

        /// <summary>
        /// Executes a blockchain transaction asynchronously.
        /// </summary>
        /// <param name="requestParams">The transaction request parameters.</param>
        /// <param name="cancellationToken">Optional token to cancel the operation.</param>
        public Task ExecuteAsync(
            TransactionRequestParams requestParams,
            CancellationToken cancellationToken = default)
        {
            return _sequencer.EnqueueAsync(
                requestParams.RequestId,
                requestParams,
                Sequencer_OnSuccess,
                Sequencer_OnError,
                cancellationToken);
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
            return _sequencer.TryCancelAsync(requestId, cancellationToken);
        }

        private Task Sequencer_OnSuccess(
            SuccessCallEventArgs<TransactionRequestParams, string> args,
            CancellationToken cancellationToken)
        {
            var txId = args.Result;

            TxMempooled?.Invoke(this, new MempooledEventArgs
            {
                TxId = txId,
                RequestId = args.CallId
            });

            _txIdToCallId.TryAdd(txId, args.CallId);

            _ = _tracker.TrackTransactionAsync(
                txId,
                TimeSpan.FromSeconds(TRACKER_UPDATE_INTERVAL_SEC));

            return Task.CompletedTask;
        }

        private Task Sequencer_OnError(
            ErrorCallEventArgs<TransactionRequestParams> args,
            CancellationToken cancellationToken)
        {
            Error?.Invoke(this, new ErrorEventArgs
            {
                RequestId = args.CallId,
                Error = args.Error
            });

            return Task.CompletedTask;
        }

        private async void Tracker_ReceiptReceived(object sender, TransactionReceipt e)
        {
            if (_txIdToCallId.TryRemove(e.TransactionHash, out var callId))
                await _sequencer.CompleteAsync(callId);

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
