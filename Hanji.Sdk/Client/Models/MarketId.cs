using System.Text.Json.Serialization;

namespace Hanji.Client.Models
{
    public class MarketId
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = default!;
    }
}
