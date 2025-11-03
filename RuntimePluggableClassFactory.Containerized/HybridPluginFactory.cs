using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces;
using DevelApp.RuntimePluggableClassFactory.Containerized.Implementations;
using DevelApp.Utility.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Containerized
{
    /// <summary>
    /// Factory that can use both traditional (in-process) and containerized plugins.
    /// Enables async module-based plugin loading from Kubernetes/remote sources and local directories.
    /// This allows the CRPCF to coexist with the existing RuntimePluggableClassFactory.
    /// </summary>
    /// <typeparam name="T">Plugin interface type</typeparam>
    public class HybridPluginFactory<T> where T : IPluginClass
    {
        private readonly PluginClassFactory<T>? _traditionalFactory;
        private readonly IContainerizedPluginOrchestrator? _containerizedOrchestrator;
        private readonly ILogger<HybridPluginFactory<T>> _logger;
        private readonly HybridPluginFactoryOptions _options;

        public HybridPluginFactory(
            PluginClassFactory<T>? traditionalFactory,
            IContainerizedPluginOrchestrator? containerizedOrchestrator,
            ILogger<HybridPluginFactory<T>> logger,
            HybridPluginFactoryOptions? options = null)
        {
            if (traditionalFactory == null && containerizedOrchestrator == null)
            {
                throw new ArgumentException("At least one factory type must be provided");
            }

            _traditionalFactory = traditionalFactory;
            _containerizedOrchestrator = containerizedOrchestrator;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? new HybridPluginFactoryOptions();
        }

        /// <summary>
        /// Gets a plugin instance, preferring the specified execution mode
        /// </summary>
        /// <param name="moduleName">Plugin module name</param>
        /// <param name="pluginName">Plugin name</param>
        /// <param name="version">Plugin version (optional)</param>
        /// <param name="executionMode">Preferred execution mode</param>
        /// <returns>Plugin instance or null if not found</returns>
        public async Task<T?> GetPluginAsync(
            NamespaceString moduleName,
            IdentifierString pluginName,
            SemanticVersionNumber? version = null,
            PluginExecutionMode executionMode = PluginExecutionMode.Auto)
        {
            _logger.LogDebug("Getting plugin {ModuleName}.{PluginName} with execution mode {ExecutionMode}",
                moduleName, pluginName, executionMode);

            try
            {
                switch (executionMode)
                {
                    case PluginExecutionMode.Traditional:
                        return await GetTraditionalPluginAsync(moduleName, pluginName, version);

                    case PluginExecutionMode.Containerized:
                        return await GetContainerizedPluginAsync(moduleName, pluginName, version);

                    case PluginExecutionMode.Auto:
                    default:
                        // Try preferred mode first, then fallback
                        if (_options.PreferContainerized)
                        {
                            var containerized = await GetContainerizedPluginAsync(moduleName, pluginName, version);
                            if (containerized != null) return containerized;

                            return await GetTraditionalPluginAsync(moduleName, pluginName, version);
                        }
                        else
                        {
                            var traditional = await GetTraditionalPluginAsync(moduleName, pluginName, version);
                            if (traditional != null) return traditional;

                            return await GetContainerizedPluginAsync(moduleName, pluginName, version);
                        }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting plugin {ModuleName}.{PluginName}", moduleName, pluginName);
                throw;
            }
        }

        /// <summary>
        /// Lists all available plugins from both traditional and containerized sources
        /// </summary>
        /// <returns>Available plugins with their execution modes</returns>
        public async Task<IEnumerable<PluginInfo>> ListAvailablePluginsAsync()
        {
            var plugins = new List<PluginInfo>();

            try
            {
                // Get traditional plugins
                if (_traditionalFactory != null)
                {
                    var traditionalPlugins = await _traditionalFactory.PluginLoader.ListAllPossiblePluginsAsync();
                    plugins.AddRange(traditionalPlugins.Select(p => new PluginInfo
                    {
                        ModuleName = p.ModuleName,
                        PluginName = p.PluginName,
                        Version = p.Version,
                        Description = p.Description,
                        ExecutionMode = PluginExecutionMode.Traditional,
                        Type = p.Type
                    }));
                }

                // Get containerized plugins
                if (_containerizedOrchestrator != null)
                {
                    var containerizedPlugins = await _containerizedOrchestrator.ListPluginsAsync();
                    plugins.AddRange(containerizedPlugins.Select(p => new PluginInfo
                    {
                        ModuleName = p.PluginId.Namespace,
                        PluginName = p.PluginId.Name,
                        Version = p.PluginId.Version,
                        Description = p.Description,
                        ExecutionMode = PluginExecutionMode.Containerized,
                        ContainerInfo = p.ContainerInfo
                    }));
                }

                _logger.LogInformation("Found {Count} available plugins ({TraditionalCount} traditional, {ContainerizedCount} containerized)",
                    plugins.Count,
                    plugins.Count(p => p.ExecutionMode == PluginExecutionMode.Traditional),
                    plugins.Count(p => p.ExecutionMode == PluginExecutionMode.Containerized));

                return plugins;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing available plugins");
                throw;
            }
        }

        /// <summary>
        /// Deploys a NuGet package as a containerized plugin
        /// </summary>
        /// <param name="request">Deployment request</param>
        /// <returns>Deployment result</returns>
        public async Task<PluginDeploymentResult> DeployContainerizedPluginAsync(PluginDeploymentRequest request)
        {
            if (_containerizedOrchestrator == null)
            {
                throw new InvalidOperationException("Containerized orchestrator not available");
            }

            _logger.LogInformation("Deploying containerized plugin to platform {Platform}", request.TargetPlatform);

            try
            {
                return await _containerizedOrchestrator.DeployPluginAsync(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deploying containerized plugin");
                throw;
            }
        }

        /// <summary>
        /// Allows a plugin for traditional loading
        /// </summary>
        public void AllowTraditionalPlugin(NamespaceString moduleName, IdentifierString pluginName, SemanticVersionNumber version)
        {
            if (_traditionalFactory == null)
            {
                throw new InvalidOperationException("Traditional factory not available");
            }

            _traditionalFactory.AllowPlugin(moduleName, pluginName, version);
            _logger.LogDebug("Allowed traditional plugin {ModuleName}.{PluginName}@{Version}", moduleName, pluginName, version);
        }

        /// <summary>
        /// Refreshes traditional plugins
        /// </summary>
        public async Task<(bool Success, int Count)> RefreshTraditionalPluginsAsync()
        {
            if (_traditionalFactory == null)
            {
                return (false, 0);
            }

            try
            {
                return await _traditionalFactory.RefreshPluginsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing traditional plugins");
                throw;
            }
        }

        private Task<T?> GetTraditionalPluginAsync(NamespaceString moduleName, IdentifierString pluginName, SemanticVersionNumber? version)
        {
            if (_traditionalFactory == null)
            {
                _logger.LogDebug("Traditional factory not available");
                return Task.FromResult<T?>(default);
            }

            try
            {
                // For traditional plugins, we use the synchronous method
                var plugin = version != null 
                    ? _traditionalFactory.GetInstance(moduleName, pluginName, version)
                    : _traditionalFactory.GetInstance(moduleName, pluginName);

                if (plugin != null)
                {
                    _logger.LogDebug("Found traditional plugin {ModuleName}.{PluginName}", moduleName, pluginName);
                }

                return Task.FromResult(plugin);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Traditional plugin {ModuleName}.{PluginName} not found or failed to load", 
                    moduleName, pluginName);
                return Task.FromResult<T?>(default);
            }
        }

        private async Task<T?> GetContainerizedPluginAsync(NamespaceString moduleName, IdentifierString pluginName, SemanticVersionNumber? version)
        {
            if (_containerizedOrchestrator == null)
            {
                _logger.LogDebug("Containerized orchestrator not available");
                return default;
            }

            try
            {
                var pluginId = new PluginIdentifier
                {
                    Namespace = moduleName,
                    Name = pluginName,
                    Version = version ?? new SemanticVersionNumber(0, 0, 0) // Will match latest if version not specified
                };

                var pluginInfo = await _containerizedOrchestrator.GetPluginInfoAsync(pluginId);
                if (pluginInfo != null)
                {
                    var proxy = ContainerizedPluginProxyFactory.Create<T>(_containerizedOrchestrator, pluginInfo);
                    _logger.LogDebug("Found containerized plugin {ModuleName}.{PluginName}", moduleName, pluginName);
                    // Since the proxy implements IPluginClass but T might be more specific, we need to handle this carefully
                    if (proxy is T typedProxy)
                    {
                        return typedProxy;
                    }
                    throw new InvalidOperationException($"Containerized plugin proxy cannot be cast to {typeof(T).Name}");
                }

                return default;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Containerized plugin {ModuleName}.{PluginName} not found or failed to load",
                    moduleName, pluginName);
                return default;
            }
        }
    }

    /// <summary>
    /// Plugin execution modes
    /// </summary>
    public enum PluginExecutionMode
    {
        /// <summary>
        /// Automatically choose the best available option
        /// </summary>
        Auto,

        /// <summary>
        /// Use traditional in-process plugin loading
        /// </summary>
        Traditional,

        /// <summary>
        /// Use containerized plugin execution
        /// </summary>
        Containerized
    }

    /// <summary>
    /// Plugin information including execution mode
    /// </summary>
    public class PluginInfo
    {
        public NamespaceString ModuleName { get; set; } = new();
        public IdentifierString PluginName { get; set; } = new();
        public SemanticVersionNumber Version { get; set; } = new(1, 0, 0);
        public string Description { get; set; } = string.Empty;
        public PluginExecutionMode ExecutionMode { get; set; }
        public Type? Type { get; set; }
        public ContainerInfo? ContainerInfo { get; set; }
    }

    /// <summary>
    /// Configuration options for hybrid plugin factory
    /// </summary>
    public class HybridPluginFactoryOptions
    {
        /// <summary>
        /// Whether to prefer containerized plugins when both are available
        /// </summary>
        public bool PreferContainerized { get; set; } = true;

        /// <summary>
        /// Whether to automatically deploy containerized plugins if they are not found
        /// </summary>
        public bool AutoDeployContainerized { get; set; } = false;

        /// <summary>
        /// Default deployment configuration for auto-deployed plugins
        /// </summary>
        public DeploymentConfiguration? DefaultDeploymentConfig { get; set; }
    }
}