using DevelApp.RuntimePluggableClassFactory;
using DevelApp.RuntimePluggableClassFactory.Interface;
using RuntimePluggableClassFactory.Benchmarks.Harness;
using DevelApp.RuntimePluggableClassFactory.Containerized;
using DevelApp.RuntimePluggableClassFactory.Containerized.Implementations;
using DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces;
using DevelApp.Utility.Model;
using Microsoft.Extensions.Logging.Abstractions;
using PluginImplementations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RuntimePluggableClassFactory.Benchmarks
{
    /// <summary>
    /// Measurement 2 (issue #28): mode comparison - latency/throughput of
    /// in-process vs containerized-path plugin execution, including the
    /// Auto-resolution overhead of HybridPluginFactory.
    ///
    /// IMPORTANT scope note: the containerized numbers produced here run
    /// through an in-memory IContainerizedPluginOrchestrator (see
    /// Harness/InMemoryOrchestrator.cs). They measure the shipped CRPCF
    /// dispatch path - ContainerizedPluginProxy.ExecuteTyped -&gt; orchestrator
    /// -&gt; ContainerizedPluginExecutionResult mapping - WITHOUT any container.
    /// They are therefore an upper-bound-free measurement of the framework
    /// seam overhead: the real containerized leg (container cold start,
    /// per-exec container cost) requires a container runtime and is tracked
    /// as the remaining containerized half of this measurement.
    /// </summary>
    public static class M2ModeComparison
    {
        private const string PluginNamespace = "Test";
        private const string PluginName = "SpecificClassImpl";

        public static async Task<Dictionary<string, object>> RunAsync(string pluginPath, int iterations)
        {
            var report = new Dictionary<string, object>
            {
                ["measurement"] = "m2-mode-comparison",
                ["description"] = "In-process vs containerized-seam dispatch overhead + HybridPluginFactory auto-resolution overhead. Containerized-seam numbers use an in-memory orchestrator (no container) and measure the CRPCF framework path only.",
                ["scopeWarning"] = "containerized-seam != containerized execution. The container leg (cold start, container runtime cost) is not measured by this harness yet."
            };

            var perCall = iterations * 20;
            var concurrency = 8;

            // ------------------------------------------------------------------
            // Setup: in-process factory over the shipped plugin corpus
            // ------------------------------------------------------------------
            using var loader = M1RawThresholdData.CreateLoader(pluginPath);
            var factory = new PluginClassFactory<ISpecificInterface>(loader);
            await M1RawThresholdData.AllowAllAsync(factory);
            var plugins = await factory.GetPossiblePlugins();
            var plugin = plugins.First();
            var instance = factory.GetInstance(plugin.moduleName, plugin.pluginName)
                ?? throw new InvalidOperationException("GetInstance returned null");

            var pluginId = new PluginIdentifier
            {
                Namespace = new NamespaceString("Test"),
                Name = new IdentifierString("SpecificClassImpl"),
                Version = new SemanticVersionNumber(1, 2, 1)
            };

            // ------------------------------------------------------------------
            // Payload sweep: same logical operation (word-guess) at three input
            // payload sizes, in-process vs containerized-seam
            // ------------------------------------------------------------------
            var payloadSizes = new[] { 8, 10_240, 102_400 };
            foreach (var payloadSize in payloadSizes)
            {
                // Payload of the requested size; the word-guess handler returns
                // false for oversized payloads - both paths cross the boundary
                // with the same bytes (the comparison result is irrelevant).
                var payload = payloadSize <= 7
                    ? "Monster"
                    : new string('x', payloadSize);

                // In-process
                Measure.Warmup(50, () => _ = instance.Execute("Monster"));
                var inProc = Measure.Collect("ms", perCall, i => _ = instance.Execute(payload));

                // Containerized-seam (proxy over in-memory orchestrator)
                using var orchestrator = new InMemoryOrchestrator().WithPlugin(pluginId);
                var proxy = ContainerizedPluginProxyFactory.Create<ISpecificInterface>(orchestrator,
                    orchestrator.ListPluginsAsync().Result.First());
                Measure.Warmup(50, () => _ = proxy.ExecuteTyped(BenchExecutionContext.Instance, "Monster"));
                var seam = Measure.Collect("ms", perCall, i => _ = proxy.ExecuteTyped(BenchExecutionContext.Instance, payload));

                report[$"payload{payloadSize}BytesInProcess"] = inProc.ToSummary();
                report[$"payload{payloadSize}BytesContainerizedSeam"] = seam.ToSummary();
                report[$"payload{payloadSize}BytesSeamOverhead"] = new Dictionary<string, object>
                {
                    ["meanDeltaMs"] = seam.Mean - inProc.Mean,
                    ["medianDeltaMs"] = seam.Median - inProc.Median,
                    ["seamToInProcMeanRatio"] = inProc.Mean > 0 ? seam.Mean / inProc.Mean : 0
                };
            }

            // ------------------------------------------------------------------
            // Throughput: sequential + concurrent (8 workers), word-guess payload
            // ------------------------------------------------------------------
            {
                // In-process concurrent
                using var orchestrator = new InMemoryOrchestrator().WithPlugin(pluginId);
                var proxy = ContainerizedPluginProxyFactory.Create<ISpecificInterface>(orchestrator,
                    orchestrator.ListPluginsAsync().Result.First());

                const string payload = "Monster";

                var inProcThroughput = new List<double>();
                var seamThroughput = new List<double>();
                var batches = Math.Max(5, iterations / 6);
                for (var b = 0; b < batches; b++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (var i = 0; i < perCall; i++)
                    {
                        _ = instance.Execute(payload);
                    }
                    sw.Stop();
                    inProcThroughput.Add(perCall * 1000.0 / sw.Elapsed.TotalMilliseconds);

                    sw = System.Diagnostics.Stopwatch.StartNew();
                    for (var i = 0; i < perCall; i++)
                    {
                        _ = proxy.ExecuteTyped(BenchExecutionContext.Instance, payload);
                    }
                    sw.Stop();
                    seamThroughput.Add(perCall * 1000.0 / sw.Elapsed.TotalMilliseconds);
                }
                report["throughputSequentialInProcess"] = new SampleSet("exec/sec", inProcThroughput).ToSummary();
                report["throughputSequentialContainerizedSeam"] = new SampleSet("exec/sec", seamThroughput).ToSummary();

                // Concurrent
                var inProcConc = new List<double>();
                var seamConc = new List<double>();
                var perWorker = perCall / concurrency;
                for (var b = 0; b < batches; b++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
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
                    inProcConc.Add(perCall * 1000.0 / sw.Elapsed.TotalMilliseconds);

                    sw = System.Diagnostics.Stopwatch.StartNew();
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
                    seamConc.Add(perCall * 1000.0 / sw.Elapsed.TotalMilliseconds);
                }
                report["throughputConcurrentWorkers"] = concurrency;
                report["throughputConcurrentInProcess"] = new SampleSet("exec/sec", inProcConc).ToSummary();
                report["throughputConcurrentContainerizedSeam"] = new SampleSet("exec/sec", seamConc).ToSummary();
            }

            // ------------------------------------------------------------------
            // HybridPluginFactory auto-resolution overhead
            // ------------------------------------------------------------------
            {
                using var orchestrator = new InMemoryOrchestrator().WithPlugin(pluginId);
                // Base-interface factory: the hybrid factory is typed against
                // IPluginClass, so it needs its own loader over the same
                // plugin folder.
                using var baseLoader = new DevelApp.RuntimePluggableClassFactory.FilePlugin.FilePluginLoader<DevelApp.RuntimePluggableClassFactory.Interface.IPluginClass>(
                    new Uri(Path.GetFullPath(pluginPath)));
                var traditional = new PluginClassFactory<DevelApp.RuntimePluggableClassFactory.Interface.IPluginClass>(baseLoader);
                foreach (var p in await traditional.GetPossiblePlugins())
                {
                    traditional.AllowPlugin(p.moduleName, p.pluginName, p.version);
                }
                await traditional.RefreshPluginsAsync();

                var hybridTraditionalFirst = new HybridPluginFactory<IPluginClass>(
                    traditional, orchestrator, NullLogger<HybridPluginFactory<IPluginClass>>.Instance,
                    new HybridPluginFactoryOptions { PreferContainerized = false });

                var hybridContainerizedFirst = new HybridPluginFactory<IPluginClass>(
                    traditional, orchestrator, NullLogger<HybridPluginFactory<IPluginClass>>.Instance,
                    new HybridPluginFactoryOptions { PreferContainerized = true });

                var hybridMiss = new HybridPluginFactory<IPluginClass>(
                    traditional, new InMemoryOrchestrator(), NullLogger<HybridPluginFactory<IPluginClass>>.Instance,
                    new HybridPluginFactoryOptions { PreferContainerized = true });

                var ns = new NamespaceString(plugin.moduleName.ToString());
                var name = new IdentifierString(plugin.pluginName.ToString());

                // Warm-up
                Measure.Warmup(20, () => _ = traditional.GetInstance(ns, name));
                _ = await hybridTraditionalFirst.GetPluginAsync(ns, name);
                _ = await hybridContainerizedFirst.GetPluginAsync(ns, name);
                _ = await hybridMiss.GetPluginAsync(ns, name);

                // Direct traditional lookup (baseline)
                var direct = Measure.Collect("ms", perCall, i => _ = traditional.GetInstance(ns, name));

                // Auto, traditional-first (prefer containerized = false)
                var autoTrad = await Measure.CollectAsync("ms", perCall,
                    async i => _ = await hybridTraditionalFirst.GetPluginAsync(ns, name));

                // Auto, containerized-first hit (prefer containerized = true)
                var autoCont = await Measure.CollectAsync("ms", perCall,
                    async i => _ = await hybridContainerizedFirst.GetPluginAsync(ns, name));

                // Auto, containerized-first miss -> fallback to traditional
                var autoMiss = await Measure.CollectAsync("ms", perCall,
                    async i => _ = await hybridMiss.GetPluginAsync(ns, name));

                report["autoResolutionDirectTraditional"] = direct.ToSummary();
                report["autoResolutionHybridAutoTraditionalFirst"] = autoTrad.ToSummary();
                report["autoResolutionHybridAutoContainerizedFirst"] = autoCont.ToSummary();
                report["autoResolutionHybridAutoContainerizedMissFallback"] = autoMiss.ToSummary();
                report["autoResolutionOverheadNotes"] = new Dictionary<string, object>
                {
                    ["hybridAutoTraditionalFirstVsDirectMeanMs"] = autoTrad.Mean - direct.Mean,
                    ["hybridAutoContainerizedFirstVsDirectMeanMs"] = autoCont.Mean - direct.Mean,
                    ["hybridAutoMissFallbackVsDirectMeanMs"] = autoMiss.Mean - direct.Mean,
                    ["note"] = "Miss-fallback = GetPluginInfoAsync miss on the orchestrator + traditional lookup; with an in-memory orchestrator the miss cost is the dictionary lookup only. A real container backend's miss/list latency adds on top."
                };
                traditional.Dispose();
            }

            return report;
        }
    }
}
