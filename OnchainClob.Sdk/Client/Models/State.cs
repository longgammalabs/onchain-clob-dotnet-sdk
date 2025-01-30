using System.Text.Json.Serialization;

namespace OnchainClob.Client.Models
{
    public class State
    {
        [JsonPropertyName("status")]
        public string Status { get; init; } = default!;
        [JsonPropertyName("lastTouched")]
        public long LastTouched { get; init; }
    }
}
