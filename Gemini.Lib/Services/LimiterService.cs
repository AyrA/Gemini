using AyrA.AutoDI;
using System.Diagnostics.CodeAnalysis;

namespace Gemini.Lib.Services
{
    /// <summary>
    /// Semaphore based limiter service with dynamically adjustable parallelism count
    /// </summary>
    [AutoDIRegister(AutoDIType.Transient)]
    public class LimiterService : IDisposable
    {
        /// <summary>
        /// Result of taking a handle from a limit
        /// </summary>
        /// <remarks>
        /// To release the handle, dispose this instance
        /// </remarks>
        public sealed class LimiterHandle : IDisposable
        {
            private LimiterService? _instance;

            internal LimiterHandle(LimiterService? instance)
            {
                _instance = instance;
            }

            /// <summary>
            /// Backup method in case the object was not properly disposed of
            /// </summary>
            ~LimiterHandle()
            {
                Dispose();
            }

            /// <summary>
            /// Releases the handle if it is still being held
            /// </summary>
            public void Dispose()
            {
                lock (this)
                {
                    try
                    {
                        _instance?.Release();
                    }
                    catch
                    {
                        //NOOP. May happen if instance is already disposed
                    }
                    _instance = null;
                }
                GC.SuppressFinalize(this);
            }
        }


        private readonly SemaphoreSlim _internalLock = new(1);
        private SemaphoreSlim? _semaphore;

        /// <summary>
        /// The initial limit of the limiter.
        /// This can only be set once using <see cref="Initialize(int)"/>
        /// </summary>
        public int InitialLimit { get; private set; }
        /// <summary>
        /// The current limit of the limiter.
        /// This can be adjusted between zero and <see cref="InitialLimit"/> (both inclusive)
        /// during runtime using the appropriate methods
        /// </summary>
        public int CurrentLimit { get; private set; }
        /// <summary>
        /// The remaining number of limits this instance will grant
        /// </summary>
        /// <remarks>
        /// This value is generally not thread safe
        /// </remarks>
        public int Remaining
        {
            get
            {
                EnsureReady(); return _semaphore.CurrentCount;
            }
        }
        /// <summary>
        /// The number of limits that are currently taken
        /// </summary>
        /// <remarks>This value is generally not thread safe</remarks>
        public int Taken
        {
            get
            {
                EnsureReady(); return CurrentLimit - _semaphore.CurrentCount;
            }
        }

        /// <summary>
        /// DI
        /// </summary>
        public LimiterService() { }

        /// <summary>
        /// Initializes <see cref="InitialLimit"/>
        /// </summary>
        /// <param name="limit">Initial limit</param>
        public void Initialize(int limit)
        {
            if (limit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limit));
            }
            if (_semaphore != null)
            {
                throw new InvalidOperationException("Upper limit has already been set");
            }
            InitialLimit = limit;
            _semaphore = new SemaphoreSlim(limit);
        }

        /// <summary>
        /// Try to take a handle, with limited wait time
        /// </summary>
        /// <param name="maxWait">Wait time</param>
        /// <returns>Limit handle, which must be disposed when the handle is to be freed</returns>
        public async Task<LimiterHandle?> Take(TimeSpan maxWait)
        {
            EnsureReady();
            await _internalLock.WaitAsync();
            try
            {
                if (await _semaphore.WaitAsync(maxWait))
                {
                    return new LimiterHandle(maxWait == TimeSpan.Zero ? null : this);
                }
            }
            finally
            {
                _internalLock.Release();
            }
            return null;
        }

        /// <summary>
        /// Try to take a handle, with limited wait time
        /// </summary>
        /// <param name="maxWait">Wait time</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Limit handle, which must be disposed when the handle is to be freed</returns>
        public async Task<LimiterHandle?> Take(TimeSpan maxWait, CancellationToken ct)
        {
            EnsureReady();
            await _internalLock.WaitAsync(ct);
            try
            {
                if (await _semaphore.WaitAsync(maxWait, ct))
                {
                    return new LimiterHandle(maxWait == TimeSpan.Zero ? null : this);
                }
            }
            finally
            {
                _internalLock.Release();
            }
            return null;
        }

        /// <summary>
        /// Takes a handle, waiting indefinitely if necessary
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Limit handle, which must be disposed when the handle is to be freed</returns>
        public async Task<LimiterHandle> Take(CancellationToken ct)
        {
            EnsureReady();
            await _internalLock.WaitAsync(ct);
            try
            {
                await _semaphore.WaitAsync(ct);
                return new LimiterHandle(this);
            }
            finally
            {
                _internalLock.Release();
            }
        }

        /// <summary>
        /// Takes a handle, waiting indefinitely if necessary
        /// </summary>
        /// <returns>Limit handle, which must be disposed when the handle is to be freed</returns>
        public async Task<LimiterHandle> Take()
        {
            EnsureReady();
            await _internalLock.WaitAsync();
            try
            {
                await _semaphore.WaitAsync();
                return new LimiterHandle(this);
            }
            finally
            {
                _internalLock.Release();
            }
        }

        /// <summary>
        /// Releases a handle. Not to be called externally
        /// </summary>
        internal void Release()
        {
            EnsureReady();
            _semaphore.Release();
        }

        /// <summary>
        /// Sets <see cref="CurrentLimit"/> to the given number
        /// </summary>
        /// <param name="newLimit">New limit</param>
        public async Task LimitSet(int newLimit)
        {
            EnsureReady();
            if (newLimit < 0 || newLimit > InitialLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(newLimit));
            }
            await _internalLock.WaitAsync();
            try
            {
                if (newLimit > CurrentLimit)
                {
                    _semaphore.Release(newLimit - CurrentLimit);
                    CurrentLimit = newLimit;
                }
                else if (newLimit < CurrentLimit)
                {
                    while (newLimit < CurrentLimit)
                    {
                        await _semaphore.WaitAsync();
                        --CurrentLimit;
                    }
                }
            }
            finally
            {
                _internalLock.Release();
            }
        }

        /// <summary>
        /// Reduce the limit by the given number
        /// </summary>
        /// <param name="count">Count</param>
        /// <remarks>If <paramref name="count"/> is negative, the limit will increase</remarks>
        public async Task LimitReduceBy(int count)
        {
            EnsureReady();
            if (count < 0)
            {
                await LimitIncreaseBy(-count);
            }
            if (count == 0)
            {
                return;
            }
            await _internalLock.WaitAsync();
            //Nobody can take the semaphore now. This means the count properties are temporarily thread safe
            try
            {
                if (count > CurrentLimit)
                {
                    throw new ArgumentOutOfRangeException(nameof(count), "Count exceeds current limit");
                }
                for (var i = 0; i < count; i++)
                {
                    await _semaphore.WaitAsync();
                    --CurrentLimit;
                }
            }
            finally
            {
                _internalLock.Release();
            }
        }

        /// <summary>
        /// Increases the limit by the given number
        /// </summary>
        /// <param name="count">Count</param>
        /// <remarks>The limit cannot be increased beyond <see cref="CurrentLimit"/></remarks>
        /// <remarks>If <paramref name="count"/> is negative, the limit will decrease</remarks>
        public async Task LimitIncreaseBy(int count)
        {
            EnsureReady();
            if (count < 0)
            {
                await LimitReduceBy(-count);
            }
            if (count == 0)
            {
                return;
            }
            await _internalLock.WaitAsync();
            try
            {
                if (count + CurrentLimit > InitialLimit)
                {
                    throw new ArgumentOutOfRangeException(nameof(count), "Semaphore would exceed initial upper limit after operation");
                }
                _semaphore.Release(count);
                CurrentLimit += count;
            }
            finally
            {
                _internalLock.Release();
            }
        }

        /// <summary>
        /// Ensures the semaphore has been initialized
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [MemberNotNull(nameof(_semaphore))]
        private void EnsureReady()
        {
            if (_semaphore == null)
            {
                throw new InvalidOperationException("Upper limit has not been set");
            }
        }

        /// <summary>
        /// Disposes this instance
        /// </summary>
        /// <remarks>
        /// Disposing the instance while there are pending requests
        /// may throw an exception in all pending requests
        /// </remarks>
        public void Dispose()
        {
            _internalLock?.Dispose();
            var s = _semaphore;
            _semaphore = null;
            s?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
