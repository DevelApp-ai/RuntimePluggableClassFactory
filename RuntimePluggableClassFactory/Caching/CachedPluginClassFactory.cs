using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.RuntimePluggableClassFactory.SemanticVersioning;
using DevelApp.Utility.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Caching
{
    /// <summary>
    /// Plugin class factory with caching and pooling support
    /// Improves performance by caching plugin instances and metadata
    /// </summary>
    /// <typeparam name="T">Plugin interface type</typeparam>
    public class CachedPluginClassFactory<T> : PluginClassFactory<T>, IDisposable where T : IPluginClass
    {
        private readonly PluginInstancePool<T> _instancePool;
        private readonly AssemblyMetadataCache _metadataCache;
        private readonly ConcurrentDictionary<(NamespaceString ModuleName, IdentifierString Name, SemanticVersionNumber Version), T> _instanceCache = 
            new ConcurrentDictionary<(NamespaceString, IdentifierString, SemanticVersionNumber), T>();
        
        private readonly ConcurrentDictionary<(NamespaceString ModuleName, IdentifierString Name), bool> _isStatelessCache = 
            new ConcurrentDictionary<(NamespaceString, IdentifierString), bool>();
        
        private bool _disposed = false;

        /// <summary>
        /// Creates a new cached plugin class factory
        /// </summary>
        /// <param name="pluginLoader">Plugin loader</param>
        /// <param name="retainOldVersions">Number of old versions to retain</param>
        /// <param name="poolSize">Maximum pool size per plugin type</param>
        /// <param name="enableInstanceCaching">Whether to enable instance caching</param>
        public CachedPluginClassFactory(
            IPluginLoader<T> pluginLoader,
            int retainOldVersions = 1,
            int poolSize = 10,
            bool enableInstanceCaching = true)
            : base(pluginLoader, retainOldVersions)
        {
            _instancePool = new PluginInstancePool<T>(poolSize);
            _metadataCache = new AssemblyMetadataCache();
            EnableInstanceCaching = enableInstanceCaching;
        }

        /// <summary>
        /// Whether instance caching is enabled
        /// </summary>
        public bool EnableInstanceCaching { get; set; }

        /// <summary>
        /// Instance pool for plugin instances
        /// </summary>
        public PluginInstancePool<T> InstancePool => _instancePool;

        /// <summary>
        /// Metadata cache for assemblies
        /// </summary>
        public AssemblyMetadataCache MetadataCache => _metadataCache;

        /// <summary>
        /// Gets a plugin instance with caching support
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <returns>Plugin instance or null</returns>
        public new T? GetInstance(NamespaceString moduleName, IdentifierString name)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CachedPluginClassFactory<T>));

            // Try to get from instance cache first
            if (EnableInstanceCaching)
            {
                var newestVersion = GetNewestVersion(moduleName, name);
                if (newestVersion != null)
                {
                    var cacheKey = (moduleName, name, newestVersion);
                    if (_instanceCache.TryGetValue(cacheKey, out var cachedInstance))
                    {
                        return cachedInstance;
                    }
                }
            }

            // Fall back to base implementation
            var instance = base.GetInstance(moduleName, name);
            
            if (instance != null && EnableInstanceCaching)
            {
                var newestVersion = GetNewestVersion(moduleName, name);
                if (newestVersion != null)
                {
                    var cacheKey = (moduleName, name, newestVersion);
                    _instanceCache.TryAdd(cacheKey, instance);
                }
            }

            return instance;
        }

        /// <summary>
        /// Gets a plugin instance for a specific version with caching support
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <param name="version">Version</param>
        /// <returns>Plugin instance or null</returns>
        public new T? GetInstance(NamespaceString moduleName, IdentifierString name, SemanticVersionNumber version)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CachedPluginClassFactory<T>));

            // Try to get from instance cache first
            if (EnableInstanceCaching)
            {
                var cacheKey = (moduleName, name, version);
                if (_instanceCache.TryGetValue(cacheKey, out var cachedInstance))
                {
                    return cachedInstance;
                }
            }

            // Fall back to base implementation
            var instance = base.GetInstance(moduleName, name, version);
            
            if (instance != null && EnableInstanceCaching)
            {
                var cacheKey = (moduleName, name, version);
                _instanceCache.TryAdd(cacheKey, instance);
            }

            return instance;
        }

        /// <summary>
        /// Gets a plugin instance from the pool or creates a new one
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <returns>Plugin instance</returns>
        public T GetPooledInstance(NamespaceString moduleName, IdentifierString name)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CachedPluginClassFactory<T>));

            return _instancePool.GetInstance(
                moduleName.ToString(),
                name.ToString(),
                () => base.GetInstance(moduleName, name) ?? throw new InvalidOperationException("Failed to create plugin instance"));
        }

        /// <summary>
        /// Returns a plugin instance to the pool
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <param name="instance">Instance to return</param>
        public void ReturnToPool(NamespaceString moduleName, IdentifierString name, T instance)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CachedPluginClassFactory<T>));

            _instancePool.ReturnInstance(moduleName.ToString(), name.ToString(), instance);
        }

        /// <summary>
        /// Gets a plugin instance from the pool or creates a new one asynchronously
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <returns>Plugin instance</returns>
        public async Task<T> GetPooledInstanceAsync(NamespaceString moduleName, IdentifierString name)
        {
            return await Task.Run(() => GetPooledInstance(moduleName, name));
        }

        /// <summary>
        /// Marks a plugin as stateless (safe to cache and pool)
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <param name="isStateless">Whether the plugin is stateless</param>
        public void MarkAsStateless(NamespaceString moduleName, IdentifierString name, bool isStateless = true)
        {
            _isStatelessCache[(moduleName, name)] = isStateless;
        }

        /// <summary>
        /// Checks if a plugin is marked as stateless
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <returns>True if the plugin is stateless</returns>
        public bool IsStateless(NamespaceString moduleName, IdentifierString name)
        {
            return _isStatelessCache.GetValueOrDefault((moduleName, name));
        }

        /// <summary>
        /// Clears all cached instances
        /// </summary>
        public void ClearInstanceCache()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CachedPluginClassFactory<T>));

            _instanceCache.Clear();
        }

        /// <summary>
        /// Clears cached instances for a specific plugin
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        public void ClearInstanceCache(NamespaceString moduleName, IdentifierString name)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CachedPluginClassFactory<T>));

            var keysToRemove = _instanceCache.Keys
                .Where(k => k.ModuleName == moduleName && k.Name == name)
                .ToArray();
            
            foreach (var key in keysToRemove)
            {
                _instanceCache.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// Gets cache statistics
        /// </summary>
        public CacheStatistics GetCacheStatistics()
        {
            return new CacheStatistics
            {
                InstanceCacheCount = _instanceCache.Count,
                InstancePoolCount = _instancePool.TotalPooledCount,
                MetadataCacheCount = _metadataCache.Count,
                InstancePoolStatistics = _instancePool.GetStatistics()
            };
        }

        /// <summary>
        /// Refreshes plugins and clears stale cache entries
        /// </summary>
        /// <returns>Refresh result</returns>
        public new async Task<(bool Success, int Count)> RefreshPluginsAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CachedPluginClassFactory<T>));

            // Clear stale cache entries
            ClearStaleCacheEntries();

            // Refresh plugins
            return await base.RefreshPluginsAsync();
        }

        /// <summary>
        /// Clears cache entries that have expired metadata
        /// </summary>
        private void ClearStaleCacheEntries()
        {
            // Clear expired metadata from cache
            var expiredPaths = _metadataCache.GetCachedAssemblyPaths()
                .Where(path => _metadataCache.Get(path)?.IsExpired == true)
                .ToArray();
            
            foreach (var path in expiredPaths)
            {
                _metadataCache.Remove(path);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _instancePool.Dispose();
                    _metadataCache.Dispose();
                    _instanceCache.Clear();
                    _isStatelessCache.Clear();
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
    /// Cache statistics
    /// </summary>
    public class CacheStatistics
    {
        /// <summary>
        /// Number of cached instances
        /// </summary>
        public int InstanceCacheCount { get; set; }

        /// <summary>
        /// Number of pooled instances
        /// </summary>
        public int InstancePoolCount { get; set; }

        /// <summary>
        /// Number of cached assembly metadata
        /// </summary>
        public int MetadataCacheCount { get; set; }

        /// <summary>
        /// Instance pool statistics
        /// </summary>
        public Dictionary<(string ModuleName, string PluginName), int> InstancePoolStatistics { get; set; } = 
            new Dictionary<(string, string), int>();
    }
}
