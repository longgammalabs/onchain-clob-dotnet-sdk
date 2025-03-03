using OnchainClob.Client.Events;

namespace OnchainClob.Trading.Abstract
{
    public interface IVaultTrader : ITrader
    {
        string VaultContractAddress { get; }

        event EventHandler<VaultTotalValuesEventArgs>? VaultTotalValuesChanged;
    }
}
