using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces;
using DevelApp.Utility.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Containerized.Implementations
{
    /// <summary>
    /// Plugin loader that uses containerized plugins via CRPCF
    /// This allows the traditional PluginClassFactory to work with containerized plugins
    /// </summary>
    /// <typeparam name="T">Plugin interface type</typeparam>
    public class ContainerizedPluginLoader<T> : IPluginLoader<T> where T : IPluginClass
    {
        private readonly IContainerizedPluginOrchestrator _orchestrator;
        private readonly ILogger<ContainerizedPluginLoader<T>> _logger;
        private readonly ContainerizedPluginLoaderOptions _options;

        public ContainerizedPluginLoader(
            IContainerizedPluginOrchestrator orchestrator,
            ILogger<ContainerizedPluginLoader<T>> logger,
            ContainerizedPluginLoaderOptions? options = null)
        {
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? new ContainerizedPluginLoaderOptions();
        }

        /// <summary>
        /// Loads plugins that are already deployed as containers
        /// </summary>
        public async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string Description, Type Type)>> LoadPluginsAsync(
            List<(NamespaceString ModuleName, IdentifierString Name, SemanticVersionNumber Version)> allowedPlugins)
        {
            try
            {
                _logger.LogInformation("Loading {Count} containerized plugins", allowedPlugins.Count);

                var result = new List<(NamespaceString, IdentifierString, SemanticVersionNumber, string, Type)>();

                // Get all deployed plugins
                var deployedPlugins = await _orchestrator.ListPluginsAsync(new PluginListOptions
                {
                    StatusFilter = PluginDeploymentStatus.Deployed
                });

                foreach (var allowedPlugin in allowedPlugins)
                {
                    var deployedPlugin = deployedPlugins.FirstOrDefault(p =>
                        p.PluginId.Namespace.Equals(allowedPlugin.ModuleName) &&
                        p.PluginId.Name.Equals(allowedPlugin.Name) &&
                        p.PluginId.Version.Equals(allowedPlugin.Version));

                    if (deployedPlugin != null)
                    {
                        // Create a proxy type for the containerized plugin
                        var proxyType = typeof(ContainerizedPluginProxy<>).MakeGenericType(typeof(T));
                        
                        result.Add((
                            allowedPlugin.ModuleName,
                            allowedPlugin.Name,
                            allowedPlugin.Version,
                            deployedPlugin.Description,
                            proxyType
                        ));

                        _logger.LogDebug("Found containerized plugin: {PluginId}", deployedPlugin.PluginId);
                    }
                    else
                    {
                        _logger.LogWarning("Plugin {ModuleName}.{PluginName}@{Version} not found in deployed containers",
                            allowedPlugin.ModuleName, allowedPlugin.Name, allowedPlugin.Version);
                    }
                }

                _logger.LogInformation("Loaded {Count} containerized plugins", result.Count);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading containerized plugins");
                throw;
            }
        }

        /// <summary>
        /// Lists all possible containerized plugins that are deployed
        /// </summary>
        public async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string Description, Type Type)>> ListAllPossiblePluginsAsync()
        {
            try
            {
                _logger.LogInformation("Listing all deployed containerized plugins");

                var deployedPlugins = await _orchestrator.ListPluginsAsync();
                var result = new List<(NamespaceString, IdentifierString, SemanticVersionNumber, string, Type)>();

                foreach (var plugin in deployedPlugins)
                {
                    var proxyType = typeof(ContainerizedPluginProxy<>).MakeGenericType(typeof(T));
                    
                    result.Add((
                        plugin.PluginId.Namespace,
                        plugin.PluginId.Name,
                        plugin.PluginId.Version,
                        plugin.Description,
                        proxyType
                    ));
                }

                _logger.LogInformation("Found {Count} deployed containerized plugins", result.Count);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing containerized plugins");
                throw;
            }
        }

        /// <summary>
        /// Unloads a specific plugin (stops and removes the container)
        /// </summary>
        public bool UnloadPlugin(string pluginPath)
        {
            try
            {
                // For containerized plugins, the pluginPath represents the plugin identifier
                if (!TryParsePluginIdentifier(pluginPath, out var pluginId))
                {
                    _logger.LogWarning("Invalid plugin path format: {PluginPath}", pluginPath);
                    return false;
                }

                // Undeploy the plugin asynchronously (fire and forget for compatibility with sync interface)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _orchestrator.UndeployPluginAsync(pluginId);
                        _logger.LogInformation("Successfully undeployed containerized plugin: {PluginId}", pluginId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error undeploying containerized plugin: {PluginId}", pluginId);
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unloading containerized plugin: {PluginPath}", pluginPath);
                return false;
            }
        }

        /// <summary>
        /// Unloads all containerized plugins
        /// </summary>
        public void UnloadAllPlugins()
        {
            try
            {
                _logger.LogInformation("Unloading all containerized plugins");

                // Undeploy all plugins asynchronously (fire and forget for compatibility with sync interface)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var deployedPlugins = await _orchestrator.ListPluginsAsync();
                        var undeployTasks = deployedPlugins.Select(p => _orchestrator.UndeployPluginAsync(p.PluginId));
                        await Task.WhenAll(undeployTasks);
                        _logger.LogInformation("Successfully undeployed all containerized plugins");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error undeploying containerized plugins");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unloading all containerized plugins");
            }
        }

        private bool TryParsePluginIdentifier(string pluginPath, out PluginIdentifier pluginId)
        {
            pluginId = new PluginIdentifier();

            try
            {
                // Expected format: "namespace.name@version"
                var parts = pluginPath.Split('@');
                if (parts.Length != 2)
                    return false;

                var nameParts = parts[0].Split('.');
                if (nameParts.Length < 2)
                    return false;

                pluginId.Namespace = new NamespaceString(string.Join(".", nameParts.Take(nameParts.Length - 1)));
                pluginId.Name = new IdentifierString(nameParts.Last());
                // Parse the version string - assuming it's in the format "major.minor.patch"
                var versionParts = parts[1].Split('.');
                var major = int.Parse(versionParts[0]);
                var minor = versionParts.Length > 1 ? int.Parse(versionParts[1]) : 0;
                var patch = versionParts.Length > 2 ? int.Parse(versionParts[2]) : 0;
                pluginId.Version = new SemanticVersionNumber(major, minor, patch);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Configuration options for the containerized plugin loader
    /// </summary>
    public class ContainerizedPluginLoaderOptions
    {
        /// <summary>
        /// Timeout for container operations
        /// </summary>
        public TimeSpan ContainerOperationTimeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Whether to automatically deploy plugins if they are not found
        /// </summary>
        public bool AutoDeploy { get; set; } = false;

        /// <summary>
        /// Default deployment configuration for auto-deployed plugins
        /// </summary>
        public DeploymentConfiguration? DefaultDeploymentConfig { get; set; }
    }
}