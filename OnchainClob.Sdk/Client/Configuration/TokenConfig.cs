namespace OnchainClob.Client.Configuration
{
    public class TokenConfig
    {
        public string Name { get; init; } = default!;
        public string ContractAddress { get; init; } = default!;
        public int Decimals { get; init; }
        public bool IsNative { get; init; }
        public string? PriceFeedId { get; init; }
    }
}
