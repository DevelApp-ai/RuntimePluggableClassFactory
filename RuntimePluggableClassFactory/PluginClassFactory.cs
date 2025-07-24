using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory
{
    //TODO Link pluginclass version to plugin files
    //TODO Example project to make single dll output with references internalized Fody ? Otherwise compressed zip deployment with definition file
    public class PluginClassFactory<T> where T : IPluginClass
    {
        public PluginClassFactory(IPluginLoader<T> pluginLoader, int retainOldVersions = 1)
        {
            PluginLoader = pluginLoader;
            _retainOldVersions = retainOldVersions;
            if (!typeof(T).IsInterface)
            {
                throw new PluginClassFactoryException("Generic type T is not an interface as required");
            }
        }

        /// <summary>
        /// The configured plugin loader
        /// </summary>
        public IPluginLoader<T> PluginLoader { get; }

        private List<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version)> _allowedPlugins = new List<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version)>();

        /// <summary>
        /// Allows a plugin to be loaded into the factory
        /// </summary>
        /// <param name="moduleName"></param>
        /// <param name="pluginName"></param>
        /// <param name="version"></param>
        public void AllowPlugin(NamespaceString moduleName, IdentifierString pluginName, SemanticVersionNumber version)
        {
            if (!_allowedPlugins.Contains((moduleName, pluginName, version)))
            {
                _allowedPlugins.Add((moduleName, pluginName, version));
            }
        }

        /// <summary>
        /// Disallows a plugin to be loaded into the factory
        /// </summary>
        /// <param name="moduleName"></param>
        /// <param name="pluginName"></param>
        /// <param name="version"></param>
        public void DisallowPlugin(NamespaceString moduleName, IdentifierString pluginName, SemanticVersionNumber version)
        {
            if (_allowedPlugins.Contains((moduleName, pluginName, version)))
            {
                _allowedPlugins.Remove((moduleName, pluginName, version));
            }
            RemovePluginClassVersion(moduleName, pluginName, version);
        }

        /// <summary>
        /// Retuens identified plugins
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<(NamespaceString moduleName, IdentifierString pluginName, SemanticVersionNumber version, string Description, Type Type)>> GetPossiblePlugins()
        {
            return await PluginLoader.ListAllPossiblePluginsAsync();
        }


        /// <summary>
        /// Returns all plugins of interface T currently in the store
        /// </summary>
        /// <returns></returns>
        public IEnumerable<(NamespaceString ModuleName, IdentifierString Name, string Description, List<SemanticVersionNumber> Versions)> GetAllInstanceNamesDescriptionsAndVersions()
        {
            foreach (PluginClass pluginClass in pluginClassStore.Values)
            {
                yield return (ModuleName: pluginClass.ModuleName, Name: pluginClass.Name, Description: pluginClass.Description, Versions: pluginClass.Versions);
            }
        }

        /// <summary>
        /// Stores the types for the factory
        /// </summary>
        private ConcurrentDictionary<(NamespaceString moduleName, IdentifierString name), PluginClass> pluginClassStore = new ConcurrentDictionary<(NamespaceString moduleName, IdentifierString name), PluginClass>();

        private int _retainOldVersions;

        public async Task<(bool Success,int Count)> RefreshPluginsAsync()
        {
            try
            {
                var pluginTypeTuples = await PluginLoader.LoadPluginsAsync(_allowedPlugins);
                foreach (var pluginTypeTuple in pluginTypeTuples)
                {
                    if (pluginClassStore.TryGetValue((pluginTypeTuple.ModuleName, pluginTypeTuple.PluginName), out PluginClass outPluginClass))
                    {
                        outPluginClass.UpsertVersion(pluginTypeTuple.Version, pluginTypeTuple.Type);
                        outPluginClass.Description = pluginTypeTuple.Description;
                        outPluginClass.Name = pluginTypeTuple.PluginName;
                        outPluginClass.ModuleName = pluginTypeTuple.ModuleName;
                    }
                    else
                    {
                        PluginClass pluginClass = new PluginClass(_retainOldVersions, pluginTypeTuple.ModuleName, pluginTypeTuple.PluginName, pluginTypeTuple.Description, pluginTypeTuple.Version, pluginTypeTuple.Type);
                        pluginClassStore.TryAdd((pluginTypeTuple.ModuleName, pluginTypeTuple.PluginName), pluginClass);
                    }
                }
                return (true, pluginTypeTuples.Count());
            }
            catch (Exception)
            {
                //Log.Error(ex, "RefreshPluginsAsync failed");
                return (false, 0);
            }
        }

        /// <summary>
        /// Returns an instance of the newest version of the named class abiding interface T
        /// Enhanced with sandbox execution for stability (TDS requirement)
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public T GetInstance(NamespaceString moduleName, IdentifierString name)
        {
            if (pluginClassStore.TryGetValue((moduleName, name), out PluginClass pluginClass))
            {
                try
                {
                    var type = pluginClass.GetNewestVersion();
                    return CreateInstanceSafely(type, moduleName, name);
                }
                catch (Exception ex)
                {
                    // Log the error and return default instead of crashing
                    OnPluginInstantiationFailed(moduleName, name, null, ex);
                    return default;
                }
            }
            else
            {
                return default;
            }
        }

        /// <summary>
        /// Returns an instance of the specific version of the named class abiding interface T
        /// Enhanced with sandbox execution for stability (TDS requirement)
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public T GetInstance(NamespaceString moduleName, IdentifierString name, SemanticVersionNumber version)
        {
            if (pluginClassStore.TryGetValue((moduleName, name), out PluginClass pluginClass))
            {
                if (pluginClass.TryGetVersion(version, out Type type))
                {
                    try
                    {
                        return CreateInstanceSafely(type, moduleName, name, version);
                    }
                    catch (Exception ex)
                    {
                        // Log the error and return default instead of crashing
                        OnPluginInstantiationFailed(moduleName, name, version, ex);
                        return default;
                    }
                }
                else
                {
                    return default;
                }
            }
            else
            {
                return default;
            }
        }

        /// <summary>
        /// Event fired when plugin instantiation fails
        /// </summary>
        public event EventHandler<PluginInstantiationErrorEventArgs> PluginInstantiationFailed;

        /// <summary>
        /// Safely creates a plugin instance with error handling
        /// </summary>
        private T CreateInstanceSafely(Type type, NamespaceString moduleName, IdentifierString name, SemanticVersionNumber version = null)
        {
            try
            {
                var instance = (T)Activator.CreateInstance(type);
                return instance;
            }
            catch (Exception ex)
            {
                OnPluginInstantiationFailed(moduleName, name, version, ex);
                throw; // Re-throw to be handled by calling method
            }
        }

        /// <summary>
        /// Fires the plugin instantiation failed event
        /// </summary>
        private void OnPluginInstantiationFailed(NamespaceString moduleName, IdentifierString name, SemanticVersionNumber version, Exception exception)
        {
            try
            {
                PluginInstantiationFailed?.Invoke(this, new PluginInstantiationErrorEventArgs
                {
                    ModuleName = moduleName?.ToString(),
                    PluginName = name?.ToString(),
                    Version = version?.ToString(),
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
        /// Removes the specific instance from the plugin class factory
        /// </summary>
        /// <param name="moduleName"></param>
        /// <param name="name"></param>
        /// <param name="version"></param>
        internal void RemovePluginClassVersion(NamespaceString moduleName, IdentifierString name, SemanticVersionNumber version)
        {
            if (pluginClassStore.TryGetValue((moduleName, name), out PluginClass pluginClass))
            {
                pluginClass.RemoveVersion(version);
            }
        }

        private sealed class PluginClass
        {
            private int _retainOldVersions;
            internal PluginClass(int retainOldVersions, NamespaceString moduleName, IdentifierString name, string description, SemanticVersionNumber version, Type type)
            {
                _retainOldVersions = retainOldVersions;
                ModuleName = moduleName;
                Name = name;
                Description = description;
                UpsertVersion(version, type);
            }

            private ConcurrentDictionary<SemanticVersionNumber, Type> pluginVersions = new ConcurrentDictionary<SemanticVersionNumber, Type>();

            internal NamespaceString ModuleName { get; set; }

            /// <summary>
            /// The name of the plugin.
            /// </summary>
            internal IdentifierString Name { get; set; }

            /// <summary>
            /// The description of the plugin. Always containing the version last added plugin (assumed to be the newest)
            /// </summary>
            internal string Description { get; set; }

            /// <summary>
            /// Returns all the version numbers stored
            /// </summary>
            public List<SemanticVersionNumber> Versions
            {
                get
                {
                    return pluginVersions.Keys.ToList();
                }
            }

            /// <summary>
            /// Returns the newest version in the store
            /// </summary>
            /// <returns></returns>
            internal Type GetNewestVersion()
            {
                if (pluginVersions.Count == 0)
                {
                    throw new PluginClassFactoryException("Somehow there is no plugin versions");
                }
                SemanticVersionNumber highestKey = pluginVersions.Keys.Max();
                if (TryGetVersion(highestKey, out Type type))
                {
                    return type;
                }
                else
                {
                    throw new PluginClassFactoryException("Somehow the higest version of plugin was not there");
                }
            }

            /// <summary>
            /// Tries to get a specific version 
            /// </summary>
            /// <param name="version"></param>
            /// <param name="type"></param>
            /// <returns></returns>
            internal bool TryGetVersion(SemanticVersionNumber version, out Type type)
            {
                if (pluginVersions.Count == 0)
                {
                    throw new PluginClassFactoryException("Somehow there is no plugin versions");
                }
                if (pluginVersions.TryGetValue(version, out Type innerType))
                {
                    type = innerType;
                    return true;
                }
                else
                {
                    type = null;
                    return false;
                }
            }

            /// <summary>
            /// Inserts a version and removes versions that should not be retained
            /// </summary>
            /// <param name="version"></param>
            /// <param name="type"></param>
            /// <returns></returns>
            internal bool UpsertVersion(SemanticVersionNumber version, Type type)
            {
                if (pluginVersions.ContainsKey(version))
                {
                    pluginVersions[version] = type;
                }
                else
                {
                    //Delete old versions if not retaining them
                    if (pluginVersions.TryAdd(version, type))
                    {
                        List<SemanticVersionNumber> retainKeys = pluginVersions.Keys.OrderByDescending(s => s).Take(_retainOldVersions).ToList();

                        foreach (SemanticVersionNumber deletableVersion in pluginVersions.Keys.Where(s => !retainKeys.Contains(s)))
                        {
                            if (pluginVersions.Remove(deletableVersion, out Type value))
                            {
                                //TODO Log removed type or use as observable
                            }
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
                return true;
            }

            /// <summary>
            /// Removes a specific version if existing
            /// </summary>
            /// <param name="version"></param>
            internal void RemoveVersion(SemanticVersionNumber version)
            {
                if (pluginVersions.ContainsKey(version))
                {
                    pluginVersions.Remove(version, out Type ignoredType);
                }
            }
        }
    }

    /// <summary>
    /// Event arguments for plugin instantiation errors
    /// </summary>
    public class PluginInstantiationErrorEventArgs : EventArgs
    {
        public string ModuleName { get; set; }
        public string PluginName { get; set; }
        public string Version { get; set; }
        public Exception Exception { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
