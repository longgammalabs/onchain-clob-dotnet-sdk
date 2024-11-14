using Hanji.Common;
using Hanji.MarketData.Models;

namespace Hanji.MarketData
{
    public class OrderBook(string symbol)
    {
        private long _lastTransactionId = 0;
        private readonly object _sync = new();

        public string Symbol { get; init; } = symbol ?? throw new ArgumentNullException(nameof(symbol));
        public readonly SortedDictionary<decimal, Entry> Buys = new(new DescendingComparer<decimal>());
        public readonly SortedDictionary<decimal, Entry> Sells = [];

        public Quote TopOfBook()
        {
            lock (_sync)
            {
                var quote = new Quote
                {
                    Symbol = Symbol,
                    TimeStamp = DateTime.UtcNow, // todo: change to last update time
                    Bid = Buys.Count != 0 ? Buys.First().Key : 0,
                    Ask = Sells.Count != 0 ? Sells.First().Key : decimal.MaxValue
                };

                return quote;
            }
        }

        public bool IsValid()
        {
            var quote = TopOfBook();

            return quote.Bid != 0 &&
                quote.Ask != 0 &&
                quote.Ask != decimal.MaxValue;
        }

        public void ApplySnapshot(Snapshot snapshot)
        {
            lock (_sync)
            {
                Buys.Clear();
                Sells.Clear();

                foreach (var entry in snapshot.Entries)
                    ApplyEntry(entry);

                _lastTransactionId = snapshot.LastTransactionId;
            }
        }

        public void ApplyEntry(Entry entry, bool checkTransactionId = false)
        {
            lock (_sync)
            {
                if (checkTransactionId && entry.TransactionId <= _lastTransactionId)
                    return;

                var book = entry.Side == Side.Buy ? Buys : Sells;

                if (entry.Qty() > 0)
                {
                    book[entry.Price] = entry;
                }
                else
                {
                    book.Remove(entry.Price);
                }
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                Buys.Clear();
                Sells.Clear();
            }
        }
    }
}
