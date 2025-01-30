using System.Text.Json.Serialization;

namespace OnchainClob.Client.Models
{
    public class MarketId
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = default!;
    }
}
