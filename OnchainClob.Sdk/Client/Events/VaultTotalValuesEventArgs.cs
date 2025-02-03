using OnchainClob.Client.Models;

namespace OnchainClob.Client.Events
{
    public class VaultTotalValuesEventArgs
    {
        public VaultTotalValues VaultTotalValues { get; init; } = default!;
    }
}
