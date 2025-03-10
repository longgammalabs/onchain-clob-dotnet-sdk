using Microsoft.Extensions.Logging;
using OnchainClob.Common;
using System.Numerics;

namespace OnchainClob.Services.Pyth
{
    public class Pyth(
        string[] priceFeedIds,
        PythHermesApi pythHermesApi,
        BigInteger priceUpdateFeePerFeed,
        TimeSpan updateInterval,
        ILogger<Pyth>? logger = null)
    {
        public event EventHandler<EventArgs>? OnUpdate;

        private readonly PythHermesApi _pythHermesApi = pythHermesApi;
        private readonly TimeSpan _updateInterval = updateInterval;
        private readonly ILogger<Pyth>? _logger = logger;
        private readonly object _startStopLock = new();
        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private bool _isRunning;
        private byte[][]? _priceUpdateData;

        public string[] PriceFeedIds { get; init; } = priceFeedIds;
        public BigInteger PriceUpdateFeePerFeed { get; init; } = priceUpdateFeePerFeed;
        public BigInteger PriceUpdateFee { get; init; } = priceUpdateFeePerFeed * priceFeedIds.Length;
        public long LastPriceUpdateTime { get; private set; }

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
                return _priceUpdateData;
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
            catch (OperationCanceledException)
            {
                // expected
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

            LastPriceUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            lock (_lock)
            {
                _priceUpdateData = priceUpdateData;
            }

            OnUpdate?.Invoke(this, EventArgs.Empty);
        }
    }
}
