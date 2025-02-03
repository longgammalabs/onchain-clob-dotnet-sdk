namespace OnchainClob.Client.Configuration
{
    public interface IVaultSymbolConfig : ISymbolConfig
    {
        public byte LobId { get; }
    }
}
