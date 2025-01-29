namespace Hanji.Client.Configuration
{
    public class HanjiGasLimits
    {
        public ulong PlaceOrder { get; init; }
        public ulong ClaimOrder { get; init; }
        public ulong ChangeOrder { get; init; }
        public ulong MaxPerTransaction { get; init; }
    }
}
