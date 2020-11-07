using DevelApp.RuntimePluggableClassFactory.Interface;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DevelApp.RuntimePluggableClassFactory
{
    public class PluginClassFactory<T> where T : IPluginClass
    {
        public PluginClassFactory(int retainOldVersions)
        {
            _retainOldVersions = retainOldVersions;
            if (!typeof(T).IsInterface)
            {
                throw new PluginClassFactoryException("Generic type is not an interface as required");
            }
        }

        /// <summary>
        /// Stores the types for the factory
        /// </summary>
        private Dictionary<string, PluginClass> pluginClassStore = new Dictionary<string, PluginClass>();

        private int _retainOldVersions;

        /// <summary>
        /// Loads all assemblies from the pluginPath and returns all non-abstract classes implementing interface T
        /// </summary>
        /// <param name="pluginPath"></param>
        /// <returns></returns>
        public void LoadFromDirectory(string pluginPath)
        {
            if (!Directory.Exists(pluginPath))
            {
                return;
            }

            PluginLoadContext pluginLoadContext = new PluginLoadContext(pluginPath);

            // Load from each assembly in folder
            foreach (string fileName in Directory.GetFiles(pluginPath, "*.dll"))
            {
                //TODO check if assembly certificate is valid
                Assembly assembly = pluginLoadContext.LoadFromAssemblyPath(fileName);
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
                if (typeof(T).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                {
                    // Make an instance to ask for name
                    T instance = (T)Activator.CreateInstance(type);
                    if (pluginClassStore.TryGetValue(instance.Name, out PluginClass outPluginClass))
                    {
                        outPluginClass.InsertVersion(instance.Version, type);
                    }
                    else
                    {
                        PluginClass pluginClass = new PluginClass(_retainOldVersions, instance.Description, instance.Version, type);
                        pluginClassStore.Add(instance.Name, pluginClass);
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
        public T GetInstance(string name, int version)
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
            internal PluginClass(int retainOldVersions, string description, int version, Type type)
            {
                _retainOldVersions = retainOldVersions;
                Description = description;
                InsertVersion(version, type);
            }

            private Dictionary<int, Type> pluginVersions = new Dictionary<int, Type>();

            internal string Description { get; }

            internal Type GetNewestVersion()
            {
                if (pluginVersions.Count == 0)
                {
                    throw new PluginClassFactoryException("Somehow there is no plugin versions");
                }
                int highestKey = pluginVersions.Keys.Max();
                if (TryGetVersion(highestKey, out Type type))
                {
                    return type;
                }
                else
                {
                    throw new PluginClassFactoryException("Somehow the higest version of plugin was not there");
                }
            }
            internal bool TryGetVersion(int version, out Type type)
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

            internal void InsertVersion(int version, Type type)
            {
                if(pluginVersions.ContainsKey(version))
                {
                    pluginVersions[version] = type;
                }
                else
                {
                    //Delete old versions if not retaining them
                    pluginVersions.Add(version, type);
                    foreach(int deletableVersion in pluginVersions.Keys.Where(s=>s + _retainOldVersions < version))
                    {
                        pluginVersions.Remove(deletableVersion);
                    }
                }
            }
        }
    }
}
