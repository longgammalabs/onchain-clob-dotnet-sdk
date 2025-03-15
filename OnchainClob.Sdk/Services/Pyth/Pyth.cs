using Microsoft.Extensions.Logging;
using OnchainClob.Common;
using System.Numerics;

namespace OnchainClob.Services.Pyth
{
    public class Pyth(
        string pythContract,
        string[] priceFeedIds,
        PythHermesApi pythHermesApi,
        BigInteger priceUpdateFeePerFeed,
        TimeSpan updateInterval,
        TimeSpan priceValidityPeriodSeconds,
        ILogger<Pyth>? logger = null)
    {
        public event EventHandler<EventArgs>? OnUpdate;

        private readonly PythHermesApi _pythHermesApi = pythHermesApi;
        private readonly TimeSpan _updateInterval = updateInterval;
        private readonly TimeSpan _priceValidityPeriodSeconds = priceValidityPeriodSeconds;
        private readonly ILogger<Pyth>? _logger = logger;
        private readonly object _startStopLock = new();
        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private bool _isRunning;
        private byte[][]? _priceUpdateData;

        public string PythContract { get; init; } = pythContract ?? throw new ArgumentNullException(nameof(pythContract));
        public string[] PriceFeedIds { get; init; } = priceFeedIds ?? throw new ArgumentNullException(nameof(priceFeedIds));
        public BigInteger PriceUpdateFeePerFeed { get; init; } = priceUpdateFeePerFeed;
        public BigInteger PriceUpdateFee { get; init; } = priceUpdateFeePerFeed * priceFeedIds.Length;
        public long LastUpdateTime { get; private set; }
        public long LastContractUpdateTime { get; set; }

        public void Start()
        {
            lock (_startStopLock)
            {
                if (_isRunning)
                    return;

                _cts = new CancellationTokenSource();
                _isRunning = true;
                _ = DoWorkAsync(_cts.Token);
            }
        }

        public void Stop()
        {
            lock (_startStopLock)
            {
                if (!_isRunning)
                    return;

                _cts?.Cancel();
                _cts = null;
                _isRunning = false;
            }
        }

        public byte[][]? GetPriceUpdateData()
        {
            lock (_lock)
            {
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - LastContractUpdateTime > _priceValidityPeriodSeconds.TotalSeconds)
                {
                    return _priceUpdateData;
                }
                else
                {
                    return null;
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
                    await Task.Delay(_updateInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // expected
                _logger?.LogInformation("Pyth price updater stopped");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating Pyth prices");
            }
        }

        private async Task UpdateAsync(CancellationToken cancellationToken = default)
        {
            var (priceUpdateData, error) = await _pythHermesApi.GetPriceUpdateDataAsync(
                PriceFeedIds,
                cancellationToken);

            if (error != null)
            {
                _logger?.LogError(error, "Error getting price update data");
                return;
            }

            LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            lock (_lock)
            {
                _priceUpdateData = priceUpdateData;
            }

            OnUpdate?.Invoke(this, EventArgs.Empty);
        }
    }
}
