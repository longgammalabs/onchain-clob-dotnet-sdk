using Revelium.Evm.Rpc.Models;

namespace Hanji.Trading.Events
{
    public class ConfirmedEventArgs : EventArgs
    {
        public TransactionReceipt Receipt { get; init; } = default!;
    }
}
