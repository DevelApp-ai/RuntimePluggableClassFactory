using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Async
{
    /// <summary>
    /// Async plugin loader that loads plugins on background threads
    /// Improves responsiveness by offloading CPU-bound loading operations
    /// </summary>
    /// <typeparam name="T">Type of plugin interface</typeparam>
    public class AsyncPluginLoader<T> : IPluginLoader<T> where T : IPluginClass
    {
        private readonly IPluginLoader<T> _innerLoader;
        private readonly Task _initialLoadTask;
        private readonly object _loadLock = new object();
        private bool _isLoaded = false;

        /// <summary>
        /// Creates a new async plugin loader
        /// </summary>
        /// <param name="innerLoader">Inner loader to wrap</param>
        public AsyncPluginLoader(IPluginLoader<T> innerLoader)
        {
            _innerLoader = innerLoader ?? throw new ArgumentNullException(nameof(innerLoader));
            _initialLoadTask = Task.Run(() => LoadPluginsInternalAsync());
        }

        /// <summary>
        /// Event fired when plugin loading fails
        /// </summary>
        public event EventHandler<PluginLoadingErrorEventArgs>? PluginLoadingFailed
        {
            add => _innerLoader.PluginLoadingFailed += value;
            remove => _innerLoader.PluginLoadingFailed -= value;
        }

        /// <summary>
        /// Event fired when security validation fails
        /// </summary>
        public event EventHandler<PluginSecurityValidationFailedEventArgs>? SecurityValidationFailed
        {
            add => _innerLoader.SecurityValidationFailed += value;
            remove => _innerLoader.SecurityValidationFailed -= value;
        }

        /// <summary>
        /// Whether the initial async load has completed
        /// </summary>
        public bool IsInitialLoadComplete => _initialLoadTask.IsCompleted;

        /// <summary>
        /// Gets the initial load task for awaiting
        /// </summary>
        public Task InitialLoadTask => _initialLoadTask;

        /// <summary>
        /// Loads plugins asynchronously
        /// </summary>
        /// <param name="allowedPlugins">List of allowed plugins</param>
        /// <returns>List of loaded plugins</returns>
        public async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>> LoadPluginsAsync(
            List<(NamespaceString ModuleName, IdentifierString Name, SemanticVersionNumber Version)> allowedPlugins)
        {
            // If initial load is still running, wait for it
            if (!_isLoaded && !_initialLoadTask.IsCompleted)
            {
                await _initialLoadTask.ConfigureAwait(false);
            }

            // Load plugins on a background thread
            return await Task.Run(() => _innerLoader.LoadPluginsAsync(allowedPlugins)).ConfigureAwait(false);
        }

        /// <summary>
        /// Lists all possible plugins asynchronously
        /// </summary>
        /// <returns>List of all possible plugins</returns>
        public async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>> ListAllPossiblePluginsAsync()
        {
            // If initial load is still running, wait for it
            if (!_isLoaded && !_initialLoadTask.IsCompleted)
            {
                await _initialLoadTask.ConfigureAwait(false);
            }

            // List plugins on a background thread
            return await Task.Run(() => _innerLoader.ListAllPossiblePluginsAsync()).ConfigureAwait(false);
        }

        /// <summary>
        /// Unloads a specific plugin assembly by path
        /// </summary>
        /// <param name="pluginPath">Path to the plugin to unload</param>
        /// <returns>True if unloaded successfully</returns>
        public bool UnloadPlugin(string? pluginPath)
        {
            return _innerLoader.UnloadPlugin(pluginPath);
        }

        /// <summary>
        /// Unloads all plugin assemblies
        /// </summary>
        public void UnloadAllPlugins()
        {
            _innerLoader.UnloadAllPlugins();
            _isLoaded = false;
        }

        /// <summary>
        /// Internal method to load plugins on startup
        /// </summary>
        /// <returns>Task for the load operation</returns>
        private async Task LoadPluginsInternalAsync()
        {
            try
            {
                lock (_loadLock)
                {
                    if (_isLoaded)
                        return;
                }

                // Pre-load all plugins in the background
                await _innerLoader.ListAllPossiblePluginsAsync().ConfigureAwait(false);

                lock (_loadLock)
                {
                    _isLoaded = true;
                }
            }
            catch (Exception ex)
            {
                // Log but don't throw - initial load failure is not fatal
                // The plugins will be loaded on demand
            }
        }

        /// <summary>
        /// Forces a refresh of all plugins
        /// </summary>
        /// <returns>Task for the refresh operation</returns>
        public async Task RefreshAsync()
        {
            lock (_loadLock)
            {
                _isLoaded = false;
            }

            UnloadAllPlugins();
            await LoadPluginsInternalAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Gets a plugin instance asynchronously
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="pluginName">Plugin name</param>
        /// <returns>Plugin instance or null</returns>
        public async Task<T?> GetInstanceAsync(NamespaceString moduleName, IdentifierString pluginName)
        {
            // This would require integration with PluginClassFactory
            // For now, just return null as this is a loader, not a factory
            return default;
        }
    }

    /// <summary>
    /// Factory for creating async plugin loaders
    /// </summary>
    public static class AsyncPluginLoaderFactory
    {
        /// <summary>
        /// Creates an async wrapper around a file plugin loader
        /// </summary>
        /// <typeparam name="T">Plugin interface type</typeparam>
        /// <param name="pluginPathUri">Path to plugin directory</param>
        /// <param name="securityValidator">Security validator</param>
        /// <returns>Async plugin loader</returns>
        public static AsyncPluginLoader<T> CreateFilePluginLoader<T>(
            Uri pluginPathUri,
            DevelApp.RuntimePluggableClassFactory.Security.IPluginSecurityValidator? securityValidator = null) 
            where T : IPluginClass
        {
            var fileLoader = new FilePlugin.FilePluginLoader<T>(pluginPathUri, securityValidator);
            return new AsyncPluginLoader<T>(fileLoader);
        }
    }
}
