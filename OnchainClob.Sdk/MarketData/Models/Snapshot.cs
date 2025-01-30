namespace OnchainClob.MarketData.Models
{
    public class Snapshot
    {
        public long LastTransactionId { get; init; }
        public string Symbol { get; init; } = default!;
        public List<Entry> Entries { get; init; } = default!;
    }
}
