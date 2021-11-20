using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory
{
    public interface IPluginLoader
    {
        /// <summary>
        /// Responsible for loading plugins to use in the plugin factory
        /// </summary>
        /// <param name="allowedPlugins"></param>
        /// <returns></returns>
        IEnumerable<Type> LoadPluginsAsync(List<(NamespaceString ModuleName, IdentifierString Name, SemanticVersionNumber Version)> allowedPlugins);

        /// <summary>
        /// Lists all identified plugins
        /// </summary>
        /// <returns></returns>
        IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version)> ListAllPossiblePlugins();
    }
}
