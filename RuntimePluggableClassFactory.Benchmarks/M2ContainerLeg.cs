using DevelApp.RuntimePluggableClassFactory;
using DevelApp.RuntimePluggableClassFactory.Containerized;
using DevelApp.RuntimePluggableClassFactory.Containerized.Implementations;
using DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces;
using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using Microsoft.Extensions.Logging.Abstractions;
using RuntimePluggableClassFactory.Benchmarks.Harness;
using PluginImplementations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RuntimePluggableClassFactory.Benchmarks
{
    /// <summary>
    /// Measurement 2 (issue #28), container leg: latency/throughput of plugin
    /// execution through a REAL out-of-process plugin host (the
    /// RuntimePluggableClassFactory.PluginHost binary that a plugin container
    /// image would run), dispatched through the shipped CRPCF path
    /// (ContainerizedPluginProxy -&gt; IContainerizedPluginOrchestrator -&gt;
    /// ProcessIsolatedOrchestrator).
    ///
    /// This completes the container half of the mode comparison that m2.json
    /// (committed 2026-11-16) only covered as an in-memory seam. Every number
    /// here crosses a real process boundary with real JSON serialization.
    ///
    /// SCOPE (honesty labels, mirrored into the report):
    /// - REAL: separate OS process, per-call JSON-over-pipe IPC and
    ///   serialization, host spawn-to-ready cold start, process teardown.
    /// - NOT MEASURED here: Docker image build/pull, sandbox/cgroup overhead,
    ///   network transport for non-local pods, K8s scheduling. This is a
    ///   single-replica local host process (loopback pipes), not a cluster.
    /// </summary>
    public static class M2ContainerLeg
    {
        public static async Task<Dictionary<string, object>> RunAsync(string pluginPath, int iterations)
        {
            var report = new Dictionary<string, object>
            {
                ["measurement"] = "m2-container-leg",
                ["description"] =
                    "Mode comparison, out-of-process half: plugin execution through the ContainerizedPluginProxy " +
                    "-> ProcessIsolatedOrchestrator -> real plugin host process (JSON over pipes, " +
                    "per-call serialization). Same payloads, same machine, same run as the in-process baseline below.",
                ["scopeWarning"] =
                    "Process host on loopback pipes, single replica. Adds on top in a real deployment (NOT measured " +
                    "here, per ground rules these numbers must not be presented as container-runtime numbers): image " +
                    "pull/build, cgroup/sandbox overhead, network hop, K8s orchestration.",
                ["hostBinary"] = "RuntimePluggableClassFactory.PluginHost (separate process per deployment)"
            };

            var hostDll = FindHostBinary();
            if (hostDll == null)
            {
                throw new InvalidOperationException(
                    "PluginHost binary not found. Build the solution (Release) so " +
                    "RuntimePluggableClassFactory.PluginHost is available under its bin directory.");
            }
            report["hostBinaryPath"] = Path.GetFileName(hostDll);

            // Same logical plugin as the in-process baseline (v1.2.1, secret "Monster")
            var pluginDll = Directory.GetFiles(Path.Combine(pluginPath, "PluginImplementations_1_2_1"), "*.dll",
                    SearchOption.TopDirectoryOnly)
                .FirstOrDefault() ?? throw new InvalidOperationException("Plugin assembly PluginImplementations_1_2_1 not found in plugin folder.");

            var perCall = Math.Max(iterations * 5, 40);   // IPC latency samples per scenario
            var coldStartSamples = iterations;
            var concurrency = 8;

            // ------------------------------------------------------------------
            // In-process baseline (same process, same run - for directly
            // comparable per-payload overheads below). Pinned to the SAME
            // v1.2.1 assembly the host process loads: the factory's version
            // store retains only retainOldVersions (=1 default) versions per
            // plugin name, so exactly one version is allowed+loaded.
            // ------------------------------------------------------------------
            using var loader = M1RawThresholdData.CreateLoader(pluginPath);
            var factory = new PluginClassFactory<ISpecificInterface>(loader);
            var baselineEntry = (await factory.GetPossiblePlugins())
                .First(p => p.pluginName.ToString() == "SpecificClassImpl"
                          && p.Type.Assembly.Location.EndsWith("PluginImplementations_1_2_1.dll"));
            factory.AllowPlugin(baselineEntry.moduleName, baselineEntry.pluginName, baselineEntry.version);
            await factory.RefreshPluginsAsync();
            var instance = factory.GetInstance(baselineEntry.moduleName, baselineEntry.pluginName, baselineEntry.version)
                ?? throw new InvalidOperationException("GetInstance returned null");
            report["payloadPluginPinnedTo"] = "Test.SpecificClassImpl v1.2.1 (both paths, same assembly)";

            // ------------------------------------------------------------------
            // Cold start: DeployPluginAsync wall time, N full deploy/undeploy
            // cycles of a real host process
            // ------------------------------------------------------------------
            var pluginId = new PluginIdentifier
            {
                Namespace = baselineEntry.moduleName,
                Name = baselineEntry.pluginName,
                Version = baselineEntry.version
            };

            {
                var totals = new List<double>();
                var spawnToReady = new List<double>();
                var staging = new List<double>();
                var undeploys = new List<double>();
                using var orchestrator = new Harness.ProcessIsolatedOrchestrator(hostDll);
                for (var i = 0; i < coldStartSamples; i++)
                {
                    var deploy = await DeployAsync(orchestrator, pluginDll, pluginId);
                    if (!deploy.result.Success)
                    {
                        throw new InvalidOperationException("Deployment failed during cold-start sampling: " +
                            string.Join("; ", deploy.result.Errors));
                    }
                    totals.Add(orchestrator.LastDeployment.TotalMs);
                    staging.Add(orchestrator.LastDeployment.StagingMs);
                    spawnToReady.Add(orchestrator.LastDeployment.SpawnToReadyMs);

                    await orchestrator.UndeployPluginAsync(pluginId);
                    undeploys.Add(orchestrator.LastUndeployMs);
                }
                report["coldStartDeployTotal"] = new SampleSet("ms", totals).ToSummary();
                report["coldStartPackageStaging"] = new SampleSet("ms", staging).ToSummary();
                report["coldStartHostSpawnToReady"] = new SampleSet("ms", spawnToReady).ToSummary();
                report["coldStartUndeployTeardown"] = new SampleSet("ms", undeploys).ToSummary();
                report["coldStartSamples"] = coldStartSamples;
                report["coldStartNotes"] =
                    "Deploy = package staging + host process spawn + wait-for-ready (ping/pong through a real pipe). " +
                    "A container deployment adds image pull/build and scheduler time on top - not measured here.";
            }

            // ------------------------------------------------------------------
            // Latency: same logical word-guess operation at three payload sizes,
            // in-process vs out-of-process host, through the shipped proxy
            // ------------------------------------------------------------------
            using (var orchestrator = new Harness.ProcessIsolatedOrchestrator(hostDll))
            {
                var deploy = await DeployAsync(orchestrator, pluginDll, pluginId);
                if (!deploy.result.Success)
                {
                    throw new InvalidOperationException("Deployment failed: " + string.Join("; ", deploy.result.Errors));
                }

                var proxy = ContainerizedPluginProxyFactory.Create<ISpecificInterface>(
                    orchestrator, (await orchestrator.ListPluginsAsync()).First());

                var payloadSizes = new[] { 8, 10_240, 102_400 };
                foreach (var payloadSize in payloadSizes)
                {
                    var sizePayload = payloadSize <= 7
                        ? "Monster"
                        : new string('x', payloadSize);

                    // In-process (same run)
                    Measure.Warmup(50, () => _ = instance.Execute("Monster"));
                    var inProc = Measure.Collect("ms", perCall, i => _ = instance.Execute(sizePayload));

                    // Out-of-process host through the proxy
                    Measure.Warmup(20, () => _ = proxy.ExecuteTyped(BenchExecutionContext.Instance, "Monster"));
                    var oop = Measure.Collect("ms", perCall,
                        i => _ = proxy.ExecuteTyped(BenchExecutionContext.Instance, sizePayload));

                    report[$"payload{payloadSize}BytesInProcess"] = inProc.ToSummary();
                    report[$"payload{payloadSize}BytesContainerHost"] = oop.ToSummary();
                    report[$"payload{payloadSize}BytesOverhead"] = new Dictionary<string, object>
                    {
                        ["meanDeltaMs"] = oop.Mean - inProc.Mean,
                        ["medianDeltaMs"] = oop.Median - inProc.Median,
                        ["containerHostToInProcMeanRatio"] = inProc.Mean > 0 ? oop.Mean / inProc.Mean : 0
                    };
                }

                // Protocol floor: ping/pong through the pipe, no plugin work
                _ = await orchestrator.GetPluginHealthAsync(pluginId);
                var floors = new List<double>();
                for (var i = 0; i < perCall; i++)
                {
                    _ = await orchestrator.GetPluginHealthAsync(pluginId);
                    floors.Add(orchestrator.LastPingRoundTripMs);
                }
                report["ipcProtocolFloorPingPong"] = new SampleSet("ms", floors).ToSummary();

                // ------------------------------------------------------------------
                // Throughput: sequential + 8 workers, single replica (requests
                // serialize at the host's pipe, as they would on a replicas:1 pod)
                // ------------------------------------------------------------------
                const string payload = "Monster";
                {
                    var batches = Math.Max(5, iterations / 6);
                    var inProcThroughput = new List<double>();
                    var oopThroughput = new List<double>();
                    for (var b = 0; b < batches; b++)
                    {
                        var sw = Stopwatch.StartNew();
                        for (var i = 0; i < perCall; i++)
                        {
                            _ = instance.Execute(payload);
                        }
                        sw.Stop();
                        inProcThroughput.Add(perCall * 1000.0 / sw.Elapsed.TotalMilliseconds);

                        sw = Stopwatch.StartNew();
                        for (var i = 0; i < perCall; i++)
                        {
                            _ = proxy.ExecuteTyped(BenchExecutionContext.Instance, payload);
                        }
                        sw.Stop();
                        oopThroughput.Add(perCall * 1000.0 / sw.Elapsed.TotalMilliseconds);
                    }
                    report["throughputSequentialInProcess"] = new SampleSet("exec/sec", inProcThroughput).ToSummary();
                    report["throughputSequentialContainerHost"] = new SampleSet("exec/sec", oopThroughput).ToSummary();

                    // Concurrent: 8 workers against the single-replica host
                    var inProcConc = new List<double>();
                    var oopConc = new List<double>();
                    var perWorker = Math.Max(perCall / concurrency, 5);
                    for (var b = 0; b < batches; b++)
                    {
                        var sw = Stopwatch.StartNew();
                        var tasks = new Task[concurrency];
                        for (var w = 0; w < concurrency; w++)
                        {
                            tasks[w] = Task.Run(() =>
                            {
                                for (var i = 0; i < perWorker; i++)
                                {
                                    _ = instance.Execute(payload);
                                }
                            });
                        }
                        await Task.WhenAll(tasks);
                        sw.Stop();
                        inProcConc.Add(concurrency * perWorker * 1000.0 / sw.Elapsed.TotalMilliseconds);

                        sw = Stopwatch.StartNew();
                        tasks = new Task[concurrency];
                        for (var w = 0; w < concurrency; w++)
                        {
                            tasks[w] = Task.Run(() =>
                            {
                                for (var i = 0; i < perWorker; i++)
                                {
                                    _ = proxy.ExecuteTyped(BenchExecutionContext.Instance, payload);
                                }
                            });
                        }
                        await Task.WhenAll(tasks);
                        sw.Stop();
                        oopConc.Add(concurrency * perWorker * 1000.0 / sw.Elapsed.TotalMilliseconds);
                    }
                    report["throughputConcurrentWorkers"] = concurrency;
                    report["throughputConcurrentInProcess"] = new SampleSet("exec/sec", inProcConc).ToSummary();
                    report["throughputConcurrentContainerHost"] = new SampleSet("exec/sec", oopConc).ToSummary();
                    report["throughputNotes"] =
                        "Single-replica host: concurrent dispatch serializes at the pipe boundary just like a " +
                        "replicas:1 pod; the concurrent-container number measures dispatch queueing, not parallel " +
                        "plugin execution. Horizontal scale (more replicas) is not measured here.";
                }

                // ------------------------------------------------------------------
                // Auto-resolution overhead against the REAL orchestrator
                // ------------------------------------------------------------------
                {
                    using var missOrchestrator = new Harness.ProcessIsolatedOrchestrator(hostDll);
                    using var baseLoader = new DevelApp.RuntimePluggableClassFactory.FilePlugin.FilePluginLoader<IPluginClass>(
                        new Uri(Path.GetFullPath(pluginPath)));
                    var traditional = new PluginClassFactory<IPluginClass>(baseLoader);
                    foreach (var p in await traditional.GetPossiblePlugins())
                    {
                        traditional.AllowPlugin(p.moduleName, p.pluginName, p.version);
                    }
                    await traditional.RefreshPluginsAsync();

                    var hybridHit = new HybridPluginFactory<IPluginClass>(
                        traditional, orchestrator, NullLogger<HybridPluginFactory<IPluginClass>>.Instance,
                        new HybridPluginFactoryOptions { PreferContainerized = true });

                    var hybridMiss = new HybridPluginFactory<IPluginClass>(
                        traditional, missOrchestrator, NullLogger<HybridPluginFactory<IPluginClass>>.Instance,
                        new HybridPluginFactoryOptions { PreferContainerized = true });

                    var ns = new NamespaceString(baselineEntry.moduleName.ToString());
                    var name = new IdentifierString(baselineEntry.pluginName.ToString());

                    _ = hybridHit.GetPluginAsync(ns, name);
                    _ = hybridMiss.GetPluginAsync(ns, name);

                    var direct = Measure.Collect("ms", perCall, i => _ = traditional.GetInstance(ns, name));

                    var autoHit = await Measure.CollectAsync("ms", perCall,
                        async i => _ = await hybridHit.GetPluginAsync(ns, name));

                    var autoMiss = await Measure.CollectAsync("ms", perCall,
                        async i => _ = await hybridMiss.GetPluginAsync(ns, name));

                    report["autoResolutionDirectTraditional"] = direct.ToSummary();
                    report["autoResolutionHybridContainerizedHit"] = autoHit.ToSummary();
                    report["autoResolutionHybridContainerizedMissFallback"] = autoMiss.ToSummary();
                    report["autoResolutionOverheadNotes"] = new Dictionary<string, object>
                    {
                        ["hitVsDirectMeanMs"] = autoHit.Mean - direct.Mean,
                        ["missFallbackVsDirectMeanMs"] = autoMiss.Mean - direct.Mean,
                        ["note"] =
                            "Hit = orchestrator GetPluginInfoAsync over a LIVE host registry entry + proxy creation. " +
                            "Miss-fallback = info miss on an empty orchestrator + traditional lookup (the deployed host " +
                            "in the miss case was never spawned; a real registry with network round trips adds latency " +
                            "not measured here)."
                    };
                    traditional.Dispose();
                }
            }

            return report;
        }

        internal static async Task<(PluginDeploymentResult result, double elapsedMs)> DeployAsync(
            Harness.ProcessIsolatedOrchestrator orchestrator, string pluginDll, PluginIdentifier pluginId)
        {
            await using var package = File.OpenRead(pluginDll);
            var request = new PluginDeploymentRequest
            {
                PackageStream = package,
                TargetPlatform = "process-host",
                Metadata = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["namespace"] = pluginId.Namespace.ToString(),
                    ["name"] = pluginId.Name.ToString(),
                    ["major"] = pluginId.Version.Major,
                    ["minor"] = pluginId.Version.Minor,
                    ["patch"] = pluginId.Version.Patch
                }
            };
            var sw = Stopwatch.StartNew();
            var result = await orchestrator.DeployPluginAsync(request);
            sw.Stop();
            return (result, sw.Elapsed.TotalMilliseconds);
        }

        /// <summary>
        /// Resolves the plugin host binary the same way the harness resolves
        /// the plugin folder: walk up to the repository root from the output
        /// directory, then into the PluginHost project's Release output.
        /// </summary>
        internal static string? FindHostBinary()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var current = dir; current != null; current = current.Parent)
            {
                var candidate = Path.Combine(current.FullName,
                    "RuntimePluggableClassFactory.PluginHost", "bin", "Release", "net8.0",
                    "RuntimePluggableClassFactory.PluginHost.dll");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                    File.Exists(Path.Combine(current.FullName, "RuntimePluggableClassFactory.sln")))
                {
                    // stop at repo root to avoid wandering the disk
                    var rootCandidate = candidate;
                    return File.Exists(rootCandidate) ? rootCandidate : null;
                }
            }
            return null;
        }
    }
}
