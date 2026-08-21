using DevelApp.RuntimePluggableClassFactory.Interface;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.HealthChecks
{
    /// <summary>
    /// Health check for plugin loader connectivity and functionality
    /// </summary>
    /// <typeparam name="T">Type of plugin interface</typeparam>
    public class PluginLoaderHealthCheck<T> : IHealthCheck where T : IPluginClass
    {
        private readonly IPluginLoader<T> _pluginLoader;
        
        public PluginLoaderHealthCheck(IPluginLoader<T> pluginLoader)
        {
            _pluginLoader = pluginLoader ?? throw new ArgumentNullException(nameof(pluginLoader));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Test if plugin loader can list plugins
                var plugins = await _pluginLoader.ListAllPossiblePluginsAsync();
                
                return HealthCheckResult.Healthy(
                    $"Plugin loader is healthy. Found {plugins?.Count() ?? 0} plugins.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy(
                    "Plugin loader health check failed",
                    ex);
            }
        }
    }
}
