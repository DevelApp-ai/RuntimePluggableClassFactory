using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Async
{
    /// <summary>
    /// Async plugin class factory that performs operations on background threads
    /// Improves responsiveness for UI and API applications
    /// </summary>
    /// <typeparam name="T">Type of plugin interface</typeparam>
    public class AsyncPluginClassFactory<T> : PluginClassFactory<T>, IDisposable where T : IPluginClass
    {
        private readonly IPluginLoader<T> _asyncLoader;
        private bool _disposed = false;

        /// <summary>
        /// Creates a new async plugin class factory
        /// </summary>
        /// <param name="asyncLoader">Async plugin loader</param>
        /// <param name="retainOldVersions">Number of old versions to retain</param>
        public AsyncPluginClassFactory(
            IPluginLoader<T> asyncLoader,
            int retainOldVersions = 1)
            : base(asyncLoader, retainOldVersions)
        {
            _asyncLoader = asyncLoader ?? throw new ArgumentNullException(nameof(asyncLoader));
        }

        /// <summary>
        /// Gets a plugin instance asynchronously
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <returns>Plugin instance or null</returns>
        public async Task<T?> GetInstanceAsync(NamespaceString moduleName, IdentifierString name)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AsyncPluginClassFactory<T>));

            return await Task.Run(() => base.GetInstance(moduleName, name)).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets a plugin instance asynchronously for a specific version
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <param name="version">Version</param>
        /// <returns>Plugin instance or null</returns>
        public async Task<T?> GetInstanceAsync(
            NamespaceString moduleName,
            IdentifierString name,
            SemanticVersionNumber version)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AsyncPluginClassFactory<T>));

            return await Task.Run(() => base.GetInstance(moduleName, name, version)).ConfigureAwait(false);
        }

        /// <summary>
        /// Tries to get a plugin instance asynchronously
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <param name="instance">Output parameter for the plugin instance</param>
        /// <returns>True if instance was found and created successfully</returns>
        public async Task<bool> TryGetInstanceAsync(
            NamespaceString moduleName,
            IdentifierString name,
            out T? instance)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AsyncPluginClassFactory<T>));

            try
            {
                instance = await GetInstanceAsync(moduleName, name).ConfigureAwait(false);
                return instance != null;
            }
            catch
            {
                instance = default;
                return false;
            }
        }

        /// <summary>
        /// Tries to get a plugin instance asynchronously for a specific version
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <param name="version">Version</param>
        /// <param name="instance">Output parameter for the plugin instance</param>
        /// <returns>True if instance was found and created successfully</returns>
        public async Task<bool> TryGetInstanceAsync(
            NamespaceString moduleName,
            IdentifierString name,
            SemanticVersionNumber version,
            out T? instance)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AsyncPluginClassFactory<T>));

            try
            {
                instance = await GetInstanceAsync(moduleName, name, version).ConfigureAwait(false);
                return instance != null;
            }
            catch
            {
                instance = default;
                return false;
            }
        }

        /// <summary>
        /// Gets all available plugin instances asynchronously
        /// </summary>
        /// <returns>Dictionary of plugin instances by (module, name)</returns>
        public async Task<IDictionary<(NamespaceString ModuleName, IdentifierString Name), T>> GetAllInstancesAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AsyncPluginClassFactory<T>));

            return await Task.Run(() =>
            {
                var result = new Dictionary<(NamespaceString, IdentifierString), T>();
                var plugins = GetAllInstanceNamesDescriptionsAndVersions();
                
                foreach (var (moduleName, name, _, versions) in plugins)
                {
                    if (versions.Count > 0)
                    {
                        var instance = GetInstance(moduleName, name);
                        if (instance != null)
                        {
                            result[(moduleName, name)] = instance;
                        }
                    }
                }
                
                return result;
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Refreshes plugins asynchronously
        /// </summary>
        /// <returns>Refresh result</returns>
        public new async Task<(bool Success, int Count)> RefreshPluginsAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AsyncPluginClassFactory<T>));

            // Use the async loader if it supports async refresh
            if (_asyncLoader is AsyncPluginLoader<T> asyncLoader)
            {
                await asyncLoader.RefreshAsync().ConfigureAwait(false);
            }

            return await base.RefreshPluginsAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Gets possible plugins asynchronously
        /// </summary>
        /// <returns>List of possible plugins</returns>
        public async Task<IEnumerable<(NamespaceString moduleName, IdentifierString pluginName, SemanticVersionNumber version, string? Description, Type Type)>> GetPossiblePluginsAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AsyncPluginClassFactory<T>));

            return await Task.Run(() => base.GetPossiblePlugins()).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks if a plugin is available asynchronously
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="name">Plugin name</param>
        /// <returns>True if plugin is available</returns>
        public async Task<bool> IsPluginAvailableAsync(NamespaceString moduleName, IdentifierString name)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AsyncPluginClassFactory<T>));

            return await Task.Run(() => base.IsPluginAvailable(moduleName, name)).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_asyncLoader is IDisposable disposableLoader)
                    {
                        disposableLoader.Dispose();
                    }
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
