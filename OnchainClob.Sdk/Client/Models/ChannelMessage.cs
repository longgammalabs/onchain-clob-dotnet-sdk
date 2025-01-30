using System.Text.Json.Serialization;

namespace OnchainClob.Client.Models
{
    public class ChannelMessage<T>
    {
        [JsonPropertyName("channel")]
        public string Channel { get; init; } = default!;
        [JsonPropertyName("id")]
        public string Id { get; init; } = default!;
        [JsonPropertyName("data")]
        public T Data { get; init; } = default!;
        [JsonPropertyName("isSnapshot")]
        public bool IsSnapshot { get; init; }
    }
}
