using Incendium;

namespace OnchainClob.Trading.Events
{
    public class ErrorEventArgs : EventArgs
    {
        public string RequestId { get; init; } = default!;
        public Error? Error { get; init; } = default!;
    }
}
