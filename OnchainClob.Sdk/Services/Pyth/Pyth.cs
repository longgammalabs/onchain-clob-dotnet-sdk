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
        private long _lastContractUpdateTime;

        public string PythContract { get; init; } = pythContract ?? throw new ArgumentNullException(nameof(pythContract));
        public string[] PriceFeedIds { get; init; } = priceFeedIds ?? throw new ArgumentNullException(nameof(priceFeedIds));
        public BigInteger PriceUpdateFeePerFeed { get; init; } = priceUpdateFeePerFeed;
        public BigInteger PriceUpdateFee { get; init; } = priceUpdateFeePerFeed * priceFeedIds.Length;

        public void Start()
        {
            lock (_startStopLock)
            {
                if (_isRunning)
                    return;

                _cts = new CancellationTokenSource();
                _isRunning = true;

                _ = Task.Run(async () => await DoWorkAsync(_cts.Token));
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
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _lastContractUpdateTime > _priceValidityPeriodSeconds.TotalSeconds)
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
                    await Task.Delay(_updateInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger?.LogInformation("Pyth price updater stopped");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating Pyth prices");
            }

            _logger?.LogInformation("Pyth DoWorkAsync exit");
        }

        private async Task UpdateAsync(CancellationToken ct = default)
        {
            _logger?.LogDebug("Pyth UpdateAsync started");

            var (priceUpdateData, error) = await _pythHermesApi.GetPriceUpdateDataAsync(
                PriceFeedIds,
                ct);

            if (error != null)
            {
                _logger?.LogError(error, "Error getting price update data");
                return;
            }

            _logger?.LogDebug("Update priceUpdateData");

            lock (_lock)
            {
                _priceUpdateData = priceUpdateData;
            }

            OnUpdate?.Invoke(this, EventArgs.Empty);
        }
    }
}
