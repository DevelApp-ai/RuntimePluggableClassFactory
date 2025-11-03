using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces;
using DevelApp.Utility.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.Containerized.Implementations
{
    /// <summary>
    /// Proxy that bridges traditional plugin interfaces with containerized execution
    /// This allows existing code to work with containerized plugins transparently
    /// </summary>
    /// <typeparam name="T">Plugin interface type</typeparam>
    public class ContainerizedPluginProxy<T> : IPluginClass where T : IPluginClass
    {
        private readonly IContainerizedPluginOrchestrator _orchestrator;
        private readonly PluginIdentifier _pluginId;
        private readonly ILogger<ContainerizedPluginProxy<T>>? _logger;
        private readonly ContainerizedPluginInfo _pluginInfo;

        public ContainerizedPluginProxy(
            IContainerizedPluginOrchestrator orchestrator,
            PluginIdentifier pluginId,
            ContainerizedPluginInfo pluginInfo,
            ILogger<ContainerizedPluginProxy<T>>? logger = null)
        {
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _pluginId = pluginId ?? throw new ArgumentNullException(nameof(pluginId));
            _pluginInfo = pluginInfo ?? throw new ArgumentNullException(nameof(pluginInfo));
            _logger = logger;
        }

        /// <summary>
        /// Plugin name from the containerized plugin
        /// </summary>
        public IdentifierString Name => _pluginId.Name;

        /// <summary>
        /// Plugin module from the containerized plugin
        /// </summary>
        public NamespaceString Module => _pluginId.Namespace;

        /// <summary>
        /// Plugin description from the containerized plugin
        /// </summary>
        public string Description => _pluginInfo.Description;

        /// <summary>
        /// Plugin version from the containerized plugin
        /// </summary>
        public SemanticVersionNumber Version => _pluginId.Version;

        /// <summary>
        /// Executes the plugin in the container (compatibility method for typed interface)
        /// </summary>
        /// <param name="context">Execution context</param>
        /// <param name="input">Input data</param>
        /// <returns>Execution result</returns>
        public Interface.PluginExecutionResult<object> ExecuteTyped(IPluginExecutionContext context, object input)
        {
            try
            {
                return ExecuteAsync(context, input).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error executing containerized plugin {PluginId}", _pluginId);
                return Interface.PluginExecutionResult<object>.CreateFailure($"Plugin execution failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Asynchronously executes the plugin in the container
        /// </summary>
        /// <param name="context">Execution context</param>
        /// <param name="input">Input data</param>
        /// <returns>Execution result</returns>
        public async Task<Interface.PluginExecutionResult<object>> ExecuteAsync(IPluginExecutionContext context, object input)
        {
            try
            {
                _logger?.LogDebug("Executing containerized plugin {PluginId}", _pluginId);

                // Serialize input data
                string inputJson;
                if (input is string stringInput)
                {
                    inputJson = stringInput;
                }
                else
                {
                    inputJson = JsonSerializer.Serialize(input);
                }

                // Create execution request
                var executionRequest = new ContainerizedPluginExecutionRequest
                {
                    PluginId = _pluginId,
                    InputData = inputJson,
                    Timeout = TimeSpan.FromMinutes(5), // Default timeout
                    Configuration = new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["context.properties"] = context.Properties
                    }
                };

                // Execute in container
                var result = await _orchestrator.ExecutePluginAsync(executionRequest, context.CancellationToken);

                if (result.Success)
                {
                    _logger?.LogDebug("Successfully executed containerized plugin {PluginId}", _pluginId);
                    
                    // Try to deserialize the result back to an object
                    object? output = null;
                    if (result.Data != null && !string.IsNullOrEmpty(result.Data.ToString()))
                    {
                        try
                        {
                            output = JsonSerializer.Deserialize<object>(result.Data.ToString()!);
                        }
                        catch (Exception ex)
                        {
                            // If deserialization fails, log and return as string
                            _logger?.LogWarning(ex, "Failed to deserialize plugin output, returning as string");
                            output = result.Data.ToString();
                        }
                    }

                    return Interface.PluginExecutionResult<object>.CreateSuccess(output!);
                }
                else
                {
                    _logger?.LogWarning("Containerized plugin {PluginId} execution failed: {Error}", 
                        _pluginId, result.ErrorMessage);
                    
                    return Interface.PluginExecutionResult<object>.CreateFailure(
                        result.ErrorMessage ?? "Unknown error", 
                        result.Exception ?? null!);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error executing containerized plugin {PluginId}", _pluginId);
                return Interface.PluginExecutionResult<object>.CreateFailure($"Plugin execution failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Factory for creating containerized plugin proxies
    /// </summary>
    public static class ContainerizedPluginProxyFactory
    {
        /// <summary>
        /// Creates a plugin proxy for the specified plugin
        /// </summary>
        /// <typeparam name="T">Plugin interface type</typeparam>
        /// <param name="orchestrator">CRPCF orchestrator</param>
        /// <param name="pluginId">Plugin identifier</param>
        /// <param name="logger">Optional logger</param>
        /// <returns>Plugin proxy</returns>
        public static async Task<ContainerizedPluginProxy<T>?> CreateAsync<T>(
            IContainerizedPluginOrchestrator orchestrator,
            PluginIdentifier pluginId,
            ILogger? logger = null) where T : IPluginClass
        {
            var pluginInfo = await orchestrator.GetPluginInfoAsync(pluginId);
            if (pluginInfo == null)
            {
                return null;
            }

            var typedLogger = logger as ILogger<ContainerizedPluginProxy<T>>;
            return new ContainerizedPluginProxy<T>(orchestrator, pluginId, pluginInfo, typedLogger);
        }

        /// <summary>
        /// Creates a plugin proxy with plugin information
        /// </summary>
        /// <typeparam name="T">Plugin interface type</typeparam>
        /// <param name="orchestrator">CRPCF orchestrator</param>
        /// <param name="pluginInfo">Plugin information</param>
        /// <param name="logger">Optional logger</param>
        /// <returns>Plugin proxy</returns>
        public static ContainerizedPluginProxy<T> Create<T>(
            IContainerizedPluginOrchestrator orchestrator,
            ContainerizedPluginInfo pluginInfo,
            ILogger? logger = null) where T : IPluginClass
        {
            var typedLogger = logger as ILogger<ContainerizedPluginProxy<T>>;
            return new ContainerizedPluginProxy<T>(orchestrator, pluginInfo.PluginId, pluginInfo, typedLogger);
        }
    }
}