namespace Hanji.MarketData.Models
{
    public class Quote
    {
        public string Symbol { get; init; } = default!;
        public DateTime TimeStamp { get; init; }
        public decimal Bid { get; init; }
        public decimal Ask { get; init; }

        public bool IsValidBid() =>
            Bid != 0;

        public bool IsValidAsk() =>
            Ask != 0 && Ask != decimal.MaxValue;

        public decimal GetMiddlePrice() => IsValidBid() && IsValidAsk()
            ? (Ask + Bid) / 2
            : IsValidBid()
                ? Bid
                : IsValidAsk()
                    ? Ask
                    : 0m;
    }
}
