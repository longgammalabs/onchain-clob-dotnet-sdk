using Incendium;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Revelium.Evm.Rpc;
using Revelium.Evm.Rpc.Parameters;
using System.Numerics;

namespace OnchainClob.Services
{
    public class NonceManagerOptions
    {
        public int UpdateIntervalMs { get; init; } = 10 * 1000; // 10 seconds
        public int NonceExpirationMs { get; init; } = 3 * 60 * 1000; // 3 minutes
        public int OfflineNonceExpirationMs { get; init; } = 60 * 1000; // 1 minute
        public string[] Addresses { get; init; } = default!;
    }

    public class NonceManager : IHostedService
    {
        private class NonceEntry
        {
            public DateTimeOffset TimeStamp { get; set; }
            public BigInteger Nonce { get; set; }
        }

        public class NonceLock : IDisposable
        {
            private readonly NonceManager _nonceManager;
            private readonly string _address;
            private readonly SemaphoreSlim _sync;
            private bool _isDisposed;

            private NonceLock(NonceManager nonceManager, string address, SemaphoreSlim sync)
            {
                _nonceManager = nonceManager;
                _address = address;
                _sync = sync;
            }

            public static async Task<NonceLock> LockAsync(
                NonceManager nonceManager,
                string address,
                SemaphoreSlim sync,
                CancellationToken ct = default)
            {
                await sync.WaitAsync(ct);
                return new NonceLock(nonceManager, address, sync);
            }

            public Task<Result<BigInteger>> GetNonceAsync(CancellationToken ct = default)
            {
                return _nonceManager.GetNonceAsync(_address, ct);
            }

            public void Reset(BigInteger nonce)
            {
                _nonceManager.Reset(_address, nonce);
            }

            protected virtual void Dispose(bool disposing)
            {
                if (_isDisposed)
                    return;

                if (disposing)
                    _sync.Release();

                _isDisposed = true;
            }

            public void Dispose()
            {
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }

        private readonly NonceManagerOptions _options;
        private readonly RpcClient _rpc;
        private readonly ILogger<NonceManager>? _logger;
        private readonly object _startStopLock = new();
        private CancellationTokenSource? _cts;
        private bool _isRunning;

        private readonly Dictionary<string, object> _networkNonceSyncs;
        private readonly Dictionary<string, NonceEntry?> _networkNonces;

        private readonly Dictionary<string, SemaphoreSlim> _offlineNonceSyncs;
        private readonly Dictionary<string, NonceEntry?> _offlineNonces;

        public NonceManager(
            NonceManagerOptions options,
            RpcClient rpc,
            ILogger<NonceManager>? logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
            _logger = logger;

            _networkNonceSyncs = _options.Addresses.ToDictionary(
                a => a.ToLowerInvariant(),
                a => new object());

            _networkNonces = _options.Addresses.ToDictionary(
                a => a.ToLowerInvariant(),
                a => default(NonceEntry));

            _offlineNonceSyncs = _options.Addresses.ToDictionary(
                a => a.ToLowerInvariant(),
                a => new SemaphoreSlim(1));

            _offlineNonces = _options.Addresses.ToDictionary(
                a => a.ToLowerInvariant(),
                a => default(NonceEntry));
        }

        public Task StartAsync(CancellationToken ct)
        {
            lock (_startStopLock)
            {
                if (_isRunning)
                    return Task.CompletedTask;

                _cts = new CancellationTokenSource();
                _isRunning = true;

                _ = Task.Run(async () => await DoWorkAsync(_cts.Token), _cts.Token);
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

        public Task<NonceLock> LockAsync(
            string address,
            CancellationToken ct = default)
        {
            return NonceLock.LockAsync(this, address, _offlineNonceSyncs[address], ct);
        }

        private (BigInteger? nonce, DateTimeOffset? timeStamp) GetNetworkNonce(string address)
        {
            lock (_networkNonceSyncs[address])
            {
                return (_networkNonces[address]?.Nonce, _networkNonces[address]?.TimeStamp);
            }
        }

        private async Task<Result<BigInteger>> GetNonceAsync(string address, CancellationToken ct = default)
        {
            var (networkNonce, networkTimeStamp) = GetNetworkNonce(address);

            if (networkNonce == null)
            {
                await UpdateNonceAsync(address, ct);

                (networkNonce, networkTimeStamp) = GetNetworkNonce(address);

                if (networkNonce == null)
                    return new Error("Failed to get network nonce");
            }

            var currentNonce = networkNonce.Value;

            if (_offlineNonces.TryGetValue(address, out var offlineNonce) && offlineNonce != null)
            {
                if (offlineNonce.TimeStamp - networkTimeStamp!.Value >=
                    TimeSpan.FromMilliseconds(_options.OfflineNonceExpirationMs))
                {
                    _logger?.LogWarning("Offline nonce for {Address} has expired. Using network nonce.", address);
                }

                if (offlineNonce.Nonce > networkNonce.Value &&
                    (offlineNonce.TimeStamp - networkTimeStamp!.Value <
                    TimeSpan.FromMilliseconds(_options.OfflineNonceExpirationMs)))
                {
                    currentNonce = offlineNonce.Nonce;
                }
            }

            _offlineNonces[address] = new NonceEntry
            {
                Nonce = currentNonce + 1,
                TimeStamp = DateTimeOffset.UtcNow
            };

            return currentNonce;
        }

        private void Reset(string address, BigInteger nonce)
        {
            if (!_offlineNonces.TryGetValue(address.ToLowerInvariant(), out var offlineNonce) || offlineNonce == null)
                return;

            offlineNonce.Nonce = nonce;

            _logger?.LogDebug("Reset {address} nonce to {nonce}", address, nonce.ToString());
        }

        public async Task UpdateNonceAsync(string address, CancellationToken ct = default)
        {
            try
            {
                var (transactionCount, error) = await _rpc
                    .GetTransactionCountAsync(
                        address,
                        BlockNumber.Latest,
                        ct)
                    .ConfigureAwait(false);

                if (error != null)
                {
                    _logger?.LogError(
                        "Error updating nonce for {Address}. Error: {@Error}",
                        address,
                        error);

                    return;
                }

                var timeStamp = DateTimeOffset.UtcNow;

                TrySetNetworkNonce(address.ToLowerInvariant(), transactionCount, timeStamp);

                _logger?.LogInformation(
                    "Network nonce for {Address} is {Nonce}",
                    address,
                    transactionCount.ToString());
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "Error updating nonce");
            }
        }

        private bool TrySetNetworkNonce(string address, BigInteger nonce, DateTimeOffset timeStamp)
        {
            lock (_networkNonceSyncs[address])
            {
                var previousEntry = _networkNonces[address];

                if (previousEntry != null && previousEntry.Nonce > nonce)
                {
                    _logger?.LogWarning("Nonce for {Address} is lower than the previous network nonce. " +
                        "Previous: {PreviousNonce}. " +
                        "Current: {CurrentNonce}",
                        address,
                        previousEntry.Nonce,
                        nonce);
                }

                if (previousEntry == null ||
                    previousEntry.Nonce != nonce ||
                    timeStamp - previousEntry.TimeStamp >= TimeSpan.FromMilliseconds(_options.NonceExpirationMs))
                {
                    _networkNonces[address] = new NonceEntry
                    {
                        Nonce = nonce,
                        TimeStamp = timeStamp
                    };

                    return true;
                }

                return false;
            }
        }

        private async Task DoWorkAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (_options.Addresses != null)
                    {
                        foreach (var address in _options.Addresses)
                            await UpdateNonceAsync(address, ct);
                    }

                    await Task.Delay(_options.UpdateIntervalMs, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // expected
                _logger?.LogInformation("NonceManager stopped");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating nonce");
            }
        }
    }
}
