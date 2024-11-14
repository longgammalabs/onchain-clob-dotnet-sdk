using System.Text.Json.Serialization;

namespace Hanji.Client.Models
{
    public class UserOrder
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = default!;
        [JsonPropertyName("claimed")]
        public string Claimed { get; init; } = default!;
        [JsonPropertyName("createdAt")]
        public long CreatedAt { get; init; }
        [JsonPropertyName("lastTouched")]
        public long LastTouched { get; init; }
        [JsonPropertyName("orderId")]
        public string OrderId { get; init; } = default!;
        [JsonPropertyName("origSize")]
        public string OrigSize { get; init; } = default!;
        [JsonPropertyName("owner")]
        public string Owner { get; init; } = default!;
        [JsonPropertyName("price")]
        public string Price { get; init; } = default!;
        [JsonPropertyName("side")]
        public string Side { get; init; } = default!;
        [JsonPropertyName("size")]
        public string Size { get; init; } = default!;
        [JsonPropertyName("status")]
        public string Status { get; init; } = default!;
        [JsonPropertyName("type")]
        public string Type { get; init; } = default!;
        [JsonPropertyName("market")]
        public MarketId Market { get; init; } = default!;
        [JsonPropertyName("txnHash")]
        public string TxnHash { get; init; } = default!;
    }
}
