using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OnchainClob.Common;
using System.Numerics;

namespace OnchainClob.Services.Pyth
{
    public class PythOptions
    {
        public string ContractAddress { get; init; } = default!;
        public string[] FeedIds { get; init; } = default!;
        public long UpdateFeePerFeed { get; init; } = default!;
        public int UpdateIntervalMs { get; init; } = default!;
        public int PriceValidityPeriodSec { get; init; } = default!;
    }

    public class Pyth(
        PythOptions options,
        PythHermesApi pythHermesApi,
        ILogger<Pyth>? logger = null) : IHostedService
    {
        public event EventHandler<EventArgs>? OnUpdate;

        private readonly PythOptions _options = options ?? throw new ArgumentNullException(nameof(options));
        private readonly PythHermesApi _pythHermesApi = pythHermesApi;
        private readonly ILogger<Pyth>? _logger = logger;
        private readonly object _startStopLock = new();
        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private bool _isRunning;
        private byte[][]? _priceUpdateData;
        private long _lastContractUpdateTime;

        public string PythContract => _options.ContractAddress;
        public string[] PriceFeedIds => _options.FeedIds;
        public BigInteger PriceUpdateFeePerFeed => _options.UpdateFeePerFeed;
        public BigInteger PriceUpdateFee => _options.UpdateFeePerFeed * _options.FeedIds.Length;

        public Task StartAsync(CancellationToken ct)
        {
            lock (_startStopLock)
            {
                if (_isRunning)
                    return Task.CompletedTask;

                _cts = new CancellationTokenSource();
                _isRunning = true;

                _ = Task.Run(async () => await DoWorkAsync(_cts.Token));
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct)
        {
            lock (_startStopLock)
            {
                if (!_isRunning)
                    return Task.CompletedTask;

                _cts?.Cancel();
                _cts = null;
                _isRunning = false;
            }

            return Task.CompletedTask;
        }

        public byte[][]? GetPriceUpdateData()
        {
            lock (_lock)
            {
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _lastContractUpdateTime > _options.PriceValidityPeriodSec)
                {
                    return _priceUpdateData;
                }
                else
                {
                    return null;
                }
            }
        }

        public long LastContractUpdateTime
        {
            get
            {
                lock (_lock)
                {
                    return _lastContractUpdateTime;
                }
            }
            set
            {
                lock (_lock)
                {
                    _lastContractUpdateTime = value;
                }
            }
        }

        private async Task DoWorkAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await UpdateAsync(cancellationToken);
                    await Task.Delay(_options.UpdateIntervalMs, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger?.LogInformation("Pyth worker cancelled");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating Pyth prices");
            }

            _logger?.LogInformation("Pyth worker stopped");
        }

        private async Task UpdateAsync(CancellationToken ct = default)
        {
            _logger?.LogDebug("Updating Pyth prices");

            var (priceUpdateData, error) = await _pythHermesApi.GetPriceUpdateDataAsync(
                PriceFeedIds,
                ct);

            if (error != null)
            {
                _logger?.LogError(error, "Error getting price update data");
                return;
            }

            lock (_lock)
            {
                _priceUpdateData = priceUpdateData;
            }

            OnUpdate?.Invoke(this, EventArgs.Empty);
        }
    }
}
