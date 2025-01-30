namespace OnchainClob.Common
{
    public class RateLimitControl(int delayMs) : IDisposable
    {
        private readonly SemaphoreSlim _semaphoreSlim = new(1);
        private readonly int _delayMs = delayMs;
        private long _lastTimeStampMs;
        private bool _disposed;

        public async Task WaitAsync(CancellationToken cancellationToken = default)
        {
            var isCompleted = false;

            while (!isCompleted)
            {
                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();

                await _semaphoreSlim.WaitAsync(cancellationToken);

                var timeStampMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                var delayRestMs = _delayMs - (timeStampMs - _lastTimeStampMs);

                if (delayRestMs > 0)
                {
                    _semaphoreSlim.Release();

                    await Task.Delay((int)delayRestMs, cancellationToken);
                }
                else
                {
                    _lastTimeStampMs = timeStampMs;

                    _semaphoreSlim.Release();

                    isCompleted = true;
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
                _semaphoreSlim.Dispose();

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
