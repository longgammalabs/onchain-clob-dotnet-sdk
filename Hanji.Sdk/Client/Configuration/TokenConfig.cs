namespace Hanji.Client.Configuration
{
    public class TokenConfig
    {
        public string ContractAddress { get; init; } = default!;
        public int Decimals { get; init; }
        public bool IsNative { get; init; }
    }
}
