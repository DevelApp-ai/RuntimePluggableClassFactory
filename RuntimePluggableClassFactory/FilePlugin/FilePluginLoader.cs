using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.RuntimePluggableClassFactory.Security;
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
    public class FilePluginLoader<T>:IPluginLoader<T> where T:IPluginClass
    {
        public FilePluginLoader(Uri pluginPathUri, IPluginSecurityValidator securityValidator = null)
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
        public event EventHandler<PluginLoadingErrorEventArgs> PluginLoadingFailed;

        /// <summary>
        /// Event fired when security validation fails
        /// </summary>
        public event EventHandler<PluginSecurityValidationFailedEventArgs> SecurityValidationFailed;

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
                if (!value.IsAbsoluteUri)
                {
                    throw new PluginClassFactoryException($"The supplied uri in {nameof(value)} is not an absolute uri but is required to be");
                }
                if (!Directory.Exists(value.AbsolutePath))
                {
                    throw new PluginClassFactoryException($"Plugin directory {value.AbsolutePath} does not exist");
                }
                _pluginPathUri = value;
            }
        }

        /// <summary>
        /// Unloads a specific plugin assembly by path
        /// </summary>
        /// <param name="pluginPath">Path to the plugin to unload</param>
        /// <returns>True if unloaded successfully, false if not found or already unloaded</returns>
        public bool UnloadPlugin(string pluginPath)
        {
            if (_loadContexts.TryRemove(pluginPath, out WeakReference? contextRef) && contextRef?.IsAlive == true)
            {
                if (contextRef.Target is PluginLoadContext context)
                {
                    context.Unload();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Unloads all plugin assemblies
        /// </summary>
        public void UnloadAllPlugins()
        {
            foreach (var kvp in _loadContexts.ToList())
            {
                if (kvp.Value.IsAlive && kvp.Value.Target is PluginLoadContext context)
                {
                    context.Unload();
                }
            }
            _loadContexts.Clear();
        }

        /// <summary>
        /// Loads all assemblies from the pluginPath and returns all non-abstract classes implementing interface T
        /// </summary>
        /// <param name="allowedPlugins"></param>
        /// <returns></returns>
        public async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string Description, Type Type)>> LoadPluginsAsync(List<(NamespaceString ModuleName, IdentifierString Name, SemanticVersionNumber Version)> allowedPlugins)
        {
            List<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string Description, Type Type)> filteredList = new List<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string Description, Type Type)>();

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
        private async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string Description, Type Type)>> LoadUnfilteredPluginsAsync()
        {
            List<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string Description, Type Type)> typeList = new List<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string Description, Type Type)>();

            // Isolate plugin context per subfolder
            foreach (string pluginSubfolder in Directory.GetDirectories(_pluginPathUri.AbsolutePath))
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

                        //TODO check if assembly certificate is valid to improve security

                        //TODO exclude interface assembly from context loaded via typeof(T).Assembly.FullName

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


                            //TODO Here be dragons. We create an instance before the instance is accepted
                            //TODO solution assembly embedded file containing ModuleName, PluginName, Version, Description via https://devblogs.microsoft.com/dotnet/new-c-source-generator-samples/
                            //TODO examine if there is a need to exclude interface dll to avoid problem with IsAssignableFrom falsly returning false
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
            // Return each T for IPluginClass
            foreach (Type type in assembly.GetTypes())
            {
                //TODO examine if there is a need to exclude interface dll to avoid problem with IsAssignableFrom falsly returning false
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
        public async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string Description, Type Type)>> ListAllPossiblePluginsAsync()
        {
            return await LoadUnfilteredPluginsAsync();
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
    }

    /// <summary>
    /// Event arguments for plugin loading errors
    /// </summary>
    public class PluginLoadingErrorEventArgs : EventArgs
    {
        public string FileName { get; set; }
        public string PluginPath { get; set; }
        public Exception Exception { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Event arguments for plugin security validation failures
    /// </summary>
    public class PluginSecurityValidationFailedEventArgs : EventArgs
    {
        public string FileName { get; set; }
        public string PluginPath { get; set; }
        public PluginSecurityValidationResult ValidationResult { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
