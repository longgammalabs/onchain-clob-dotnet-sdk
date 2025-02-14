namespace OnchainClob.Client.Models
{
    public class Trade
    {
        /// <summary>
        /// The unique trade identifier.
        /// </summary>
        public string TradeId { get; init; } = default!;

        /// <summary>
        /// Identifies trade direction ("buy" or "sell").
        /// </summary>
        public string Direction { get; init; } = default!;

        /// <summary>
        /// Price per token in the order, represented as a BigInteger string.
        /// </summary>
        public string Price { get; init; } = default!;

        /// <summary>
        /// Token quantity filled by trade, represented as a BigInteger string.
        /// </summary>
        public string Size { get; init; } = default!;

        /// <summary>
        /// Block timestamp when the trade was concluded.
        /// </summary>
        public long Timestamp { get; init; }

        /// <summary>
        /// Transaction hash in which the trade was concluded.
        /// </summary>
        public string TxnHash { get; init; } = default!;
    }
}
