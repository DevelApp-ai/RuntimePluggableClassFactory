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
        /// <summary>
        /// The configured plugin loader
        /// </summary>
        public IPluginLoader PluginLoader { get; }

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

        public IEnumerable<(NamespaceString moduleName, IdentifierString pluginName, SemanticVersionNumber version)> GetPossiblePlugins()
        {
            return PluginLoader.ListAllPossiblePlugins();
        }

        public PluginClassFactory(IPluginLoader pluginLoader, int retainOldVersions = 1)
        {
            PluginLoader = pluginLoader;
            _retainOldVersions = retainOldVersions;
            if (!typeof(T).IsInterface)
            {
                throw new PluginClassFactoryException("Generic type T is not an interface as required");
            }
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

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task<(bool Success,int Count)> RefreshPluginsAsync()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            try
            {
                IEnumerable<Type> pluginTypes = PluginLoader.LoadPluginsAsync(_allowedPlugins);
                foreach (Type pluginType in pluginTypes)
                {
                    // Make an instance to ask for name
                    object instanceObject = Activator.CreateInstance(pluginType);

                    //Assembly debug
                    Assembly pluginAssemblyT = typeof(T).Assembly;
                    Assembly pluginAssemblyType = pluginType.Assembly;
                    Assembly pluginInterfaceAssembly = typeof(IPluginClass).Assembly;

                    System.Runtime.Loader.AssemblyLoadContext pluginAssemblyTLoader = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(T).Assembly);
                    System.Runtime.Loader.AssemblyLoadContext pluginAssemblyTypeLoader = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(pluginType.Assembly);
                    System.Runtime.Loader.AssemblyLoadContext pluginInterfaceAssemblyLoader = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(IPluginClass).Assembly);
                    //End Assembly debug

                    T instance = (T)instanceObject;
                    if (pluginClassStore.TryGetValue((instance.Module.ToString(), instance.Name), out PluginClass outPluginClass))
                    {
                        outPluginClass.UpsertVersion(instance.Version, pluginType);
                        outPluginClass.Description = instance.Description;
                        outPluginClass.Name = instance.Name;
                        outPluginClass.ModuleName = instance.Module.ToString();
                    }
                    else
                    {
                        PluginClass pluginClass = new PluginClass(_retainOldVersions, instance.Module.ToString(), instance.Name, instance.Description, instance.Version, pluginType);
                        pluginClassStore.TryAdd((instance.Module.ToString(), instance.Name), pluginClass);
                    }
                }
                return (true, pluginTypes.Count());
            }
            catch (Exception ex)
            {
                //Log.Error(ex, "RefreshPluginsAsync failed");
                return (false, 0);
            }
        }

        /// <summary>
        /// Returns an instance of the newest version of the named class abiding interface T
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public T GetInstance(NamespaceString moduleName, IdentifierString name)
        {
            if (pluginClassStore.TryGetValue((moduleName, name), out PluginClass pluginClass))
            {
                return (T)Activator.CreateInstance(pluginClass.GetNewestVersion());
            }
            else
            {
                return default;
            }
        }

        /// <summary>
        /// Returns an instance of the newest version of the named class abiding interface T
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public T GetInstance(NamespaceString moduleName, IdentifierString name, SemanticVersionNumber version)
        {
            if (pluginClassStore.TryGetValue((moduleName, name), out PluginClass pluginClass))
            {
                if (pluginClass.TryGetVersion(version, out Type type))
                {
                    return (T)Activator.CreateInstance(type);
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
}
