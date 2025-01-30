namespace OnchainClob.MarketData.Abstract
{
    public interface IOrderBookProvider
    {
        event EventHandler<OrderBook> OrderBookUpdated;
        event EventHandler<bool> AvailabilityChanged;

        bool IsAvailable { get; }

        OrderBook? GetOrderBook(string currency, string quoteCurrency);
    }
}
