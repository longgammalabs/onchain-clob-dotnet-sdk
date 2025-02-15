using System.Text.Json.Serialization;

namespace OnchainClob.Client.Models
{
    public class Trade
    {
        /// <summary>
        /// The unique trade identifier.
        /// </summary>
        [JsonPropertyName("tradeId")]
        public string TradeId { get; init; } = default!;

        /// <summary>
        /// Identifies trade direction ("buy" or "sell").
        /// </summary>
        [JsonPropertyName("direction")]
        public string Direction { get; init; } = default!;

        /// <summary>
        /// Price per token in the order
        /// </summary>
        [JsonPropertyName("price")]
        public string Price { get; init; } = default!;

        /// <summary>
        /// Token quantity filled by trade
        /// </summary>
        [JsonPropertyName("size")]
        public string Size { get; init; } = default!;

        /// <summary>
        /// Block timestamp when the trade was concluded.
        /// </summary>
        [JsonPropertyName("timestamp")]
        public long Timestamp { get; init; }

        /// <summary>
        /// Transaction hash in which the trade was concluded.
        /// </summary>
        [JsonPropertyName("txnHash")]
        public string TxnHash { get; init; } = default!;
    }
}
