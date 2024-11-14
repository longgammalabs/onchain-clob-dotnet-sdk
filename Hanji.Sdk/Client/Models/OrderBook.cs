using System.Text.Json.Serialization;

namespace Hanji.Client.Models
{
    public class OrderBook
    {
        [JsonPropertyName("timestamp")]
        public long TimeStamp { get; init; }
        [JsonPropertyName("levels")]
        public Levels Levels { get; init; } = default!;
    }

    public class Levels
    {
        [JsonPropertyName("bids")]
        public List<Level> Bids { get; init; } = default!;
        [JsonPropertyName("asks")]
        public List<Level> Asks { get; init; } = default!;
    }

    public class Level
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = default!;
        [JsonPropertyName("price")]
        public string Price { get; init; } = default!;
        [JsonPropertyName("size")]
        public string Size { get; init; } = default!;
        [JsonPropertyName("n")]
        public long OrdersCount { get; init; }
    }
}
