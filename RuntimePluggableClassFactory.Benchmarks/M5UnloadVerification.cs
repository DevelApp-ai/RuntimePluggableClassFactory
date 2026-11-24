using DevelApp.RuntimePluggableClassFactory;
using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.RuntimePluggableClassFactory.FilePlugin;
using DevelApp.RuntimePluggableClassFactory.Security;
using RuntimePluggableClassFactory.Benchmarks.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using PluginImplementations;
using DevelApp.RuntimePluggableClassFactory.Containerized;
using DevelApp.RuntimePluggableClassFactory.Containerized.Implementations;
using DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces;
using DevelApp.Utility.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace RuntimePluggableClassFactory.Benchmarks
{
    /// <summary>
    /// Measurement 5 (issue #28): unload verification under sustained hybrid
    /// operation.
    ///
    /// Repeated load/execute/unload cycles with leak detection around every
    /// cycle:
    /// - load: FilePluginLoader over PluginFolder + HybridPluginFactory
    ///   combining the traditional factory with a containerized-seam proxy
    ///   (in-memory orchestrator - the CRPCF dispatch path, no container)
    /// - execute: traditional and containerized-seam executions run
    ///   CONCURRENTLY each cycle (sustained hybrid operation)
    /// - unload: loader.UnloadAllPlugins() + Dispose + forced GC
    ///
    ///  Leak indicators tracked per cycle:
    /// - WeakReference to a plugin instance and to its plugin Type must die
    ///   after unload + GC
    /// - assemblies whose file location is under PluginFolder must return to
    ///   the pre-load baseline count
    /// - managed memory (GC.GetTotalMemory) delta vs. baseline and GC
    ///   collection counts, recorded raw per cycle
    /// - the AssemblyLoadContext of every resident plugin assembly is
    ///   recorded (Default vs. collectible plugin contexts)
    ///
    /// ONE PHASE is measured: the shipped configuration (FilePluginLoader with
    /// its default wiring - the loader ctor falls back to the real
    /// DefaultPluginSecurityValidator). Since issue #51 the validator's
    /// ValidateAssemblyAsync is METADATA-ONLY (PEReader + MetadataReader, no
    /// ALC load): plugin assemblies load ONLY into the collectible
    /// PluginLoadContext and UnloadAllPlugins actually unloads them.
    ///
    /// History (both committed pre-fix runs document it): through the
    /// 2026-11-23 runs the shipped validator called Assembly.LoadFrom(path),
    /// pinning every validated plugin assembly in the DEFAULT,
    /// non-collectible AssemblyLoadContext; FilePluginLoader then reused that
    /// pinned copy instead of its collectible PluginLoadContext copy, so
    /// UnloadAllPlugins was a no-op for the plugin assemblies actually in use,
    /// and every measured cycle leaked (see data/20261116_sandbox/m5.json and
    /// data/20261123_sandbox/m5.json). A second "collectiblePath" phase using
    /// a non-loading stub validator isolated the collectible ALC machinery
    /// back then and proved it leak-free; the stub was REMOVED with the fix,
    /// because the shipped configuration now exercises the collectible path
    /// itself.
    /// </summary>
    public static class M5UnloadVerification
    {
        public static async Task<Dictionary<string, object>> RunAsync(string pluginPath, int cycles)
        {
            var report = new Dictionary<string, object>
            {
                ["measurement"] = "m5-unload-verification",
                ["description"] = "Repeated load/execute/unload cycles under concurrent traditional + containerized-seam (hybrid) execution; per-cycle leak detection via WeakReference survival, resident plugin assemblies (with their AssemblyLoadContext), managed memory and GC collection counts. Single phase: the shipped configuration (default loader wiring, metadata-only security validation since issue #51 - plugin assemblies load only into the collectible PluginLoadContext)."
            };

            report["shippedConfiguration"] = await RunPhaseAsync(pluginPath, cycles);

            report["summary"] = new Dictionary<string, object>
            {
                ["shippedConfigurationVerdict"] = ((Dictionary<string, object>)report["shippedConfiguration"])["summary"],
                ["architecturalFinding"] =
                    "Verified fixed (issue #51): DefaultPluginSecurityValidator.ValidateAssemblyAsync is metadata-only (PEReader + MetadataReader) and no longer loads the plugin file into any AssemblyLoadContext during validation, so plugin assemblies load only into the collectible PluginLoadContext and UnloadAllPlugins actually unloads them. Pre-fix behavior (Assembly.LoadFrom pinning in the DEFAULT ALC, loader reuse of the pinned copy, UnloadAllPlugins a no-op for in-use assemblies) is documented by the committed data/20261116_sandbox and data/20261123_sandbox runs.",
                ["verdictScope"] = "plugin implementation assemblies (the collectible ALC path); the shared plugin interface assembly (PluginImplementations.dll) intentionally resides in the default context in all phases",
                ["hybridExecutionNote"] = "Containerized-seam executions use an in-memory orchestrator (CRPCF dispatch path only, no container); a real container backend adds its own lifecycle that this measurement does not cover"
            };

            return report;
        }

        private static async Task<Dictionary<string, object>> RunPhaseAsync(string pluginPath, int cycles)
        {
            var pluginFolder = Path.GetFullPath(pluginPath);
            cycles = Math.Max(5, cycles);

            var phase = new Dictionary<string, object>
            {
                ["validator"] = "DefaultPluginSecurityValidator (loader default wiring, metadata-only since issue #51)"
            };

            // ------------------------------------------------------------------
            // Baseline (before any load)
            // ------------------------------------------------------------------
            M1RawThresholdData.ForceGc();
            var baselineAssemblies = ResidentPluginAssemblies(pluginFolder);
            var baselineMemory = GC.GetTotalMemory(true);
            var gen0Baseline = GC.CollectionCount(0);
            var gen1Baseline = GC.CollectionCount(1);
            var gen2Baseline = GC.CollectionCount(2);

            phase["baseline"] = new Dictionary<string, object>
            {
                ["residentPluginAssemblies"] = baselineAssemblies,
                ["totalAssemblies"] = AppDomain.CurrentDomain.GetAssemblies().Length,
                ["managedMemoryBytes"] = baselineMemory
            };

            var perCycle = new List<Dictionary<string, object>>();
            var traditionalExecsPerCycle = 40;
            var seamExecsPerCycle = 40;

            for (var cycle = 0; cycle < cycles; cycle++)
            {
                // ------------------------------------------------------------------
                // Load: fresh loader + traditional factory + hybrid factory
                // ------------------------------------------------------------------
                var sw = Stopwatch.StartNew();

                var loader = M1RawThresholdData.CreateLoader(pluginPath);
                var traditional = new PluginClassFactory<ISpecificInterface>(loader);
                await M1RawThresholdData.AllowAllAsync(traditional);

                var plugins = await traditional.GetPossiblePlugins();
                if (!plugins.Any())
                {
                    throw new InvalidOperationException("No plugins discovered - PluginFolder not populated");
                }
                var plugin = plugins.First();

                var pluginId = new PluginIdentifier
                {
                    Namespace = new NamespaceString(plugin.moduleName.ToString()),
                    Name = new IdentifierString(plugin.pluginName.ToString()),
                    Version = new SemanticVersionNumber(1, 2, 1)
                };

                using var orchestrator = new InMemoryOrchestrator().WithPlugin(pluginId);
                var hybrid = new HybridPluginFactory<ISpecificInterface>(
                    traditional, orchestrator, NullLogger<HybridPluginFactory<ISpecificInterface>>.Instance,
                    new HybridPluginFactoryOptions { PreferContainerized = false });

                var traditionalInstance = traditional.GetInstance(plugin.moduleName, plugin.pluginName)
                    ?? throw new InvalidOperationException("GetInstance returned null");

                var seamProxy = ContainerizedPluginProxyFactory.Create<ISpecificInterface>(
                    orchestrator, (await orchestrator.ListPluginsAsync()).First());

                // Leak probes: weak references to the plugin instance and its
                // plugin Type (the type object lives in the plugin's ALC).
                var instanceWeakRef = new WeakReference(traditionalInstance);
                var typeWeakRef = new WeakReference(traditionalInstance.GetType());

                var loadMs = sw.Elapsed.TotalMilliseconds;

                // ------------------------------------------------------------------
                // Execute: sustained hybrid operation (traditional and
                // containerized-seam concurrently)
                // ------------------------------------------------------------------
                sw.Restart();
                var traditionalTask = Task.Run(() =>
                {
                    for (var i = 0; i < traditionalExecsPerCycle; i++)
                    {
                        _ = traditionalInstance.Execute($"m5-cycle-{cycle}-traditional-{i}");
                    }
                });

                var seamTask = Task.Run(async () =>
                {
                    for (var i = 0; i < seamExecsPerCycle; i++)
                    {
                        _ = await hybrid.GetPluginAsync(pluginId.Namespace, pluginId.Name);
                        _ = seamProxy.ExecuteTyped(Harness.BenchExecutionContext.Instance, "Monster");
                    }
                });

                await Task.WhenAll(traditionalTask, seamTask);
                var executeMs = sw.Elapsed.TotalMilliseconds;

                // ------------------------------------------------------------------
                // Unload + force GC until the weak refs die (bounded attempts)
                // ------------------------------------------------------------------
                sw.Restart();
                loader.UnloadAllPlugins();
                loader.Dispose();
                traditional.Dispose();

                // Drop every harness-side root before forcing GC.
                traditionalInstance = null!;
                seamProxy = null;
                hybrid = null!;
                traditional = null!;
                loader = null!;

                var gcAttempts = 0;
                const int maxGcAttempts = 10;
                while (gcAttempts < maxGcAttempts &&
                       (instanceWeakRef.IsAlive || typeWeakRef.IsAlive))
                {
                    M1RawThresholdData.ForceGc();
                    gcAttempts++;
                }
                var unloadMs = sw.Elapsed.TotalMilliseconds;

                var residentAssemblies = ResidentPluginAssemblies(pluginFolder);
                var residentDetail = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location) &&
                                a.Location.StartsWith(pluginFolder, StringComparison.OrdinalIgnoreCase))
                    .Select(a => new Dictionary<string, object>
                    {
                        ["assembly"] = Path.GetFileName(a.Location),
                        ["loadContext"] = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(a)?.ToString(),
                        ["isCollectibleContext"] = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(a)?.IsCollectible ?? false
                    })
                    .ToList();

                var memory = GC.GetTotalMemory(true);
                var cycleReport = new Dictionary<string, object>
                {
                    ["cycle"] = cycle,
                    ["loadMs"] = loadMs,
                    ["executeMs"] = executeMs,
                    ["unloadMs"] = unloadMs,
                    ["cycleTotalMs"] = loadMs + executeMs + unloadMs,
                    ["traditionalExecutions"] = traditionalExecsPerCycle,
                    ["containerizedSeamExecutions"] = seamExecsPerCycle,
                    ["gcAttemptsUntilCollected"] = gcAttempts,
                    ["pluginInstanceWeakRefAlive"] = instanceWeakRef.IsAlive,
                    ["pluginTypeWeakRefAlive"] = typeWeakRef.IsAlive,
                    ["residentPluginAssemblies"] = residentAssemblies,
                    ["residentPluginAssembliesAboveBaseline"] = residentAssemblies - baselineAssemblies,
                    ["residentPluginAssemblyDetail"] = residentDetail,
                    ["totalAssemblies"] = AppDomain.CurrentDomain.GetAssemblies().Length,
                    ["managedMemoryBytes"] = memory,
                    ["managedMemoryDeltaBytesVsBaseline"] = memory - baselineMemory,
                    ["gen0CollectionsDelta"] = GC.CollectionCount(0) - gen0Baseline,
                    ["gen1CollectionsDelta"] = GC.CollectionCount(1) - gen1Baseline,
                    ["gen2CollectionsDelta"] = GC.CollectionCount(2) - gen2Baseline
                };

                cycleReport["leakDetected"] =
                    instanceWeakRef.IsAlive ||
                    typeWeakRef.IsAlive ||
                    residentAssemblies > baselineAssemblies;

                perCycle.Add(cycleReport);
                Console.WriteLine(
                    $"[m5] cycle {cycle + 1}/{cycles}: weakRefsAlive={instanceWeakRef.IsAlive || typeWeakRef.IsAlive} resident={residentAssemblies} gcAttempts={gcAttempts} leak={cycleReport["leakDetected"]}");
            }

            phase["cycles"] = perCycle;

            // ------------------------------------------------------------------
            // Aggregate verdict
            // ------------------------------------------------------------------
            var leakedCycles = perCycle.Where(c => (bool)c["leakDetected"]).Select(c => c["cycle"]).ToArray();
            var maxResident = perCycle.Max(c => (int)c["residentPluginAssembliesAboveBaseline"]);
            var firstMemory = (long)perCycle[0]["managedMemoryBytes"];
            var lastMemory = (long)perCycle[^1]["managedMemoryBytes"];

            phase["summary"] = new Dictionary<string, object>
            {
                ["cyclesCompleted"] = perCycle.Count,
                ["cyclesWithLeakIndicators"] = leakedCycles,
                ["maxResidentPluginAssembliesAboveBaseline"] = maxResident,
                ["allPluginWeakRefsCollected"] = perCycle.All(c => !(bool)c["pluginInstanceWeakRefAlive"] && !(bool)c["pluginTypeWeakRefAlive"]),
                ["managedMemoryDeltaFirstToLastCycleBytes"] = lastMemory - firstMemory,
                ["verdict"] = leakedCycles.Length == 0
                    ? "no leak indicators: every cycle's plugin instances, types and assemblies were collected after unload"
                    : $"leak indicators in {leakedCycles.Length} cycles - see cycles for which leak indicator fired and the involved AssemblyLoadContexts"
            };

            return phase;
        }

        private static int ResidentPluginAssemblies(string pluginFolder)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Count(a => !a.IsDynamic &&
                            !string.IsNullOrEmpty(a.Location) &&
                            a.Location.StartsWith(pluginFolder, StringComparison.OrdinalIgnoreCase));
        }

    }
}
