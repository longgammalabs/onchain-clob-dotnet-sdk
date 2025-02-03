using System.Text.Json.Serialization;

namespace OnchainClob.Client.Models
{
    public class VaultTotalValues
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = default!;

        [JsonPropertyName("totalUSDValue")]
        public decimal TotalUSDValue { get; init; }

        [JsonPropertyName("totalUSDCostBasis")]
        public decimal TotalUSDCostBasis { get; init; }

        [JsonPropertyName("pastWeekReturn")]
        public decimal PastWeekReturn { get; init; }

        [JsonPropertyName("leaderAddress")]
        public string LeaderAddress { get; init; } = default!;

        [JsonPropertyName("leaderUSDValue")]
        public decimal LeaderUSDValue { get; init; }

        [JsonPropertyName("vaultPerformance")]
        public VaultPerformanceValues VaultPerformance { get; init; } = default!;

        [JsonPropertyName("totalSupply")]
        public string TotalSupply { get; init; }

        [JsonPropertyName("totalWeight")]
        public decimal TotalWeight { get; init; }

        [JsonPropertyName("tokens")]
        public TokenInfo[] Tokens { get; init; } = default!;
    }

    public class VaultPerformanceValues
    {
        [JsonPropertyName("pnlPerformance")]
        public decimal PnlPerformance { get; init; }

        [JsonPropertyName("maxDrowdownPercentage")]
        public decimal MaxDrowdownPercentage { get; init; }

        [JsonPropertyName("volume")]
        public decimal Volume { get; init; }

        [JsonPropertyName("profitShare")]
        public decimal ProfitShare { get; init; }
    }

    public class TokenInfo
    {
        [JsonPropertyName("address")]
        public string Address { get; init; } = default!;

        [JsonPropertyName("symbol")]
        public string Symbol { get; init; } = default!;

        [JsonPropertyName("tokenPriceUSD")]
        public decimal TokenPriceUSD { get; init; }

        [JsonPropertyName("tokenBalance")]
        public string TokenBalance { get; init; } = default!;

        [JsonPropertyName("tokenReserved")]
        public string TokenReserved { get; init; } = default!;

        [JsonPropertyName("tokenWeight")]
        public decimal TokenWeight { get; init; }
    }
}
