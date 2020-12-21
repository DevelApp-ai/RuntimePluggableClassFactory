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

namespace DevelApp.RuntimePluggableClassFactory
{
    //TODO Link pluginclass version to plugin files
    //TODO Example project to make single dll output with references internalized Fody ? Otherwise compressed zip deployment with definition file
    public class PluginClassFactory<T> where T : IPluginClass
    {
        public PluginClassFactory(int retainOldVersions)
        {
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
        public IEnumerable<(string Name, string Description, List<SemanticVersionNumber> Versions)> GetAllInstanceNamesDescriptionsAndVersions()
        {
            foreach(PluginClass pluginClass in pluginClassStore.Values)
            {
                yield return (Name: pluginClass.Name, Description: pluginClass.Description, Versions: pluginClass.Versions);
            }
        }

        /// <summary>
        /// Stores the types for the factory
        /// </summary>
        private ConcurrentDictionary<string, PluginClass> pluginClassStore = new ConcurrentDictionary<string, PluginClass>();

        private int _retainOldVersions;

        /// <summary>
        /// Loads all assemblies from the pluginPath and returns all non-abstract classes implementing interface T
        /// </summary>
        /// <param name="pluginPathUri"></param>
        /// <returns></returns>
        public void LoadFromDirectory(Uri pluginPathUri)
        {
            if(!pluginPathUri.IsAbsoluteUri)
            {
                throw new PluginClassFactoryException($"The supplied uri in {nameof(pluginPathUri)} is not an absolute uri but is required to be");
            }
            if (!Directory.Exists(pluginPathUri.AbsolutePath))
            {
                throw new PluginClassFactoryException($"Directory {pluginPathUri.AbsolutePath} does not exist");
            }

            //Isolate plugins from other parts of the program
            PluginLoadContext pluginLoadContext = new PluginLoadContext(pluginPathUri.AbsolutePath);

            // Load from each assembly in folder
            foreach (string fileName in Directory.GetFiles(pluginPathUri.AbsolutePath, "*.dll", SearchOption.AllDirectories))
            {
                //TODO check if assembly certificate is valid to improve security

                Assembly assembly = pluginLoadContext.LoadFromAssemblyPath(fileName);

                //Get assembly from already loaded Default AssemblyLoadContext if possible so isolation is not useful
                Assembly defaultAssembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(x => x.FullName == assembly.FullName);
                if(defaultAssembly != null)
                {
                    assembly = defaultAssembly;
                }

                LoadFromAssembly(assembly);
            }
        }

        /// <summary>
        /// Return all the non-abstract classes implementing interface T
        /// </summary>
        /// <param name="assembly"></param>
        /// <returns></returns>
        public void LoadFromAssembly(Assembly assembly)
        {
            // Return each T for IPluginClass
            foreach (Type type in assembly.GetTypes())
            {
                //TODO examine if there is a need to exclude interface dll to avoid problem with IsAssignableFrom falsly returning false
                //Workaround if needed https://makolyte.com/csharp-generic-plugin-loader/
                if (typeof(IPluginClass).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                {
                    // Make an instance to ask for name
                    object instanceObject = Activator.CreateInstance(type);

                    //Assembly debug
                    Assembly pluginAssemblyT = typeof(T).Assembly;
                    Assembly pluginAssemblyType = type.Assembly;
                    Assembly pluginInterfaceAssembly = typeof(IPluginClass).Assembly;

                    System.Runtime.Loader.AssemblyLoadContext pluginAssemblyTLoader = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(T).Assembly);
                    System.Runtime.Loader.AssemblyLoadContext pluginAssemblyTypeLoader = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(type.Assembly);
                    System.Runtime.Loader.AssemblyLoadContext pluginInterfaceAssemblyLoader = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(IPluginClass).Assembly);
                    //End Assembly debug

                    T instance = (T)instanceObject;
                    if (pluginClassStore.TryGetValue(instance.Name, out PluginClass outPluginClass))
                    {
                        outPluginClass.UpsertVersion(instance.Version, type);
                        outPluginClass.Description = instance.Description;
                        outPluginClass.Name = instance.Name;
                    }
                    else
                    {
                        PluginClass pluginClass = new PluginClass(_retainOldVersions, instance.Name, instance.Description, instance.Version, type);
                        pluginClassStore.TryAdd(instance.Name, pluginClass);
                    }
                }
            }
        }

        /// <summary>
        /// Returns an instance of the newest version of the named class abiding interface T
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public T GetInstance(string name)
        {
            if (pluginClassStore.TryGetValue(name, out PluginClass pluginClass))
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
        public T GetInstance(string name, SemanticVersionNumber version)
        {
            if (pluginClassStore.TryGetValue(name, out PluginClass pluginClass))
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

        private sealed class PluginClass
        {
            private int _retainOldVersions;
            internal PluginClass(int retainOldVersions, string name, string description, SemanticVersionNumber version, Type type)
            {
                _retainOldVersions = retainOldVersions;
                Name = name;
                Description = description;
                UpsertVersion(version, type);
            }

            private ConcurrentDictionary<SemanticVersionNumber, Type> pluginVersions = new ConcurrentDictionary<SemanticVersionNumber, Type>();

            /// <summary>
            /// The name of the plugin.
            /// </summary>
            internal string Name { get; set; }

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
                if(pluginVersions.ContainsKey(version))
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
        }
    }
}
