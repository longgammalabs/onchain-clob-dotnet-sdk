using OnchainClob.Client.Events;

namespace OnchainClob.Trading.Abstract
{
    public interface IVaultTrader : ITrader
    {
        event EventHandler<VaultTotalValuesEventArgs>? VaultTotalValuesChanged;
    }
}
