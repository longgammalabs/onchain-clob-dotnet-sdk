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
    public class RpcSequencer : IExecutor
    {
        private const int RPC_QUEUE_SIZE = 16;
        private const int TRACKER_UPDATE_INTERVAL_SEC = 3;

        private readonly RpcClient _rpc;
        private readonly RpcCallSequencer _sequencer;
        private readonly ISigner _signer;
        private readonly RpcTransactionTracker _tracker;
        private readonly ILogger<RpcSequencer>? _logger;
        private readonly ConcurrentDictionary<string, string> _txIdToCallId = [];

        public event EventHandler<MempooledEventArgs>? TxMempooled;
        public event EventHandler<ConfirmedEventArgs>? TxSuccessful;
        public event EventHandler<ConfirmedEventArgs>? TxFailed;
        public event EventHandler<ErrorEventArgs>? Error;

        public ISigner Signer => _signer;

        public RpcSequencer(
            RpcClient rpc,
            ISigner signer,
            RpcTransactionTracker tracker,
            ILogger<RpcSequencer>? logger = null)
        {
            _signer = signer ?? throw new ArgumentNullException(nameof(signer));
            _logger = logger;

            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _tracker.ReceiptReceived += Tracker_ReceiptReceived;
            _tracker.ErrorReceived += Tracker_ErrorReceived;

            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
            _sequencer = RpcCallSequencer.GetOrAddInstance(_rpc, _signer, RPC_QUEUE_SIZE);
        }

        public async Task<string> ExecuteAsync(
            TransactionRequestParams requestParams,
            CancellationToken cancellationToken = default)
        {
            return await _sequencer.EnqueueAsync(
                requestParams,
                Sequencer_OnSuccess,
                Sequencer_OnError,
                cancellationToken);
        }

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
