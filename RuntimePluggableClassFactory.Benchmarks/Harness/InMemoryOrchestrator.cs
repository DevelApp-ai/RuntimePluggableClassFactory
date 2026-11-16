using DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces;
using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RuntimePluggableClassFactory.Benchmarks.Harness
{
    /// <summary>
    /// In-memory IContainerizedPluginOrchestrator for the mode-comparison
    /// measurement: NO containers, NO network - it measures the CRPCF dispatch
    /// code path (proxy -&gt; orchestrator -&gt; result mapping) only.
    /// Any number produced through this fake is explicitly an
    /// "orchestrator-seam overhead" number, NOT a containerized-execution
    /// number; the real containerized leg requires a container runtime and is
    /// tracked separately (see benchmarks/README.md).
    ///
    /// The handler implements the same logical operation as the shipped
    /// word-guess plugin (input equals the secret word -&gt; true), so the
    /// in-process and containerized-seam paths run comparable work.
    /// </summary>
    public sealed class InMemoryOrchestrator : IContainerizedPluginOrchestrator, IDisposable
    {
        private readonly List<ContainerizedPluginInfo> _plugins = new();
        private Func<string, object>? _handler;

        public long ExecutionCount;

        public event EventHandler<PluginEventArgs>? PluginDeployed { add { } remove { } }
        public event EventHandler<PluginEventArgs>? PluginExecuted { add { } remove { } }
        public event EventHandler<PluginEventArgs>? PluginFailed { add { } remove { } }
        public event EventHandler<PluginEventArgs>? PluginUndeployed { add { } remove { } }

        public InMemoryOrchestrator WithPlugin(PluginIdentifier pluginId, string description = "In-memory plugin")
        {
            _plugins.Add(new ContainerizedPluginInfo
            {
                PluginId = pluginId,
                Description = description,
                Status = PluginDeploymentStatus.Deployed,
                DeployedAt = DateTime.UtcNow
            });
            return this;
        }

        /// <summary>
        /// Sets the payload handler. Default: word-guess semantics
        /// (input == "Monster" -&gt; true) matching the shipped plugin corpus.
        /// </summary>
        public InMemoryOrchestrator WithHandler(Func<string, object> handler)
        {
            _handler = handler;
            return this;
        }

        public Task<PluginDeploymentResult> DeployPluginAsync(PluginDeploymentRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new PluginDeploymentResult { Success = true, DeploymentId = "in-memory-deploy" });

        public Task<ContainerizedPluginExecutionResult> ExecutePluginAsync(ContainerizedPluginExecutionRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref ExecutionCount);
            var handler = _handler ?? (input => input == "Monster");
            return Task.FromResult(new ContainerizedPluginExecutionResult
            {
                Success = true,
                Data = handler(request.InputData),
                ExecutionTime = TimeSpan.Zero,
                ExecutionId = Guid.NewGuid().ToString()
            });
        }

        public Task<ContainerizedPluginInfo?> GetPluginInfoAsync(PluginIdentifier pluginId, CancellationToken cancellationToken = default)
            => Task.FromResult(_plugins.FirstOrDefault(p =>
                p.PluginId.Namespace.Equals(pluginId.Namespace) &&
                p.PluginId.Name.Equals(pluginId.Name)));

        public Task<IEnumerable<ContainerizedPluginInfo>> ListPluginsAsync(PluginListOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<ContainerizedPluginInfo>>(_plugins.ToList());

        public Task UndeployPluginAsync(PluginIdentifier pluginId, CancellationToken cancellationToken = default)
        {
            _plugins.RemoveAll(p => p.PluginId.Equals(pluginId));
            return Task.CompletedTask;
        }

        public Task<PluginHealth> GetPluginHealthAsync(PluginIdentifier pluginId, CancellationToken cancellationToken = default)
            => Task.FromResult(new PluginHealth { IsHealthy = true, Status = "Healthy" });

        public Task NotifyPluginUpdateAsync(PluginIdentifier pluginId, SemanticVersionNumber newVersion, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Dispose()
        {
            _plugins.Clear();
        }
    }

    /// <summary>
    /// Minimal execution context for proxy execution (mirrors the test suite's
    /// TestExecutionContext).
    /// </summary>
    public sealed class BenchExecutionContext : IPluginExecutionContext
    {
        public static readonly BenchExecutionContext Instance = new();
        private BenchExecutionContext() { }
        public IPluginLogger? Logger => null;
        public CancellationToken CancellationToken => CancellationToken.None;
        public IReadOnlyDictionary<string, object>? Properties => null;
    }
}
