using OnchainClob.Common;

namespace OnchainClob.MarketData.Models
{
    public class Entry
    {
        public long TransactionId { get; init; }
        public string Symbol { get; init; } = default!;
        public Side Side { get; init; }
        public decimal Price { get; init; }
        public IList<decimal> QtyProfile { get; init; } = default!;

        public decimal Qty() => QtyProfile?.Sum() ?? 0;
    }
}
