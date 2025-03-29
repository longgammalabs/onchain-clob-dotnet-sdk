namespace OnchainClob.Common
{
    public class TwoStepLock() : IDisposable
    {
        private readonly SemaphoreSlim _stepOne = new(1, 1);
        private readonly SemaphoreSlim _stepTwo = new(1, 1);
        private bool _isDisposed;

        public async Task<bool> ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
        {
            var isPhaseOneEntered = false;
            var isPhaseOneReleased = false;
            var isPhaseTwoEntered = false;

            try
            {
                if (!await _stepOne.WaitAsync(0, CancellationToken.None))
                    return false;

                isPhaseOneEntered = true;

                await _stepTwo.WaitAsync(ct);

                isPhaseTwoEntered = true;

                _stepOne.Release();

                isPhaseOneReleased = true;

                await action(ct);

                return true;
            }
            catch
            {
                throw;
            }
            finally
            {
                if (isPhaseOneEntered && !isPhaseOneReleased)
                    _stepOne.Release();

                if (isPhaseTwoEntered)
                    _stepTwo.Release();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    _stepOne.Release();
                    _stepTwo.Release();
                }

                _isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
