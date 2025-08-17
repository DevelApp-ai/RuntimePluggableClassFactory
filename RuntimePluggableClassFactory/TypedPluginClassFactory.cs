using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory
{
    /// <summary>
    /// Typed plugin class factory providing type-safe plugin execution
    /// Implements TDS requirement for strongly-typed DTOs and functional context
    /// </summary>
    /// <typeparam name="TPlugin">Plugin interface type</typeparam>
    /// <typeparam name="TInput">Input data type</typeparam>
    /// <typeparam name="TOutput">Output data type</typeparam>
    public class TypedPluginClassFactory<TPlugin, TInput, TOutput> 
        where TPlugin : class, IPluginClass, ITypedPluginClass<TInput, TOutput>
    {
        private readonly PluginClassFactory<TPlugin> _underlyingFactory;

        public TypedPluginClassFactory(IPluginLoader<TPlugin> pluginLoader, int retainOldVersions = 1)
        {
            _underlyingFactory = new PluginClassFactory<TPlugin>(pluginLoader, retainOldVersions);
        }

        /// <summary>
        /// The underlying plugin loader
        /// </summary>
        public IPluginLoader<TPlugin> PluginLoader => _underlyingFactory.PluginLoader;

        /// <summary>
        /// Event fired when plugin instantiation fails
        /// </summary>
        public event EventHandler<PluginInstantiationErrorEventArgs> PluginInstantiationFailed
        {
            add => _underlyingFactory.PluginInstantiationFailed += value;
            remove => _underlyingFactory.PluginInstantiationFailed -= value;
        }

        /// <summary>
        /// Allows a plugin to be loaded into the factory
        /// </summary>
        public void AllowPlugin(NamespaceString moduleName, IdentifierString pluginName, SemanticVersionNumber version)
        {
            _underlyingFactory.AllowPlugin(moduleName, pluginName, version);
        }

        /// <summary>
        /// Disallows a plugin to be loaded into the factory
        /// </summary>
        public void DisallowPlugin(NamespaceString moduleName, IdentifierString pluginName, SemanticVersionNumber version)
        {
            _underlyingFactory.DisallowPlugin(moduleName, pluginName, version);
        }

        /// <summary>
        /// Lists all possible plugins that can be loaded
        /// </summary>
        /// <returns>Enumerable of plugin information</returns>
        public async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string Description, Type Type)>> GetPossiblePlugins()
        {
            return await _underlyingFactory.GetPossiblePlugins();
        }

        public async Task<IEnumerable<(NamespaceString ModuleName, IdentifierString PluginName, SemanticVersionNumber Version, string Description, Type Type)>> ListAllPossiblePluginsAsync()
        {
            return await _underlyingFactory.PluginLoader.ListAllPossiblePluginsAsync();
        }

        /// <summary>
        /// Returns all plugins currently in the store
        /// </summary>
        public IEnumerable<(NamespaceString ModuleName, IdentifierString Name, string Description, List<SemanticVersionNumber> Versions)> GetAllInstanceNamesDescriptionsAndVersions()
        {
            return _underlyingFactory.GetAllInstanceNamesDescriptionsAndVersions();
        }

        /// <summary>
        /// Refreshes the plugin cache
        /// </summary>
        /// <returns>Task representing the refresh operation</returns>
        public async Task<(bool Success, int Count)> RefreshPluginsAsync()
        {
            return await _underlyingFactory.RefreshPluginsAsync();
        }

        /// <summary>
        /// Executes a plugin with type-safe input and output using the newest version
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="pluginName">Plugin name</param>
        /// <param name="input">Strongly-typed input data</param>
        /// <param name="context">Optional execution context</param>
        /// <returns>Strongly-typed execution result</returns>
        public Interface.PluginExecutionResult<TOutput> ExecutePlugin(
            NamespaceString moduleName, 
            IdentifierString pluginName, 
            TInput input,
            IPluginExecutionContext context = null)
        {
            context ??= new PluginExecutionContext();

            try
            {
                var plugin = _underlyingFactory.GetInstance(moduleName, pluginName);
                if (plugin == null)
                {
                    return Interface.PluginExecutionResult<TOutput>.CreateFailure(
                        $"Plugin not found: {moduleName}.{pluginName}");
                }

                return ExecutePluginSafely(plugin, input, context);
            }
            catch (Exception ex)
            {
                return Interface.PluginExecutionResult<TOutput>.CreateFailure(
                    $"Failed to execute plugin {moduleName}.{pluginName}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Executes a plugin with type-safe input and output using a specific version
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="pluginName">Plugin name</param>
        /// <param name="version">Plugin version</param>
        /// <param name="input">Strongly-typed input data</param>
        /// <param name="context">Optional execution context</param>
        /// <returns>Strongly-typed execution result</returns>
        public Interface.PluginExecutionResult<TOutput> ExecutePlugin(
            NamespaceString moduleName, 
            IdentifierString pluginName, 
            SemanticVersionNumber version,
            TInput input,
            IPluginExecutionContext context = null)
        {
            context ??= new PluginExecutionContext();

            try
            {
                var plugin = _underlyingFactory.GetInstance(moduleName, pluginName, version);
                if (plugin == null)
                {
                    return Interface.PluginExecutionResult<TOutput>.CreateFailure(
                        $"Plugin not found: {moduleName}.{pluginName} v{version}");
                }

                return ExecutePluginSafely(plugin, input, context);
            }
            catch (Exception ex)
            {
                return Interface.PluginExecutionResult<TOutput>.CreateFailure(
                    $"Failed to execute plugin {moduleName}.{pluginName} v{version}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Executes a plugin with type-safe input and output asynchronously using the newest version
        /// </summary>
        /// <param name="moduleName">Module name</param>
        /// <param name="pluginName">Plugin name</param>
        /// <param name="input">Strongly-typed input data</param>
        /// <param name="context">Optional execution context</param>
        /// <param name="timeout">Optional timeout</param>
        /// <returns>Strongly-typed execution result</returns>
        public async Task<Interface.PluginExecutionResult<TOutput>> ExecutePluginAsync(
            NamespaceString moduleName, 
            IdentifierString pluginName, 
            TInput input,
            IPluginExecutionContext context = null,
            TimeSpan? timeout = null)
        {
            context ??= new PluginExecutionContext();

            try
            {
                var plugin = _underlyingFactory.GetInstance(moduleName, pluginName);
                if (plugin == null)
                {
                    return Interface.PluginExecutionResult<TOutput>.CreateFailure(
                        $"Plugin not found: {moduleName}.{pluginName}");
                }

                return await ExecutePluginSafelyAsync(plugin, input, context, timeout);
            }
            catch (Exception ex)
            {
                return Interface.PluginExecutionResult<TOutput>.CreateFailure(
                    $"Failed to execute plugin {moduleName}.{pluginName}: {ex.Message}", ex);
            }
        }

        private Interface.PluginExecutionResult<TOutput> ExecutePluginSafely(TPlugin plugin, TInput input, IPluginExecutionContext context)
        {
            try
            {
                return plugin.Execute(context, input);
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Plugin execution failed: {ex.Message}", ex);
                return Interface.PluginExecutionResult<TOutput>.CreateFailure(
                    $"Plugin execution failed: {ex.Message}", ex);
            }
        }

        private async Task<Interface.PluginExecutionResult<TOutput>> ExecutePluginSafelyAsync(
            TPlugin plugin, 
            TInput input, 
            IPluginExecutionContext context, 
            TimeSpan? timeout)
        {
            try
            {
                if (timeout.HasValue)
                {
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken))
                    {
                        cts.CancelAfter(timeout.Value);
                        var newContext = new PluginExecutionContext(context.Logger, cts.Token, context.Properties);
                        return await Task.Run(() => plugin.Execute(newContext, input), cts.Token);
                    }
                }
                else
                {
                    return await Task.Run(() => plugin.Execute(context, input), context.CancellationToken);
                }
            }
            catch (OperationCanceledException ex) when (timeout.HasValue)
            {
                var message = $"Plugin execution timed out after {timeout.Value.TotalSeconds} seconds";
                context.Logger.LogError(message, ex);
                return Interface.PluginExecutionResult<TOutput>.CreateFailure(message, ex);
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Plugin execution failed: {ex.Message}", ex);
                return Interface.PluginExecutionResult<TOutput>.CreateFailure(
                    $"Plugin execution failed: {ex.Message}", ex);
            }
        }
    }
}

