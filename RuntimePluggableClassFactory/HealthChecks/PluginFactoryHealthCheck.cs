using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DevelApp.RuntimePluggableClassFactory.HealthChecks
{
    /// <summary>
    /// Health check for plugin factory functionality
    /// </summary>
    /// <typeparam name="T">Type of plugin interface</typeparam>
    public class PluginFactoryHealthCheck<T> : IHealthCheck where T : IPluginClass
    {
        private readonly PluginClassFactory<T> _pluginFactory;
        
        public PluginFactoryHealthCheck(PluginClassFactory<T> pluginFactory)
        {
            _pluginFactory = pluginFactory ?? throw new ArgumentNullException(nameof(pluginFactory));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Test if plugin factory can refresh plugins
                var result = await _pluginFactory.RefreshPluginsAsync();
                
                if (!result.Success)
                {
                    return HealthCheckResult.Degraded(
                        $"Plugin factory refresh completed but with issues. Loaded {result.Count} plugins.");
                }
                
                // Get available plugins
                var plugins = _pluginFactory.GetAllInstanceNamesDescriptionsAndVersions();
                
                return HealthCheckResult.Healthy(
                    $"Plugin factory is healthy. Loaded {result.Count} plugins, {plugins?.Count() ?? 0} available.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy(
                    "Plugin factory health check failed",
                    ex);
            }
        }
    }
}
