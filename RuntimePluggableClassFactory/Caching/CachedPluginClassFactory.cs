using DevelApp.RuntimePluggableClassFactory.Caching;
using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using System;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory
{
    /// <summary>
    /// Plugin class factory with caching support
    /// Caches plugin instances for improved performance
    /// </summary>
    /// <typeparam name="T">Type of plugin interface</typeparam>
    public class CachedPluginClassFactory<T> : PluginClassFactory<T>, IDisposable where T : IPluginClass
    {
        private readonly PluginInstancePool<T> _instancePool;
        private readonly AssemblyMetadataCache _metadataCache;
        private bool _disposed = false;

        /// <summary>
        /// Creates a new cached plugin class factory
        /// </summary>
        /// <param name="pluginLoader">Plugin loader to use</param>
        /// <param name="retainOldVersions">Number of old versions to retain</param>
        /// <param name="poolSize">Maximum pool size per plugin type</param>
        /// <param name="poolLifetime">Maximum lifetime of pooled instances</param>
        public CachedPluginClassFactory(
            IPluginLoader<T> pluginLoader,
            int retainOldVersions = 1,
            int poolSize = 10,
            TimeSpan? poolLifetime = null)
            : base(pluginLoader, retainOldVersions)
        {
            _instancePool = new PluginInstancePool<T>(poolSize, poolLifetime);
            _metadataCache = new AssemblyMetadataCache();
        }

        /// <summary>
        /// Gets a plugin instance with caching
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <returns>Plugin instance</returns>
        public override T? GetInstance(NamespaceString moduleName, IdentifierString name)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CachedPluginClassFactory<T>));

            return _instancePool.GetInstance(
                moduleName.ToString(),
                name.ToString(),
                () => base.GetInstance(moduleName, name));
        }

        /// <summary>
        /// Gets a plugin instance with caching for a specific version
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <param name="version">Version</param>
        /// <returns>Plugin instance</returns>
        public override T? GetInstance(NamespaceString moduleName, IdentifierString name, SemanticVersionNumber version)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CachedPluginClassFactory<T>));

            var versionKey = $"{moduleName}_{name}_{version}";
            return _instancePool.GetInstance(
                moduleName.ToString(),
                versionKey,
                () => base.GetInstance(moduleName, name, version));
        }

        /// <summary>
        /// Returns a plugin instance to the pool
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <param name="instance">Instance to return</param>
        public void ReturnInstance(NamespaceString moduleName, IdentifierString name, T instance)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CachedPluginClassFactory<T>));

            _instancePool.ReturnInstance(moduleName.ToString(), name.ToString(), instance);
        }

        /// <summary>
        /// Gets the assembly metadata cache
        /// </summary>
        public AssemblyMetadataCache MetadataCache => _metadataCache;

        /// <summary>
        /// Gets the instance pool
        /// </summary>
        public PluginInstancePool<T> InstancePool => _instancePool;

        /// <summary>
        /// Clears all cached data
        /// </summary>
        public void ClearCache()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CachedPluginClassFactory<T>));

            _instancePool.Clear();
            _metadataCache.Clear();
        }

        /// <summary>
        /// Refreshes plugins and clears cache
        /// </summary>
        /// <returns>Refresh result</returns>
        public async Task<(bool Success, int Count)> RefreshPluginsWithCacheClearAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CachedPluginClassFactory<T>));

            // Clear cache before refreshing
            ClearCache();
            
            // Refresh plugins
            return await RefreshPluginsAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _instancePool.Dispose();
                    _metadataCache.Dispose();
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
}
