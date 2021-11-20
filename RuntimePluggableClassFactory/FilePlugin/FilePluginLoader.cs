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
        public IEnumerable<Type> LoadPluginsAsync(List<(NamespaceString ModuleName, IdentifierString Name, SemanticVersionNumber Version)> allowedPlugins)
        {

            //Isolate plugins from other parts of the program
            PluginLoadContext pluginLoadContext = new PluginLoadContext(_pluginPathUri.AbsolutePath);

            // Load from each assembly in folder
            foreach (string fileName in Directory.GetFiles(_pluginPathUri.AbsolutePath, "*.dll", SearchOption.AllDirectories))
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
                    yield return identifiedType;
                }
            }
        }

        /// <summary>
        /// Return all the non-abstract classes implementing interface T
        /// </summary>
        /// <param name="assembly"></param>
        /// <returns></returns>
        public IEnumerable<Type> LoadFromAssembly(Assembly assembly)
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
        public IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version)> ListAllPossiblePlugins()
        {
            throw new NotImplementedException();
        }
    }
}
