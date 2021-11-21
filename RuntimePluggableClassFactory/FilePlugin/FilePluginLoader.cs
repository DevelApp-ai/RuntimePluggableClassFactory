using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.FilePlugin
{
    public class FilePluginLoader:IPluginLoader
    {
        public FilePluginLoader(Uri pluginPathUri)
        {
            PluginPathUri = pluginPathUri;
        }

        private Uri _pluginPathUri;

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
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string Description, Type Type)>> LoadUnfilteredPluginsAsync()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            List<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string Description, Type Type)> typeList = new List<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string Description, Type Type)>();

            // Isolate plugin context per subfolder
            foreach (string pluginSubfolder in Directory.GetDirectories(_pluginPathUri.AbsolutePath))
            {
                //Isolate plugins from other parts of the program
                PluginLoadContext pluginLoadContext = new PluginLoadContext(pluginSubfolder);

                // Load from each assembly in folder
                foreach (string fileName in Directory.GetFiles(pluginSubfolder, "*.dll", SearchOption.AllDirectories))
                {
                    try
                    {

                        //TODO check if assembly certificate is valid to improve security

                        Assembly assembly = pluginLoadContext.LoadFromAssemblyPath(fileName);

                        //Get assembly from already loaded Default AssemblyLoadContext if possible so isolation is not useful
                        Assembly defaultAssembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(x => x.FullName == assembly.FullName);
                        if (defaultAssembly != null)
                        {
                            assembly = defaultAssembly;
                        }

                        foreach (Type identifiedType in LoadFromAssembly(assembly))
                        {
                            //Assembly debug
                            //Assembly pluginAssemblyT = typeof(T).Assembly;
                            //Assembly pluginAssemblyType = pluginType.Assembly;
                            //Assembly pluginInterfaceAssembly = typeof(IPluginClass).Assembly;

                            //System.Runtime.Loader.AssemblyLoadContext pluginAssemblyTLoader = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(T).Assembly);
                            //System.Runtime.Loader.AssemblyLoadContext pluginAssemblyTypeLoader = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(pluginType.Assembly);
                            //System.Runtime.Loader.AssemblyLoadContext pluginInterfaceAssemblyLoader = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(typeof(IPluginClass).Assembly);
                            //End Assembly debug


                            //TODO filter for T interface
                            //TODO Here be dragons. We create an instance before the instance is accepted
                            //TODO solution assembly embedded file containing ModuleName, PluginName, Version, Description ?
                            IPluginClass identifiedTypeInstance = Activator.CreateInstance(identifiedType) as IPluginClass;
                            if (identifiedTypeInstance != null)
                            {
                                typeList.Add((identifiedTypeInstance.Module, identifiedTypeInstance.Name, identifiedTypeInstance.Version, identifiedTypeInstance.Description, identifiedType));
                            }
                        }
                    }
                    catch (Exception)
                    {
                        //TODO add logging reject reason
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
    }
}
