using DevelApp.RuntimePluggableClassFactory;
using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces;
using DevelApp.RuntimePluggableClassFactory.SemanticVersioning;
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
        /// Event fired when plugin loading fails
        /// </summary>
        public event EventHandler<PluginLoadingErrorEventArgs>? PluginLoadingFailed;

        /// <summary>
        /// Event fired when security validation fails
        /// </summary>
        /// <remarks>
        /// The containerized loader performs no security validation itself (validation happens
        /// during deployment via ISignedPackageManager), so this event is never raised here;
        /// it is declared to satisfy the IPluginLoader contract for consumers that subscribe.
        /// </remarks>
#pragma warning disable CS0067 // The event is never raised within this loader
        public event EventHandler<PluginSecurityValidationFailedEventArgs>? SecurityValidationFailed;
#pragma warning restore CS0067

        /// <summary>
        /// Loads plugins that are already deployed as containers
        /// </summary>
        public async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>> LoadPluginsAsync(
            List<(NamespaceString ModuleName, IdentifierString Name, SemanticVersionNumber Version)> allowedPlugins)
        {
            try
            {
                _logger.LogInformation("Loading {Count} containerized plugins", allowedPlugins.Count);

                var result = new List<(NamespaceString, IdentifierString, SemanticVersionNumber, string?, Type)>();

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

                        PluginLoadingFailed?.Invoke(this, new PluginLoadingErrorEventArgs
                        {
                            ModuleName = allowedPlugin.ModuleName,
                            PluginName = allowedPlugin.Name,
                            Version = allowedPlugin.Version,
                            Message = $"Plugin {allowedPlugin.ModuleName}.{allowedPlugin.Name}@{allowedPlugin.Version} not found in deployed containers"
                        });
                    }
                }

                _logger.LogInformation("Loaded {Count} containerized plugins", result.Count);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading containerized plugins");

                PluginLoadingFailed?.Invoke(this, new PluginLoadingErrorEventArgs
                {
                    Message = "Error loading containerized plugins",
                    Exception = ex
                });

                throw;
            }
        }

        /// <summary>
        /// Lists all possible containerized plugins that are deployed
        /// </summary>
        public async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>> ListAllPossiblePluginsAsync()
        {
            try
            {
                _logger.LogInformation("Listing all deployed containerized plugins");

                var deployedPlugins = await _orchestrator.ListPluginsAsync();
                var result = new List<(NamespaceString, IdentifierString, SemanticVersionNumber, string?, Type)>();

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
        /// Lists all deployed containerized plugins matching a version range
        /// </summary>
        /// <param name="versionRange">Version range to match</param>
        /// <returns>Deployed plugins matching the version range</returns>
        public async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>> ListPluginsByVersionRangeAsync(VersionRange versionRange)
        {
            try
            {
                _logger.LogInformation("Listing deployed containerized plugins in version range {VersionRange}", versionRange);

                var deployedPlugins = await _orchestrator.ListPluginsAsync();
                var result = new List<(NamespaceString, IdentifierString, SemanticVersionNumber, string?, Type)>();

                foreach (var plugin in deployedPlugins)
                {
                    if (!versionRange.Contains(plugin.PluginId.Version))
                    {
                        continue;
                    }

                    var proxyType = typeof(ContainerizedPluginProxy<>).MakeGenericType(typeof(T));

                    result.Add((
                        plugin.PluginId.Namespace,
                        plugin.PluginId.Name,
                        plugin.PluginId.Version,
                        plugin.Description,
                        proxyType
                    ));
                }

                _logger.LogInformation("Found {Count} deployed containerized plugins in version range {VersionRange}",
                    result.Count, versionRange);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing containerized plugins in version range {VersionRange}", versionRange);
                throw;
            }
        }

        /// <summary>
        /// Lists all deployed containerized plugins matching a specific module
        /// </summary>
        /// <param name="moduleName">Module name to filter by</param>
        /// <returns>Deployed plugins from the specified module</returns>
        public async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string? Description, Type Type)>> ListPluginsByModuleAsync(NamespaceString moduleName)
        {
            try
            {
                _logger.LogInformation("Listing deployed containerized plugins for module {ModuleName}", moduleName);

                var deployedPlugins = await _orchestrator.ListPluginsAsync();
                var result = new List<(NamespaceString, IdentifierString, SemanticVersionNumber, string?, Type)>();

                foreach (var plugin in deployedPlugins)
                {
                    if (!plugin.PluginId.Namespace.Equals(moduleName))
                    {
                        continue;
                    }

                    var proxyType = typeof(ContainerizedPluginProxy<>).MakeGenericType(typeof(T));

                    result.Add((
                        plugin.PluginId.Namespace,
                        plugin.PluginId.Name,
                        plugin.PluginId.Version,
                        plugin.Description,
                        proxyType
                    ));
                }

                _logger.LogInformation("Found {Count} deployed containerized plugins for module {ModuleName}",
                    result.Count, moduleName);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing containerized plugins for module {ModuleName}", moduleName);
                throw;
            }
        }

        /// <summary>
        /// Unloads a specific plugin (stops and removes the container)
        /// </summary>
        public bool UnloadPlugin(string? pluginPath)
        {
            try
            {
                // For containerized plugins, the pluginPath represents the plugin identifier
                if (pluginPath == null || !TryParsePluginIdentifier(pluginPath, out var pluginId))
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

        /// <summary>
        /// Disposes the loader and releases the underlying orchestrator if it is disposable
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the loader
        /// </summary>
        /// <param name="disposing">True when disposing explicitly</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && _orchestrator is IDisposable disposableOrchestrator)
            {
                disposableOrchestrator.Dispose();
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