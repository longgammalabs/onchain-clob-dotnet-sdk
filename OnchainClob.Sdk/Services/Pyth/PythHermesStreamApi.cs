using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Revelium.Evm.Common;

namespace OnchainClob.Services.Pyth
{
    public class PythHermesStreamApiOptions
    {
        public string? Url { get; set; } = "https://hermes.pyth.network/v2/updates/price/stream";
        public List<string>? PriceFeeds { get; set; }
    }

    public class PythPrice
    {
        [JsonPropertyName("price")]
        public string Price { get; set; } = default!;
        [JsonPropertyName("conf")]
        public string Conf { get; set; } = default!;
        [JsonPropertyName("expo")]
        public int Expo { get; set; }
        [JsonPropertyName("publish_time")]
        public long PublishTime { get; set; }
    }

    public class PythParsedData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = default!;
        [JsonPropertyName("price")]
        public PythPrice Price { get; set; } = default!;
        [JsonPropertyName("ema_price")]
        public PythPrice EmaPrice { get; set; } = default!;
    }

    public class PythBinaryData
    {
        [JsonPropertyName("encoding")]
        public string Encoding { get; set; } = default!;
        [JsonPropertyName("data")]
        public string[] Data { get; set; } = default!;
    }

    public class PythPriceData
    {
        [JsonPropertyName("binary")]
        public PythBinaryData Binary { get; set; } = default!;
        [JsonPropertyName("parsed")]
        public PythParsedData[] Parsed { get; set; } = default!;
    }

    public class PythPriceUpdatedEventArgs : EventArgs
    {
        public string FeedId { get; set; } = default!;
        public decimal Price { get; set; }
    }

    public class PythHermesStreamApi(
        PythHermesStreamApiOptions options,
        HttpClient? httpClient = null,
        ILogger<PythHermesStreamApi>? logger = null) : IHostedService
    {
        public event EventHandler<PythPriceUpdatedEventArgs>? PriceUpdated;

        private readonly PythHermesStreamApiOptions _options = options;
        private readonly HttpClient _httpClient = httpClient ?? new HttpClient();
        private readonly ILogger<PythHermesStreamApi>? _logger = logger;
        private CancellationTokenSource? _cts;
        private bool _isRunning;

        public Task StartAsync(CancellationToken ct)
        {
            if (_isRunning)
                return Task.CompletedTask;

            _logger?.LogInformation("Starting PythHermesStreamApi");

            _isRunning = true;
            _cts = new CancellationTokenSource();

            _ = UpdatePriceAsync(_cts.Token);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct)
        {
            if (!_isRunning)
                return Task.CompletedTask;

            _logger?.LogInformation("Stopping PythHermesStreamApi");

            _isRunning = false;
            _cts?.Cancel();

            return Task.CompletedTask;
        }

        private async Task UpdatePriceAsync(CancellationToken ct)
        {
            var idsQuery = string.Join("&", _options.PriceFeeds.Select(id => $"ids[]={id}").ToList());
            var requestUri = $"{_options.Url}?{idsQuery}";

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var stream = await _httpClient.GetStreamAsync(requestUri);
                    using var reader = new StreamReader(stream);

                    while (!reader.EndOfStream && !ct.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();

                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        if (line.StartsWith("data:"))
                        {
                            var jsonData = line["data:".Length..];
                            var pricesData = JsonSerializer.Deserialize<PythPriceData>(jsonData)!;

                            foreach (var priceData in pricesData.Parsed)
                            {
                                PriceUpdated?.Invoke(this, new PythPriceUpdatedEventArgs
                                {
                                    FeedId = priceData.Id,
                                    Price = BigInteger.Parse(priceData.Price.Price)
                                        .Divide(BigInteger.Pow(10, Math.Abs(priceData.Price.Expo)))
                                });
                            }
                        }
                        else
                        {
                            _logger?.LogWarning("Received non-data line");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error updating prices from Pyth Hermes API");
                }
            }
        }
    }
}
