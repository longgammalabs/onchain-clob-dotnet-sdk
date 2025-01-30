namespace OnchainClob.Client.Configuration
{
    public class GasLimits
    {
        public ulong PlaceOrder { get; init; }
        public ulong ClaimOrder { get; init; }
        public ulong ChangeOrder { get; init; }
        public ulong MaxPerTransaction { get; init; }
    }
}
