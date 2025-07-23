using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory
{
    public interface IPluginLoader<T> where T: IPluginClass
    {
        /// <summary>
        /// Responsible for loading plugins to use in the plugin factory
        /// </summary>
        /// <param name="allowedPlugins"></param>
        /// <returns></returns>
        Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string Description, Type Type)>> LoadPluginsAsync(List<(NamespaceString ModuleName, IdentifierString Name, SemanticVersionNumber Version)> allowedPlugins);

        /// <summary>
        /// Lists all identified plugins
        /// </summary>
        /// <returns></returns>
        Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string Description, Type Type)>> ListAllPossiblePluginsAsync();

        /// <summary>
        /// Unloads a specific plugin assembly by path (TDS requirement)
        /// </summary>
        /// <param name="pluginPath">Path to the plugin to unload</param>
        /// <returns>True if unloaded successfully, false if not found or already unloaded</returns>
        bool UnloadPlugin(string pluginPath);

        /// <summary>
        /// Unloads all plugin assemblies (TDS requirement)
        /// </summary>
        void UnloadAllPlugins();
    }
}
