using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.RuntimePluggableClassFactory.SemanticVersioning;
using DevelApp.Utility.Model;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory
{
    public interface IPluginLoader<T> : IDisposable where T: IPluginClass
    {
        /// <summary>
        /// Responsible for loading plugins to use in the plugin factory
        /// </summary>
        /// <param name="allowedPlugins"></param>
        /// <returns></returns>
        Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>> LoadPluginsAsync(List<(NamespaceString ModuleName, IdentifierString Name, SemanticVersionNumber Version)> allowedPlugins);

        /// <summary>
        /// Lists all identified plugins
        /// </summary>
        /// <returns></returns>
        Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>> ListAllPossiblePluginsAsync();

        /// <summary>
        /// Lists all plugins matching a version range
        /// </summary>
        /// <param name="versionRange">Version range to match</param>
        /// <returns>Plugins matching the version range</returns>
        Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>> ListPluginsByVersionRangeAsync(VersionRange versionRange);

        /// <summary>
        /// Lists all plugins matching a specific module
        /// </summary>
        /// <param name="moduleName">Module name to filter by</param>
        /// <returns>Plugins from the specified module</returns>
        Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>> ListPluginsByModuleAsync(NamespaceString moduleName);

        /// <summary>
        /// Unloads a specific plugin assembly by path (TDS requirement)
        /// </summary>
        /// <param name="pluginPath">Path to the plugin to unload</param>
        /// <returns>True if unloaded successfully, false if not found or already unloaded</returns>
        bool UnloadPlugin(string? pluginPath);

        /// <summary>
        /// Unloads all plugin assemblies (TDS requirement)
        /// </summary>
        void UnloadAllPlugins();

        /// <summary>
        /// Event fired when plugin loading fails
        /// </summary>
        event EventHandler<PluginLoadingErrorEventArgs>? PluginLoadingFailed;

        /// <summary>
        /// Event fired when security validation fails
        /// </summary>
        event EventHandler<PluginSecurityValidationFailedEventArgs>? SecurityValidationFailed;
    }

    /// <summary>
    /// Event arguments for plugin loading errors
    /// </summary>
    public class PluginLoadingErrorEventArgs : EventArgs
    {
        /// <summary>
        /// Module name
        /// </summary>
        public NamespaceString? ModuleName { get; set; }

        /// <summary>
        /// Plugin name
        /// </summary>
        public IdentifierString? PluginName { get; set; }

        /// <summary>
        /// Version
        /// </summary>
        public SemanticVersionNumber? Version { get; set; }

        /// <summary>
        /// Error message
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Exception
        /// </summary>
        public Exception? Exception { get; set; }

        /// <summary>
        /// Timestamp
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Event arguments for security validation failures
    /// </summary>
    public class PluginSecurityValidationFailedEventArgs : EventArgs
    {
        /// <summary>
        /// Plugin file path
        /// </summary>
        public string? PluginPath { get; set; }

        /// <summary>
        /// Plugin folder
        /// </summary>
        public string? PluginFolder { get; set; }

        /// <summary>
        /// Security validation result
        /// </summary>
        public Security.PluginSecurityValidationResult? ValidationResult { get; set; }

        /// <summary>
        /// Timestamp
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
