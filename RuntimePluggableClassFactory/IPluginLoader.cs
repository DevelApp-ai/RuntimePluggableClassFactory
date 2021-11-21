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
    }
}
