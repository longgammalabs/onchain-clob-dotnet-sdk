using System.Text.Json.Serialization;

namespace OnchainClob.Client.Models
{
    public class UserFill
    {
        [JsonPropertyName("dir")]
        public string Dir { get; init; } = default!;
        [JsonPropertyName("orderId")]
        public string OrderId { get; init; } = default!;
        [JsonPropertyName("owner")]
        public string Owner { get; init; } = default!;
        [JsonPropertyName("price")]
        public string Price { get; init; } = default!;
        [JsonPropertyName("side")]
        public string Side { get; init; } = default!;
        [JsonPropertyName("size")]
        public string Size { get; init; } = default!;
        [JsonPropertyName("timestamp")]
        public long TimeStamp { get; init; }
        [JsonPropertyName("tradeId")]
        public string TradeId { get; init; } = default!;
        [JsonPropertyName("txnHash")]
        public string TxnHash { get; init; } = default!;
        [JsonPropertyName("type")]
        public string Type { get; init; } = default!;
    }
}
