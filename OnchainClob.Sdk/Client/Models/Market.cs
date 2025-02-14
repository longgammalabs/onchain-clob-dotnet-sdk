using System.Text.Json.Serialization;

namespace OnchainClob.Client.Models
{
    public class Token
    {
        /// <summary>
        /// Token identifier, typically the contract address.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; init; } = default!;

        /// <summary>
        /// Human-readable name of the token.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; init; } = default!;

        /// <summary>
        /// Token symbol (e.g., "ETH", "BTC").
        /// </summary>
        [JsonPropertyName("symbol")]
        public string Symbol { get; init; } = default!;

        /// <summary>
        /// Smart contract address of the token.
        /// </summary>
        [JsonPropertyName("contractAddress")]
        public string ContractAddress { get; init; } = default!;

        /// <summary>
        /// Number of decimal places for token amounts.
        /// </summary>
        [JsonPropertyName("decimals")]
        public int Decimals { get; init; }

        /// <summary>
        /// Number of decimal places for display rounding.
        /// </summary>
        [JsonPropertyName("roundingDecimals")]
        public int RoundingDecimals { get; init; }

        /// <summary>
        /// Indicates if token supports EIP-2612 permit.
        /// </summary>
        [JsonPropertyName("supportsPermit")]
        public bool SupportsPermit { get; init; }

        /// <summary>
        /// URL to token's icon image.
        /// </summary>
        [JsonPropertyName("iconUrl")]
        public string? IconUrl { get; init; }

        /// <summary>
        /// Indicates if token is from original deployment.
        /// </summary>
        [JsonPropertyName("fromOg")]
        public bool FromOg { get; init; }

        /// <summary>
        /// Indicates if token is the blockchain's native currency.
        /// </summary>
        [JsonPropertyName("isNative")]
        public bool IsNative { get; init; }

        /// <summary>
        /// Price feed identifier for the token.
        /// </summary>
        [JsonPropertyName("priceFeed")]
        public string PriceFeed { get; init; } = default!;

        /// <summary>
        /// Number of decimal places in price feed data.
        /// </summary>
        [JsonPropertyName("priceFeedDecimals")]
        public int PriceFeedDecimals { get; init; }

        /// <summary>
        /// Indicates if token was added by a user.
        /// </summary>
        [JsonPropertyName("isUserGenerated")]
        public bool IsUserGenerated { get; init; }
    }

    public class Market
    {
        /// <summary>
        /// Market identifier.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; init; } = default!;

        /// <summary>
        /// Human-readable name of the market.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; init; } = default!;

        /// <summary>
        /// Market symbol.
        /// </summary>
        [JsonPropertyName("symbol")]
        public string Symbol { get; init; } = default!;

        /// <summary>
        /// Orderbook contract address.
        /// </summary>
        [JsonPropertyName("orderbookAddress")]
        public string OrderbookAddress { get; init; } = default!;

        /// <summary>
        /// Price aggregation levels.
        /// </summary>
        [JsonPropertyName("aggregations")]
        public int[] Aggregations { get; init; } = [];

        /// <summary>
        /// Last traded price.
        /// </summary>
        [JsonPropertyName("lastPrice")]
        public decimal? LastPrice { get; init; }

        /// <summary>
        /// Lowest price in last 24 hours.
        /// </summary>
        [JsonPropertyName("lowPrice24h")]
        public decimal? LowPrice24h { get; init; }

        /// <summary>
        /// Highest price in last 24 hours.
        /// </summary>
        [JsonPropertyName("highPrice24h")]
        public decimal? HighPrice24h { get; init; }

        /// <summary>
        /// Price 24 hours ago.
        /// </summary>
        [JsonPropertyName("price24h")]
        public decimal? Price24h { get; init; }

        /// <summary>
        /// Best bid price.
        /// </summary>
        [JsonPropertyName("bestBid")]
        public decimal? BestBid { get; init; }

        /// <summary>
        /// Best ask price.
        /// </summary>
        [JsonPropertyName("bestAsk")]
        public decimal? BestAsk { get; init; }

        /// <summary>
        /// Trading volume in last 24 hours.
        /// </summary>
        [JsonPropertyName("tradingVolume24h")]
        public string TradingVolume24h { get; init; } = "0";

        /// <summary>
        /// Total supply.
        /// </summary>
        [JsonPropertyName("totalSupply")]
        public decimal? TotalSupply { get; init; }

        /// <summary>
        /// CoinMarketCap identifier.
        /// </summary>
        [JsonPropertyName("coinMarketCapId")]
        public int? CoinMarketCapId { get; init; }

        /// <summary>
        /// Last update timestamp.
        /// </summary>
        [JsonPropertyName("lastTouched")]
        public long LastTouched { get; init; }

        /// <summary>
        /// Indicates if market supports native token.
        /// </summary>
        [JsonPropertyName("supportsNativeToken")]
        public bool SupportsNativeToken { get; init; }

        /// <summary>
        /// Indicates if base token is native.
        /// </summary>
        [JsonPropertyName("isNativeTokenX")]
        public bool IsNativeTokenX { get; init; }

        /// <summary>
        /// Base token scaling factor.
        /// </summary>
        [JsonPropertyName("tokenXScalingFactor")]
        public int TokenXScalingFactor { get; init; }

        /// <summary>
        /// Quote token scaling factor.
        /// </summary>
        [JsonPropertyName("tokenYScalingFactor")]
        public int TokenYScalingFactor { get; init; }

        /// <summary>
        /// Price scaling factor.
        /// </summary>
        [JsonPropertyName("priceScalingFactor")]
        public int PriceScalingFactor { get; init; }

        /// <summary>
        /// Fee for aggressive orders.
        /// </summary>
        [JsonPropertyName("aggressiveFee")]
        public decimal AggressiveFee { get; init; }

        /// <summary>
        /// Fee for passive orders.
        /// </summary>
        [JsonPropertyName("passiveFee")]
        public decimal PassiveFee { get; init; }

        /// <summary>
        /// Payout for passive orders.
        /// </summary>
        [JsonPropertyName("passiveOrderPayout")]
        public decimal PassiveOrderPayout { get; init; }

        /// <summary>
        /// Indicates if market was created by user.
        /// </summary>
        [JsonPropertyName("isUserGenerated")]
        public bool IsUserGenerated { get; init; }

        /// <summary>
        /// Base token information.
        /// </summary>
        [JsonPropertyName("baseToken")]
        public Token BaseToken { get; init; } = default!;

        /// <summary>
        /// Quote token information.
        /// </summary>
        [JsonPropertyName("quoteToken")]
        public Token QuoteToken { get; init; } = default!;
    }
}
