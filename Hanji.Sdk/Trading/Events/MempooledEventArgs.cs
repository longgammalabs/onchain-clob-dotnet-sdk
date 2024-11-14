namespace Hanji.Trading.Events
{
    public class MempooledEventArgs : EventArgs
    {
        public string RequestId { get; init; } = default!;
        public string TxId { get; init; } = default!;
    }
}
