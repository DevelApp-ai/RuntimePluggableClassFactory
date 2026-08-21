using System;
using System.Threading;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Resilience
{
    /// <summary>
    /// Retry policy for plugin operations
    /// Provides configurable retry behavior for transient failures
    /// </summary>
    public class RetryPolicy : IDisposable
    {
        private readonly int _maxRetries;
        private readonly TimeSpan _initialDelay;
        private readonly TimeSpan _maxDelay;
        private readonly double _backoffMultiplier;
        private readonly Func<Exception, bool> _shouldRetry;
        private readonly Func<Exception, int, TimeSpan> _delayCalculator;
        private bool _disposed = false;

        /// <summary>
        /// Creates a new retry policy with default settings
        /// </summary>
        public RetryPolicy()
            : this(maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(100))
        {
        }

        /// <summary>
        /// Creates a new retry policy with custom settings
        /// </summary>
        /// <param name="maxRetries">Maximum number of retry attempts</param>
        /// <param name="initialDelay">Initial delay between retries</param>
        /// <param name="maxDelay">Maximum delay between retries</param>
        /// <param name="backoffMultiplier">Multiplier for exponential backoff</param>
        /// <param name="shouldRetry">Function to determine if an exception should be retried</param>
        public RetryPolicy(
            int maxRetries = 3,
            TimeSpan? initialDelay = null,
            TimeSpan? maxDelay = null,
            double backoffMultiplier = 2.0,
            Func<Exception, bool>? shouldRetry = null)
        {
            if (maxRetries < 0)
                throw new ArgumentException("Max retries must be non-negative", nameof(maxRetries));
            if (backoffMultiplier <= 0)
                throw new ArgumentException("Backoff multiplier must be positive", nameof(backoffMultiplier));

            _maxRetries = maxRetries;
            _initialDelay = initialDelay ?? TimeSpan.FromMilliseconds(100);
            _maxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
            _backoffMultiplier = backoffMultiplier;
            _shouldRetry = shouldRetry ?? (ex => true);

            // Use exponential backoff by default
            _delayCalculator = (ex, attempt) =>
            {
                var delay = TimeSpan.FromTicks((long)(_initialDelay.Ticks * Math.Pow(_backoffMultiplier, attempt - 1)));
                return delay > _maxDelay ? _maxDelay : delay;
            };
        }

        /// <summary>
        /// Maximum number of retry attempts
        /// </summary>
        public int MaxRetries => _maxRetries;

        /// <summary>
        /// Executes an action with retry policy
        /// </summary>
        /// <typeparam name="T">Return type</typeparam>
        /// <param name="action">Action to execute</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result of the action</returns>
        public async Task<T> ExecuteAsync<T>(
            Func<Task<T>> action,
            CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RetryPolicy));

            Exception? lastException = null;
            int attempt = 0;

            while (true)
            {
                attempt++;

                try
                {
                    return await action().ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt <= _maxRetries && _shouldRetry(ex))
                {
                    lastException = ex;
                    var delay = _delayCalculator(ex, attempt);

                    try
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Ignore delay errors and retry immediately
                    }
                }
            }
        }

        /// <summary>
        /// Executes an action with retry policy
        /// </summary>
        /// <param name="action">Action to execute</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task ExecuteAsync(
            Func<Task> action,
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(() => action().ContinueWith(_ => true), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a synchronous action with retry policy
        /// </summary>
        /// <typeparam name="T">Return type</typeparam>
        /// <param name="action">Action to execute</param>
        /// <returns>Result of the action</returns>
        public T Execute<T>(Func<T> action)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RetryPolicy));

            Exception? lastException = null;
            int attempt = 0;

            while (true)
            {
                attempt++;

                try
                {
                    return action();
                }
                catch (Exception ex) when (attempt <= _maxRetries && _shouldRetry(ex))
                {
                    lastException = ex;
                    var delay = _delayCalculator(ex, attempt);

                    if (delay > TimeSpan.Zero)
                    {
                        System.Threading.Thread.Sleep(delay);
                    }
                }
            }
        }

        /// <summary>
        /// Executes a synchronous action with retry policy
        /// </summary>
        /// <param name="action">Action to execute</param>
        public void Execute(Action action)
        {
            Execute(() => { action(); return true; });
        }

        /// <summary>
        /// Creates a retry policy with exponential backoff
        /// </summary>
        /// <param name="maxRetries">Maximum number of retry attempts</param>
        /// <param name="initialDelay">Initial delay between retries</param>
        /// <returns>Retry policy</returns>
        public static RetryPolicy ExponentialBackoff(int maxRetries = 3, TimeSpan? initialDelay = null)
        {
            return new RetryPolicy(maxRetries, initialDelay, null, 2.0);
        }

        /// <summary>
        /// Creates a retry policy with linear backoff
        /// </summary>
        /// <param name="maxRetries">Maximum number of retry attempts</param>
        /// <param name="delay">Fixed delay between retries</param>
        /// <returns>Retry policy</returns>
        public static RetryPolicy LinearBackoff(int maxRetries = 3, TimeSpan? delay = null)
        {
            var fixedDelay = delay ?? TimeSpan.FromMilliseconds(100);
            return new RetryPolicy(maxRetries, fixedDelay, fixedDelay, 1.0);
        }

        /// <summary>
        /// Creates a retry policy that retries only on specific exception types
        /// </summary>
        /// <typeparam name="TException">Exception type to retry on</typeparam>
        /// <param name="maxRetries">Maximum number of retry attempts</param>
        /// <param name="initialDelay">Initial delay between retries</param>
        /// <returns>Retry policy</returns>
        public static RetryPolicy OnException<TException>(int maxRetries = 3, TimeSpan? initialDelay = null) 
            where TException : Exception
        {
            return new RetryPolicy(maxRetries, initialDelay, null, 2.0, ex => ex is TException);
        }

        /// <summary>
        /// Creates a retry policy that never retries
        /// </summary>
        /// <returns>Retry policy</returns>
        public static RetryPolicy NoRetry()
        {
            return new RetryPolicy(0);
        }

        /// <summary>
        /// Creates a retry policy that retries forever
        /// </summary>
        /// <param name="initialDelay">Initial delay between retries</param>
        /// <returns>Retry policy</returns>
        public static RetryPolicy Forever(TimeSpan? initialDelay = null)
        {
            return new RetryPolicy(int.MaxValue, initialDelay);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Clean up resources
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Retry policy options
    /// </summary>
    public class RetryPolicyOptions
    {
        /// <summary>
        /// Maximum number of retry attempts
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Initial delay between retries
        /// </summary>
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Maximum delay between retries
        /// </summary>
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Backoff multiplier for exponential backoff
        /// </summary>
        public double BackoffMultiplier { get; set; } = 2.0;

        /// <summary>
        /// Creates default options
        /// </summary>
        public static RetryPolicyOptions Default { get; } = new RetryPolicyOptions();

        /// <summary>
        /// Creates aggressive retry options
        /// </summary>
        public static RetryPolicyOptions Aggressive { get; } = new RetryPolicyOptions
        {
            MaxRetries = 5,
            InitialDelay = TimeSpan.FromMilliseconds(50),
            MaxDelay = TimeSpan.FromSeconds(5),
            BackoffMultiplier = 1.5
        };

        /// <summary>
        /// Creates lenient retry options
        /// </summary>
        public static RetryPolicyOptions Lenient { get; } = new RetryPolicyOptions
        {
            MaxRetries = 1,
            InitialDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(10),
            BackoffMultiplier = 2.0
        };
    }
}
