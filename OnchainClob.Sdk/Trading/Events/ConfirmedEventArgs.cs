using Revelium.Evm.Rpc.Models;

namespace OnchainClob.Trading.Events
{
    public class ConfirmedEventArgs : EventArgs
    {
        public TransactionReceipt Receipt { get; init; } = default!;
    }
}
