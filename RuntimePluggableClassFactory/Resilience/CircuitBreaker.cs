using System;
using System.Threading;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Resilience
{
    /// <summary>
    /// Circuit breaker state
    /// </summary>
    public enum CircuitState
    {
        /// <summary>
        /// Circuit is closed and operating normally
        /// </summary>
        Closed,

        /// <summary>
        /// Circuit is open and failing fast
        /// </summary>
        Open,

        /// <summary>
        /// Circuit is half-open and testing if the operation succeeds
        /// </summary>
        HalfOpen
    }

    /// <summary>
    /// Event arguments for circuit breaker state changes
    /// </summary>
    public class CircuitStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Previous state
        /// </summary>
        public CircuitState PreviousState { get; set; }

        /// <summary>
        /// New state
        /// </summary>
        public CircuitState NewState { get; set; }

        /// <summary>
        /// Reason for the state change
        /// </summary>
        public string? Reason { get; set; }

        /// <summary>
        /// Exception that caused the state change (if any)
        /// </summary>
        public Exception? Exception { get; set; }

        /// <summary>
        /// Timestamp
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Circuit breaker for plugin operations
    /// Prevents repeated failures from causing cascading failures
    /// </summary>
    public class CircuitBreaker : IDisposable
    {
        private readonly object _stateLock = new object();
        private readonly int _failureThreshold;
        private readonly TimeSpan _resetTimeout;
        private readonly TimeSpan _halfOpenTestTimeout;
        
        private CircuitState _state = CircuitState.Closed;
        private int _failureCount = 0;
        private DateTime _lastFailureTime = DateTime.MinValue;
        private DateTime _stateChangeTime = DateTime.UtcNow;
        private bool _disposed = false;

        /// <summary>
        /// Creates a new circuit breaker
        /// </summary>
        /// <param name="failureThreshold">Number of failures before opening the circuit</param>
        /// <param name="resetTimeout">Time to wait before attempting to close the circuit</param>
        /// <param name="halfOpenTestTimeout">Time to wait in half-open state before closing</param>
        public CircuitBreaker(
            int failureThreshold = 5,
            TimeSpan? resetTimeout = null,
            TimeSpan? halfOpenTestTimeout = null)
        {
            if (failureThreshold <= 0)
                throw new ArgumentException("Failure threshold must be positive", nameof(failureThreshold));

            _failureThreshold = failureThreshold;
            _resetTimeout = resetTimeout ?? TimeSpan.FromSeconds(30);
            _halfOpenTestTimeout = halfOpenTestTimeout ?? TimeSpan.FromSeconds(10);
        }

        /// <summary>
        /// Current state of the circuit breaker
        /// </summary>
        public CircuitState State
        {
            get
            {
                lock (_stateLock)
                {
                    // Auto-transition from Open to HalfOpen if reset timeout has passed
                    if (_state == CircuitState.Open && 
                        DateTime.UtcNow - _stateChangeTime > _resetTimeout)
                    {
                        TransitionTo(CircuitState.HalfOpen, "Reset timeout elapsed");
                    }
                    
                    // Auto-transition from HalfOpen to Closed if test timeout has passed without failure
                    if (_state == CircuitState.HalfOpen &&
                        DateTime.UtcNow - _stateChangeTime > _halfOpenTestTimeout)
                    {
                        TransitionTo(CircuitState.Closed, "Half-open test timeout elapsed");
                    }
                    
                    return _state;
                }
            }
        }

        /// <summary>
        /// Number of consecutive failures
        /// </summary>
        public int FailureCount
        {
            get
            {
                lock (_stateLock)
                {
                    return _failureCount;
                }
            }
        }

        /// <summary>
        /// Time of last failure
        /// </summary>
        public DateTime LastFailureTime
        {
            get
            {
                lock (_stateLock)
                {
                    return _lastFailureTime;
                }
            }
        }

        /// <summary>
        /// Event fired when circuit state changes
        /// </summary>
        public event EventHandler<CircuitStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// Executes an action with circuit breaker protection
        /// </summary>
        /// <typeparam name="T">Return type</typeparam>
        /// <param name="action">Action to execute</param>
        /// <param name="fallback">Fallback action if circuit is open</param>
        /// <returns>Result of the action or fallback</returns>
        public T Execute<T>(Func<T> action, Func<T>? fallback = null)
        {
            lock (_stateLock)
            {
                switch (_state)
                {
                    case CircuitState.Closed:
                        try
                        {
                            var result = action();
                            Reset();
                            return result;
                        }
                        catch (Exception ex)
                        {
                            RecordFailure(ex);
                            throw;
                        }

                    case CircuitState.Open:
                        if (fallback != null)
                            return fallback();
                        throw new CircuitOpenException("Circuit is open");

                    case CircuitState.HalfOpen:
                        try
                        {
                            var result = action();
                            Reset();
                            return result;
                        }
                        catch (Exception ex)
                        {
                            // In half-open state, any failure reopens the circuit
                            TransitionTo(CircuitState.Open, "Half-open test failed", ex);
                            throw;
                        }

                    default:
                        throw new InvalidOperationException("Unknown circuit state");
                }
            }
        }

        /// <summary>
        /// Executes an async action with circuit breaker protection
        /// </summary>
        /// <typeparam name="T">Return type</typeparam>
        /// <param name="action">Async action to execute</param>
        /// <param name="fallback">Fallback async action if circuit is open</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result of the action or fallback</returns>
        public async Task<T> ExecuteAsync<T>(
            Func<Task<T>> action,
            Func<Task<T>>? fallback = null,
            CancellationToken cancellationToken = default)
        {
            // Check state first (without lock to avoid deadlocks)
            var currentState = State;
            
            switch (currentState)
            {
                case CircuitState.Closed:
                    try
                    {
                        var result = await action().ConfigureAwait(false);
                        Reset();
                        return result;
                    }
                    catch (Exception ex)
                    {
                        RecordFailure(ex);
                        throw;
                    }

                case CircuitState.Open:
                    if (fallback != null)
                        return await fallback().ConfigureAwait(false);
                    throw new CircuitOpenException("Circuit is open");

                case CircuitState.HalfOpen:
                    try
                    {
                        var result = await action().ConfigureAwait(false);
                        Reset();
                        return result;
                    }
                    catch (Exception ex)
                    {
                        // In half-open state, any failure reopens the circuit
                        TransitionTo(CircuitState.Open, "Half-open test failed", ex);
                        throw;
                    }

                default:
                    throw new InvalidOperationException("Unknown circuit state");
            }
        }

        /// <summary>
        /// Executes an action with circuit breaker protection
        /// </summary>
        /// <param name="action">Action to execute</param>
        /// <param name="fallback">Fallback action if circuit is open</param>
        public void Execute(Action action, Action? fallback = null)
        {
            Execute(() => { action(); return true; }, 
                   fallback != null ? () => { fallback(); return true; } : null);
        }

        /// <summary>
        /// Executes an async action with circuit breaker protection
        /// </summary>
        /// <param name="action">Async action to execute</param>
        /// <param name="fallback">Fallback async action if circuit is open</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task ExecuteAsync(
            Func<Task> action,
            Func<Task>? fallback = null,
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(() => action().ContinueWith(_ => true),
                             fallback != null ? () => fallback().ContinueWith(_ => true) : null,
                             cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Records a failure
        /// </summary>
        /// <param name="exception">Exception that caused the failure</param>
        private void RecordFailure(Exception? exception)
        {
            lock (_stateLock)
            {
                _failureCount++;
                _lastFailureTime = DateTime.UtcNow;

                if (_failureCount >= _failureThreshold)
                {
                    TransitionTo(CircuitState.Open, "Failure threshold exceeded", exception);
                }
            }
        }

        /// <summary>
        /// Resets the circuit breaker
        /// </summary>
        private void Reset()
        {
            lock (_stateLock)
            {
                _failureCount = 0;
                if (_state == CircuitState.HalfOpen)
                {
                    TransitionTo(CircuitState.Closed, "Half-open test succeeded");
                }
            }
        }

        /// <summary>
        /// Transitions to a new state
        /// </summary>
        /// <param name="newState">New state</param>
        /// <param name="reason">Reason for the transition</param>
        /// <param name="exception">Exception that caused the transition (if any)</param>
        private void TransitionTo(CircuitState newState, string? reason, Exception? exception = null)
        {
            lock (_stateLock)
            {
                if (_state == newState)
                    return;

                var previousState = _state;
                _state = newState;
                _stateChangeTime = DateTime.UtcNow;

                // Reset failure count when transitioning to closed or half-open
                if (newState == CircuitState.Closed || newState == CircuitState.HalfOpen)
                {
                    _failureCount = 0;
                }

                // Fire event
                StateChanged?.Invoke(this, new CircuitStateChangedEventArgs
                {
                    PreviousState = previousState,
                    NewState = newState,
                    Reason = reason,
                    Exception = exception,
                    Timestamp = _stateChangeTime
                });
            }
        }

        /// <summary>
        /// Manually resets the circuit breaker
        /// </summary>
        public void ForceReset()
        {
            lock (_stateLock)
            {
                TransitionTo(CircuitState.Closed, "Manual reset");
            }
        }

        /// <summary>
        /// Manually opens the circuit breaker
        /// </summary>
        public void ForceOpen()
        {
            lock (_stateLock)
            {
                TransitionTo(CircuitState.Open, "Manual open");
            }
        }

        /// <summary>
        /// Gets a value indicating whether the circuit allows execution
        /// </summary>
        public bool AllowsExecution => State != CircuitState.Open;

        /// <summary>
        /// Gets the time until the circuit will attempt to reset
        /// </summary>
        public TimeSpan TimeUntilReset
        {
            get
            {
                lock (_stateLock)
                {
                    if (_state != CircuitState.Open)
                        return TimeSpan.Zero;

                    var elapsed = DateTime.UtcNow - _stateChangeTime;
                    var remaining = _resetTimeout - elapsed;
                    return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Clean up resources
                    StateChanged = null;
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
    /// Exception thrown when circuit is open
    /// </summary>
    public class CircuitOpenException : Exception
    {
        public CircuitOpenException(string message) : base(message)
        {
        }

        public CircuitOpenException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Circuit breaker options
    /// </summary>
    public class CircuitBreakerOptions
    {
        /// <summary>
        /// Number of failures before opening the circuit
        /// </summary>
        public int FailureThreshold { get; set; } = 5;

        /// <summary>
        /// Time to wait before attempting to close the circuit
        /// </summary>
        public TimeSpan ResetTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Time to wait in half-open state before closing
        /// </summary>
        public TimeSpan HalfOpenTestTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Creates default options
        /// </summary>
        public static CircuitBreakerOptions Default { get; } = new CircuitBreakerOptions();

        /// <summary>
        /// Creates aggressive options (fails fast)
        /// </summary>
        public static CircuitBreakerOptions Aggressive { get; } = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            ResetTimeout = TimeSpan.FromSeconds(60),
            HalfOpenTestTimeout = TimeSpan.FromSeconds(5)
        };

        /// <summary>
        /// Creates lenient options (tolerates more failures)
        /// </summary>
        public static CircuitBreakerOptions Lenient { get; } = new CircuitBreakerOptions
        {
            FailureThreshold = 10,
            ResetTimeout = TimeSpan.FromSeconds(10),
            HalfOpenTestTimeout = TimeSpan.FromSeconds(3)
        };
    }
}
