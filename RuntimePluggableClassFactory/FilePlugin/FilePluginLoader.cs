using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.RuntimePluggableClassFactory.Security;
using DevelApp.RuntimePluggableClassFactory.SemanticVersioning;
using DevelApp.Utility.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.FilePlugin
{
    public class FilePluginLoader<T> : IPluginLoader<T>, IDisposable where T : IPluginClass
    {
        private bool _disposed = false;

        public FilePluginLoader(Uri pluginPathUri, IPluginSecurityValidator? securityValidator = null)
        {
            PluginPathUri = pluginPathUri;
            _securityValidator = securityValidator ?? new DefaultPluginSecurityValidator();
        }

        private Uri _pluginPathUri;

        // Track AssemblyLoadContext instances for unloading capability
        private readonly ConcurrentDictionary<string, WeakReference> _loadContexts = new ConcurrentDictionary<string, WeakReference>();

        // Security validator for plugin validation (TDS requirement)
        private readonly IPluginSecurityValidator _securityValidator;

        /// <summary>
        /// Event fired when plugin loading fails
        /// </summary>
        public event EventHandler<PluginLoadingErrorEventArgs>? PluginLoadingFailed;

        /// <summary>
        /// Event fired when security validation fails
        /// </summary>
        public event EventHandler<PluginSecurityValidationFailedEventArgs>? SecurityValidationFailed;

        /// <summary>
        /// Url for the plugin path used
        /// </summary>
        public Uri PluginPathUri
        {
            get
            {
                return _pluginPathUri;
            }
            set
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(FilePluginLoader<T>));
                }
                _pluginPathUri = value;
            }
        }

        /// <summary>
        /// Loads all assemblies from the pluginPath and returns all non-abstract classes implementing interface T
        /// </summary>
        /// <param name="allowedPlugins"></param>
        /// <returns></returns>
        public async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>> LoadPluginsAsync(List<(NamespaceString ModuleName, IdentifierString Name, SemanticVersionNumber Version)> allowedPlugins)
        {
            ThrowIfDisposed();

            List<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)> filteredList = new List<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>();

            foreach (var tuple in await LoadUnfilteredPluginsAsync())
            {
                //filter based on allowed plugins  
                if (allowedPlugins.Contains((tuple.ModuleName, tuple.PluginName, tuple.Version)))
                {
                    filteredList.Add((tuple.ModuleName, tuple.PluginName, tuple.Version, tuple.Description, tuple.Type));
                }
            }

            return filteredList;
        }

        /// <summary>
        /// Loads all assemblies from the pluginPath and returns all types from IPluginClass
        /// </summary>
        /// <returns></returns>
        private async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>> LoadUnfilteredPluginsAsync()
        {
            List<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)> typeList = new List<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>();

            // Isolate plugin context per subfolder
            foreach (string pluginSubfolder in Directory.GetDirectories(_pluginPathUri.LocalPath))
            {
                //Isolate plugins from other parts of the program with collectible context
                PluginLoadContext pluginLoadContext = new PluginLoadContext(pluginSubfolder);

                // Track the context for potential unloading
                _loadContexts.TryAdd(pluginSubfolder, new WeakReference(pluginLoadContext));

                // Load from each assembly in folder
                foreach (string fileName in Directory.GetFiles(pluginSubfolder, "*.dll", SearchOption.AllDirectories))
                {
                    try
                    {
                        // Security validation before loading (TDS requirement)
                        var securityResult = await _securityValidator.ValidateAssemblyAsync(fileName);
                        if (!securityResult.IsValid)
                        {
                            OnSecurityValidationFailed(fileName, pluginSubfolder, securityResult);
                            continue; // Skip loading this plugin
                        }

                        // Log security warnings if any
                        if (securityResult.Warnings.Any())
                        {
                            OnSecurityValidationFailed(fileName, pluginSubfolder, securityResult);
                        }

                        // Security: Certificate validation is handled by DefaultPluginSecurityValidator

                        // Interface assembly exclusion: Handled by type filtering in LoadPluginClasses

                        Assembly assembly = pluginLoadContext.LoadFromAssemblyPath(fileName);

                        // Additional security validation on loaded assembly
                        var loadedSecurityResult = _securityValidator.ValidateLoadedAssembly(assembly);
                        if (!loadedSecurityResult.IsValid)
                        {
                            OnSecurityValidationFailed(fileName, pluginSubfolder, loadedSecurityResult);
                            continue; // Skip this assembly
                        }

                        //Get assembly from already loaded Default AssemblyLoadContext if possible so isolation is not needed and to avoid
                        Assembly? defaultAssembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(x => x.FullName == assembly.FullName);
                        if (defaultAssembly != null)
                        {
                            assembly = defaultAssembly;
                        }

                        foreach (Type identifiedType in LoadFromAssembly(assembly))
                        {
                            //Assembly debug
                            //Assembly pluginAssemblyT = typeof(T).Assembly;
                            //Assembly pluginAssemblyType = identifiedType.Assembly;
                            //Assembly pluginInterfaceAssembly = typeof(IPluginClass).Assembly;

                            //System.Runtime.Loader.AssemblyLoadContext pluginAssemblyTLoader = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(T).Assembly);
                            //System.Runtime.Loader.AssemblyLoadContext pluginAssemblyTypeLoader = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(pluginType.Assembly);
                            //System.Runtime.Loader.AssemblyLoadContext pluginInterfaceAssemblyLoader = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(IPluginClass).Assembly);
                            //End Assembly debug

                            //Workaround if needed https://makolyte.com/csharp-generic-plugin-loader/
                            if (typeof(T).IsAssignableFrom(identifiedType) && !identifiedType.IsAbstract && !identifiedType.IsInterface)
                            {
                                T? identifiedTypeInstance = (T?)Activator.CreateInstance(identifiedType);
                                if (identifiedTypeInstance != null)
                                {
                                    typeList.Add((identifiedTypeInstance.Module, identifiedTypeInstance.Name, identifiedTypeInstance.Version, identifiedTypeInstance.Description, identifiedType));
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Enhanced error handling with detailed logging (TDS requirement)
                        OnPluginLoadingFailed(fileName, pluginSubfolder, ex);
                    }
                }
            }
            return typeList;
        }

        /// <summary>
        /// Return all the non-abstract classes implementing interface T
        /// </summary>
        /// <param name="assembly"></param>
        /// <returns></returns>
        private IEnumerable<Type> LoadFromAssembly(Assembly assembly)
        {
            foreach (Type type in assembly.GetTypes())
            {
                // Type filtering handles interface exclusion; IsAssignableFrom works correctly
                //Workaround if needed https://makolyte.com/csharp-generic-plugin-loader/
                if (typeof(IPluginClass).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                {
                    yield return type;
                }
            }
        }

        /// <summary>
        /// Returns all possible plugins located in the file folder
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>> ListAllPossiblePluginsAsync()
        {
            ThrowIfDisposed();
            return await LoadUnfilteredPluginsAsync();
        }

        /// <summary>
        /// Lists all plugins matching a version range
        /// </summary>
        /// <param name="versionRange">Version range to match</param>
        /// <returns>Plugins matching the version range</returns>
        public async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>> ListPluginsByVersionRangeAsync(VersionRange versionRange)
        {
            ThrowIfDisposed();
            var allPlugins = await LoadUnfilteredPluginsAsync();
            return allPlugins.Where(p => versionRange.Contains(p.Version));
        }

        /// <summary>
        /// Lists all plugins matching a specific module
        /// </summary>
        /// <param name="moduleName">Module name to filter by</param>
        /// <returns>Plugins from the specified module</returns>
        public async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>> ListPluginsByModuleAsync(NamespaceString moduleName)
        {
            ThrowIfDisposed();
            var allPlugins = await LoadUnfilteredPluginsAsync();
            return allPlugins.Where(p => p.ModuleName == moduleName);
        }

        /// <summary>
        /// Unloads a specific plugin assembly by path (TDS requirement)
        /// </summary>
        /// <param name="pluginPath">Path to the plugin to unload</param>
        /// <returns>True if unloaded successfully, false if not found or already unloaded</returns>
        public bool UnloadPlugin(string? pluginPath)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(pluginPath))
            {
                return false;
            }

            // Try to find the plugin path in our tracked contexts
            foreach (var contextRef in _loadContexts.Values)
            {
                if (contextRef.IsAlive && contextRef.Target is PluginLoadContext context)
                {
                    // Check if this context loaded the plugin
                    if (context.Assemblies.Any(a => a.Location.Equals(pluginPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        context.Unload();
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Unloads all plugin assemblies (TDS requirement)
        /// </summary>
        public void UnloadAllPlugins()
        {
            ThrowIfDisposed();
            foreach (var contextRef in _loadContexts.Values)
            {
                if (contextRef.IsAlive && contextRef.Target is PluginLoadContext context)
                {
                    context.Unload();
                }
            }
            _loadContexts.Clear();
        }

        /// <summary>
        /// Fires the plugin loading failed event
        /// </summary>
        private void OnPluginLoadingFailed(string fileName, string pluginPath, Exception exception)
        {
            try
            {
                PluginLoadingFailed?.Invoke(this, new PluginLoadingErrorEventArgs
                {
                    FileName = fileName,
                    PluginPath = pluginPath,
                    Exception = exception,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch
            {
                // Ignore errors in event firing to prevent cascading failures
            }
        }

        /// <summary>
        /// Fires the security validation failed event
        /// </summary>
        private void OnSecurityValidationFailed(string fileName, string pluginPath, PluginSecurityValidationResult validationResult)
        {
            try
            {
                SecurityValidationFailed?.Invoke(this, new PluginSecurityValidationFailedEventArgs
                {
                    FileName = fileName,
                    PluginPath = pluginPath,
                    ValidationResult = validationResult,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch
            {
                // Ignore errors in event firing to prevent cascading failures
            }
        }

        /// <summary>
        /// Disposes of the file plugin loader and its resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected implementation of Dispose pattern
        /// </summary>
        /// <param name="disposing">True if called from Dispose(), false if called from finalizer</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Unload all plugin contexts
                    UnloadAllPlugins();

                    // Dispose security validator if it's disposable
                    if (_securityValidator is IDisposable disposableValidator)
                    {
                        disposableValidator.Dispose();
                    }
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// Throws if this object has been disposed
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FilePluginLoader<T>));
            }
        }
    }

    /// <summary>
    /// Event arguments for plugin loading errors
    /// </summary>
    public class PluginLoadingErrorEventArgs : EventArgs
    {
        public string? FileName { get; set; }
        public string? PluginPath { get; set; }
        public Exception? Exception { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Event arguments for plugin security validation failures
    /// </summary>
    public class PluginSecurityValidationFailedEventArgs : EventArgs
    {
        public string? FileName { get; set; }
        public string? PluginPath { get; set; }
        public PluginSecurityValidationResult? ValidationResult { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
