using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.RuntimePluggableClassFactory.SemanticVersioning;
using DevelApp.Utility.Model;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Resilience
{
    /// <summary>
    /// Resilient plugin class factory with circuit breaker and retry policies
    /// Provides fault-tolerant plugin loading and execution
    /// </summary>
    /// <typeparam name="T">Plugin interface type</typeparam>
    public class ResilientPluginClassFactory<T> : PluginClassFactory<T>, IDisposable where T : IPluginClass
    {
        private readonly CircuitBreaker _circuitBreaker;
        private readonly RetryPolicy _retryPolicy;
        private readonly object _pluginLock = new object();
        private bool _disposed = false;

        /// <summary>
        /// Creates a new resilient plugin class factory
        /// </summary>
        /// <param name="pluginLoader">Plugin loader</param>
        /// <param name="retainOldVersions">Number of old versions to retain</param>
        /// <param name="circuitBreakerOptions">Circuit breaker options</param>
        /// <param name="retryPolicyOptions">Retry policy options</param>
        public ResilientPluginClassFactory(
            IPluginLoader<T> pluginLoader,
            int retainOldVersions = 1,
            CircuitBreakerOptions? circuitBreakerOptions = null,
            RetryPolicyOptions? retryPolicyOptions = null)
            : base(pluginLoader, retainOldVersions)
        {
            _circuitBreaker = new CircuitBreaker(
                circuitBreakerOptions?.FailureThreshold ?? 5,
                circuitBreakerOptions?.ResetTimeout ?? TimeSpan.FromSeconds(30),
                circuitBreakerOptions?.HalfOpenTestTimeout ?? TimeSpan.FromSeconds(10));

            _retryPolicy = new RetryPolicy(
                retryPolicyOptions?.MaxRetries ?? 3,
                retryPolicyOptions?.InitialDelay ?? TimeSpan.FromMilliseconds(100),
                retryPolicyOptions?.MaxDelay ?? TimeSpan.FromSeconds(30),
                retryPolicyOptions?.BackoffMultiplier ?? 2.0);

            // Subscribe to circuit breaker state changes
            _circuitBreaker.StateChanged += OnCircuitStateChanged;
        }

        /// <summary>
        /// Circuit breaker for plugin operations
        /// </summary>
        public CircuitBreaker CircuitBreaker => _circuitBreaker;

        /// <summary>
        /// Retry policy for plugin operations
        /// </summary>
        public RetryPolicy RetryPolicy => _retryPolicy;

        /// <summary>
        /// Whether the circuit is currently open
        /// </summary>
        public bool IsCircuitOpen => _circuitBreaker.State == CircuitState.Open;

        /// <summary>
        /// Gets a plugin instance with circuit breaker and retry protection
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <returns>Plugin instance or null</returns>
        public new T? GetInstance(NamespaceString moduleName, IdentifierString name)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResilientPluginClassFactory<T>));

            return _circuitBreaker.Execute(() => base.GetInstance(moduleName, name));
        }

        /// <summary>
        /// Gets a plugin instance with circuit breaker and retry protection asynchronously
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <returns>Plugin instance or null</returns>
        public async Task<T?> GetInstanceAsync(NamespaceString moduleName, IdentifierString name)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResilientPluginClassFactory<T>));

            return await _circuitBreaker.ExecuteAsync(() => 
                _retryPolicy.ExecuteAsync(() => Task.FromResult(base.GetInstance(moduleName, name))));
        }

        /// <summary>
        /// Gets a plugin instance for a specific version with circuit breaker and retry protection
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <param name="version">Version</param>
        /// <returns>Plugin instance or null</returns>
        public new T? GetInstance(NamespaceString moduleName, IdentifierString name, SemanticVersionNumber version)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResilientPluginClassFactory<T>));

            return _circuitBreaker.Execute(() => base.GetInstance(moduleName, name, version));
        }

        /// <summary>
        /// Gets a plugin instance for a specific version with circuit breaker and retry protection asynchronously
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <param name="version">Version</param>
        /// <returns>Plugin instance or null</returns>
        public async Task<T?> GetInstanceAsync(NamespaceString moduleName, IdentifierString name, SemanticVersionNumber version)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResilientPluginClassFactory<T>));

            return await _circuitBreaker.ExecuteAsync(() => 
                _retryPolicy.ExecuteAsync(() => Task.FromResult(base.GetInstance(moduleName, name, version))));
        }

        /// <summary>
        /// Tries to get a plugin instance with circuit breaker and retry protection
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <param name="instance">Output parameter for the plugin instance</param>
        /// <returns>True if instance was found and created successfully</returns>
        public new bool TryGetInstance(NamespaceString moduleName, IdentifierString name, out T? instance)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResilientPluginClassFactory<T>));

            try
            {
                instance = GetInstance(moduleName, name);
                return instance != null;
            }
            catch
            {
                instance = default;
                return false;
            }
        }

        /// <summary>
        /// Tries to get a plugin instance with circuit breaker and retry protection asynchronously
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <param name="instance">Output parameter for the plugin instance</param>
        /// <returns>True if instance was found and created successfully</returns>
        public async Task<bool> TryGetInstanceAsync(NamespaceString moduleName, IdentifierString name, out T? instance)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResilientPluginClassFactory<T>));

            try
            {
                instance = await GetInstanceAsync(moduleName, name);
                return instance != null;
            }
            catch
            {
                instance = default;
                return false;
            }
        }

        /// <summary>
        /// Refreshes plugins with circuit breaker and retry protection
        /// </summary>
        /// <returns>Refresh result</returns>
        public new async Task<(bool Success, int Count)> RefreshPluginsAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResilientPluginClassFactory<T>));

            return await _circuitBreaker.ExecuteAsync(() => 
                _retryPolicy.ExecuteAsync(base.RefreshPluginsAsync));
        }

        /// <summary>
        /// Gets plugins by version range with circuit breaker and retry protection
        /// </summary>
        /// <param name="versionRange">Version range</param>
        /// <returns>Plugins matching the version range</returns>
        public async Task<IEnumerable<(NamespaceString moduleName, IdentifierString pluginName, SemanticVersionNumber version, string? Description, Type Type)>> GetPluginsByVersionRangeAsync(VersionRange versionRange)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResilientPluginClassFactory<T>));

            return await _circuitBreaker.ExecuteAsync(() => 
                _retryPolicy.ExecuteAsync(() => base.GetPluginsByVersionRangeAsync(versionRange)));
        }

        /// <summary>
        /// Gets plugins by module with circuit breaker and retry protection
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <returns>Plugins from the specified module</returns>
        public async Task<IEnumerable<(NamespaceString moduleName, IdentifierString pluginName, SemanticVersionNumber version, string? Description, Type Type)>> GetPluginsByModuleAsync(NamespaceString moduleName)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ResilientPluginClassFactory<T>));

            return await _circuitBreaker.ExecuteAsync(() => 
                _retryPolicy.ExecuteAsync(() => base.GetPluginsByModuleAsync(moduleName)));
        }

        /// <summary>
        /// Handles circuit breaker state changes
        /// </summary>
        private void OnCircuitStateChanged(object? sender, CircuitStateChangedEventArgs e)
        {
            // Log circuit state changes for observability
            // This can be used to trigger alerts or monitoring
        }

        /// <summary>
        /// Resets the circuit breaker
        /// </summary>
        public void ResetCircuitBreaker()
        {
            _circuitBreaker.ForceReset();
        }

        /// <summary>
        /// Opens the circuit breaker manually
        /// </summary>
        public void OpenCircuitBreaker()
        {
            _circuitBreaker.ForceOpen();
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _circuitBreaker.Dispose();
                    _retryPolicy.Dispose();
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Options for creating a resilient plugin class factory
    /// </summary>
    public class ResilientPluginClassFactoryOptions
    {
        /// <summary>
        /// Circuit breaker options
        /// </summary>
        public CircuitBreakerOptions CircuitBreakerOptions { get; set; } = CircuitBreakerOptions.Default;

        /// <summary>
        /// Retry policy options
        /// </summary>
        public RetryPolicyOptions RetryPolicyOptions { get; set; } = RetryPolicyOptions.Default;

        /// <summary>
        /// Number of old versions to retain
        /// </summary>
        public int RetainOldVersions { get; set; } = 1;

        /// <summary>
        /// Creates default options
        /// </summary>
        public static ResilientPluginClassFactoryOptions Default { get; } = new ResilientPluginClassFactoryOptions();

        /// <summary>
        /// Creates aggressive options (fails fast, retries aggressively)
        /// </summary>
        public static ResilientPluginClassFactoryOptions Aggressive { get; } = new ResilientPluginClassFactoryOptions
        {
            CircuitBreakerOptions = CircuitBreakerOptions.Aggressive,
            RetryPolicyOptions = RetryPolicyOptions.Aggressive,
            RetainOldVersions = 2
        };

        /// <summary>
        /// Creates lenient options (tolerates more failures, retries less)
        /// </summary>
        public static ResilientPluginClassFactoryOptions Lenient { get; } = new ResilientPluginClassFactoryOptions
        {
            CircuitBreakerOptions = CircuitBreakerOptions.Lenient,
            RetryPolicyOptions = RetryPolicyOptions.Lenient,
            RetainOldVersions = 3
        };
    }
}
