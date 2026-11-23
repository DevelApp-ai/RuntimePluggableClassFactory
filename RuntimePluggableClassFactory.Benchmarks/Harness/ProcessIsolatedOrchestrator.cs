using DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces;
using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RuntimePluggableClassFactory.Benchmarks.Harness
{
    /// <summary>
    /// IContainerizedPluginOrchestrator over a real out-of-process plugin host
    /// (RuntimePluggableClassFactory.PluginHost) for the issue #28 M2 container
    /// leg and M3 migration study.
    ///
    /// REAL here: a separate OS process per deployment, real plugin assembly
    /// load inside that process, real JSON-over-pipe IPC and serialization on
    /// EVERY ExecutePluginAsync call, real cold start on every
    /// DeployPluginAsync, real teardown on every UndeployPluginAsync.
    ///
    /// NOT REAL here (labeled as such in every report that uses this class):
    /// no Docker image build/pull, no cgroup/sandbox isolation, no network hop
    /// (loopback pipes instead), no K8s scheduling (single host process per
    /// deployment, i.e. a single replica). This measures the process-hosting
    /// leg of containerized execution, not the full container stack.
    /// </summary>
    public sealed class ProcessIsolatedOrchestrator : IContainerizedPluginOrchestrator, IDisposable
    {
        private sealed class Deployment
        {
            public Process? Process;
            public StreamWriter? StdIn;
            public StreamReader? StdOut;
            public ContainerizedPluginInfo Info = new();
            public string HostId = string.Empty;
            public readonly object Gate = new();
        }

        private readonly string _hostDllPath;
        private readonly Dictionary<string, Deployment> _deployments = new();
        private readonly string _stagingRoot;
        private readonly object _gate = new();
        private bool _disposed;

        public event EventHandler<PluginEventArgs>? PluginDeployed { add { } remove { } }
        public event EventHandler<PluginEventArgs>? PluginExecuted { add { } remove { } }
        public event EventHandler<PluginEventArgs>? PluginFailed { add { } remove { } }
        public event EventHandler<PluginEventArgs>? PluginUndeployed { add { } remove { } }

        /// <summary>Most recent deployment diagnostics (measured inside DeployPluginAsync).</summary>
        public DeploymentDiagnostics LastDeployment { get; private set; } = new();

        /// <summary>Most recent undeploy diagnostics (measured inside UndeployPluginAsync).</summary>
        public double LastUndeployMs { get; private set; }

        /// <summary>Plain ping/pong round trip through the pipe (protocol floor, no plugin work).</summary>
        public double LastPingRoundTripMs { get; private set; }

        public ProcessIsolatedOrchestrator(string hostDllPath)
        {
            if (!File.Exists(hostDllPath))
            {
                throw new FileNotFoundException(
                    "PluginHost binary not found - build the solution first.", hostDllPath);
            }
            _hostDllPath = Path.GetFullPath(hostDllPath);
            _stagingRoot = Path.Combine(Path.GetTempPath(), "rpcf-bench-deploy-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_stagingRoot);
        }

        public async Task<PluginDeploymentResult> DeployPluginAsync(
            PluginDeploymentRequest request, CancellationToken cancellationToken = default)
        {
            // PackageStream carries the plugin payload (in this harness: the
            // plugin assembly itself, standing in for the signed NuGet package
            // a real CRPCF deployment consumes - signed packaging and signature
            // verification are NOT exercised by the measurement).
            if (request.PackageStream == null || request.PackageStream == Stream.Null)
            {
                return Failed("No plugin package stream in deployment request.");
            }
            if (request.Metadata == null ||
                !request.Metadata.TryGetValue("namespace", out var nsObj) ||
                !request.Metadata.TryGetValue("name", out var nameObj))
            {
                return Failed(
                    "Deployment request metadata must carry 'namespace' and 'name' (the shipped " +
                    "PluginDeploymentRequest type has no PluginIdentifier field, so the harness " +
                    "passes it via Metadata).");
            }

            var pluginId = new PluginIdentifier
            {
                Namespace = new NamespaceString(nsObj.ToString()!),
                Name = new IdentifierString(nameObj.ToString()!),
                Version = new SemanticVersionNumber(
                    request.Metadata.TryGetValue("major", out var mj) ? int.Parse(mj.ToString()!) : 1,
                    request.Metadata.TryGetValue("minor", out var mn) ? int.Parse(mn.ToString()!) : 0,
                    request.Metadata.TryGetValue("patch", out var pt) ? int.Parse(pt.ToString()!) : 0)
            };

            var sw = Stopwatch.StartNew();

            // 1) stage the payload (a real deployment moves the package into
            //    the registry/node; disk copy time is part of deploy cost)
            var payloadPath = Path.Combine(_stagingRoot, $"{pluginId.Name}_{pluginId.Version}.dll");
            using (var file = File.Create(payloadPath))
            {
                await request.PackageStream.CopyToAsync(file, 81920, cancellationToken);
            }
            var stagingMs = sw.Elapsed.TotalMilliseconds;

            // 2) spawn the host process (image start / container cold start)
            sw.Restart();
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{_hostDllPath}\" --plugin \"{payloadPath}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to spawn plugin host");
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.Error.WriteLine($"[pluginhost] {e.Data}"); };
            process.BeginErrorReadLine();

            var deployment = new Deployment
            {
                Process = process,
                StdIn = process.StandardInput,
                StdOut = process.StandardOutput,
                HostId = payloadPath,
                Info =
                {
                    PluginId = pluginId,
                    Description = "Process-isolated plugin host (measurement harness)",
                    Status = PluginDeploymentStatus.Deploying,
                    DeployedAt = DateTime.UtcNow,
                    ContainerInfo = new ContainerInfo
                    {
                        Platform = "process-host",
                        InstanceId = process.Id.ToString(),
                        Status = "starting"
                    }
                }
            };
            deployment.StdIn.AutoFlush = true;

            // 3) wait for readiness (destructive ping through the full pipe)
            const int readyTimeoutMs = 30000;
            var ready = false;
            var spin = Stopwatch.StartNew();
            while (spin.Elapsed.TotalMilliseconds < readyTimeoutMs && !process.HasExited)
            {
                try
                {
                    deployment.StdIn.WriteLine("{\"op\":\"ping\"}");
                    var reply = await deployment.StdOut.ReadLineAsync().WaitAsync(
                        TimeSpan.FromSeconds(5));
                    if (reply != null && reply.Contains("\"pong\""))
                    {
                        ready = true;
                        break;
                    }
                    if (reply == null) break; // pipe closed -> host died
                }
                catch (TimeoutException) { /* keep waiting until deadline */ }
                catch (OperationCanceledException) { }
            }
            var spawnToReadyMs = sw.Elapsed.TotalMilliseconds;

            if (!ready)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return Failed($"Plugin host did not become ready within {readyTimeoutMs} ms.");
            }

            deployment.Info.Status = PluginDeploymentStatus.Deployed;
            deployment.Info.ContainerInfo!.Status = "running";

            lock (_gate)
            {
                _deployments[Key(pluginId)] = deployment;
            }

            LastDeployment = new DeploymentDiagnostics
            {
                StagingMs = stagingMs,
                SpawnToReadyMs = spawnToReadyMs,
                TotalMs = stagingMs + spawnToReadyMs
            };

            return new PluginDeploymentResult
            {
                Success = true,
                DeploymentId = payloadPath,
                PluginId = pluginId,
                ContainerInfo = deployment.Info.ContainerInfo
            };
        }

        public async Task<ContainerizedPluginExecutionResult> ExecutePluginAsync(
            ContainerizedPluginExecutionRequest request, CancellationToken cancellationToken = default)
        {
            Deployment? deployment;
            lock (_gate)
            {
                _deployments.TryGetValue(Key(request.PluginId), out deployment);
            }
            if (deployment == null || deployment.Process == null || deployment.Process.HasExited)
            {
                return new ContainerizedPluginExecutionResult
                {
                    Success = false,
                    ErrorMessage = "Plugin host not deployed or has exited."
                };
            }

            var sw = Stopwatch.StartNew();
            var execId = Guid.NewGuid().ToString();
            string? reply = null;
            try
            {
                lock (deployment.Gate)
                {
                    // single replica -> requests serialize at the IPC boundary,
                    // exactly like a replicas:1 pod
                    var req = JsonSerializer.Serialize(new { op = "execute", id = execId, input = request.InputData });
                    deployment.StdIn!.WriteLine(req);
                    reply = deployment.StdOut!.ReadLine();
                }
            }
            catch (IOException ex)
            {
                return new ContainerizedPluginExecutionResult
                {
                    Success = false,
                    ErrorMessage = $"IPC failure: {ex.Message}",
                    ExecutionId = execId
                };
            }
            sw.Stop();

            if (reply == null)
            {
                return new ContainerizedPluginExecutionResult
                {
                    Success = false,
                    ErrorMessage = "Plugin host closed the pipe.",
                    ExecutionId = execId
                };
            }

            try
            {
                using var doc = JsonDocument.Parse(reply);
                var root = doc.RootElement;
                var success = root.TryGetProperty("success", out var sEl) && sEl.GetBoolean();
                if (!success)
                {
                    var err = root.TryGetProperty("error", out var eEl) ? eEl.GetString() : "unknown error";
                    return new ContainerizedPluginExecutionResult
                    {
                        Success = false,
                        ErrorMessage = err ?? "unknown error",
                        ExecutionId = execId,
                        ExecutionTime = sw.Elapsed
                    };
                }
                var data = root.TryGetProperty("data", out var dEl) ? dEl.ValueKind switch
                {
                    JsonValueKind.True => (object)true,
                    JsonValueKind.False => (object)false,
                    JsonValueKind.String => dEl.GetString()!,
                    _ => dEl.ToString()
                } : null;

                return new ContainerizedPluginExecutionResult
                {
                    Success = true,
                    Data = data,
                    ExecutionId = execId,
                    ExecutionTime = sw.Elapsed
                };
            }
            catch (JsonException ex)
            {
                return new ContainerizedPluginExecutionResult
                {
                    Success = false,
                    ErrorMessage = $"Bad response document: {ex.Message}",
                    ExecutionId = execId,
                    ExecutionTime = sw.Elapsed
                };
            }
        }

        public Task<ContainerizedPluginInfo?> GetPluginInfoAsync(
            PluginIdentifier pluginId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _deployments.TryGetValue(Key(pluginId), out var d);
                return Task.FromResult(d?.Info);
            }
        }

        public Task<IEnumerable<ContainerizedPluginInfo>> ListPluginsAsync(
            PluginListOptions? options = null, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult<IEnumerable<ContainerizedPluginInfo>>(
                    _deployments.Values.Select(d => d.Info).ToList());
            }
        }

        public Task UndeployPluginAsync(
            PluginIdentifier pluginId, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            Deployment? removed;
            lock (_gate)
            {
                _deployments.Remove(Key(pluginId), out removed);
            }
            if (removed != null)
            {
                try
                {
                    if (removed.StdIn != null)
                    {
                        removed.StdIn.WriteLine("{\"op\":\"shutdown\"}");
                        // best-effort ack; do not depend on it
                        Thread.Sleep(50);
                    }
                }
                catch (Exception) { /* pipe may already be broken */ }
                try
                {
                    if (removed.Process is { HasExited: false })
                    {
                        removed.Process.Kill(entireProcessTree: true);
                        removed.Process.WaitForExit(5000);
                    }
                }
                catch (Exception) { /* already gone */ }
                removed.StdIn?.Dispose();
                removed.StdOut?.Dispose();
                removed.Process?.Dispose();
            }
            sw.Stop();
            LastUndeployMs = sw.Elapsed.TotalMilliseconds;
            return Task.CompletedTask;
        }

        public async Task<PluginHealth> GetPluginHealthAsync(
            PluginIdentifier pluginId, CancellationToken cancellationToken = default)
        {
            Deployment? deployment;
            lock (_gate)
            {
                _deployments.TryGetValue(Key(pluginId), out deployment);
            }
            if (deployment?.Process == null || deployment.Process.HasExited)
            {
                return new PluginHealth { IsHealthy = false, Status = "No running host" };
            }

            var sw = Stopwatch.StartNew();
            try
            {
                lock (deployment.Gate)
                {
                    deployment.StdIn!.WriteLine("{\"op\":\"ping\"}");
                }
                var reply = deployment.StdOut!.ReadLine();
                sw.Stop();
                var healthy = reply != null && reply.Contains("\"pong\"");
                LastPingRoundTripMs = sw.Elapsed.TotalMilliseconds;
                return new PluginHealth { IsHealthy = healthy, Status = healthy ? "Healthy" : "No pong" };
            }
            catch (Exception ex)
            {
                return new PluginHealth { IsHealthy = false, Status = $"IPC failure: {ex.Message}" };
            }
        }

        public Task NotifyPluginUpdateAsync(
            PluginIdentifier pluginId, SemanticVersionNumber newVersion, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var key in _deployments.Keys.ToList())
            {
                var id = new PluginIdentifier
                {
                    Namespace = new NamespaceString(key.Split('|')[0]),
                    Name = new IdentifierString(key.Split('|')[1]),
                    Version = new SemanticVersionNumber(1, 0, 0)
                };
                UndeployPluginAsync(id).GetAwaiter().GetResult();
            }
            try { Directory.Delete(_stagingRoot, recursive: true); } catch { }
        }

        private static string Key(PluginIdentifier id) => $"{id.Namespace}|{id.Name}";

        private static PluginDeploymentResult Failed(string error) => new()
        {
            Success = false,
            Errors = new[] { error }
        };

        /// <summary>Cold-start breakdown of the most recent deployment.</summary>
        public sealed class DeploymentDiagnostics
        {
            public double StagingMs { get; set; }
            public double SpawnToReadyMs { get; set; }
            public double TotalMs { get; set; }
        }
    }
}
