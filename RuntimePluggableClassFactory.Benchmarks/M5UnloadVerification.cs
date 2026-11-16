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
    /// Leak indicators tracked per cycle:
    /// - WeakReference to a plugin instance and to its plugin Type must die
    ///   after unload + GC
    /// - assemblies whose file location is under PluginFolder must return to
    ///   the pre-load baseline count
    /// - managed memory (GC.GetTotalMemory) delta vs. baseline and GC
    ///   collection counts, recorded raw per cycle
    /// - the AssemblyLoadContext of every resident plugin assembly is
    ///   recorded (Default vs. collectible plugin contexts)
    ///
    /// TWO PHASES are measured, because the shipped wiring and the collectible
    /// machinery behave differently (see the findings recorded below):
    ///
    /// A) "shippedConfiguration": FilePluginLoader with its default wiring.
    ///    The loader always attaches a DefaultPluginSecurityValidator (ctor
    ///    falls back to one when none is passed), whose path-based
    ///    ValidateAssemblyAsync calls Assembly.LoadFrom(path). LoadFrom
    ///    loads the plugin assembly into the DEFAULT, non-collectible
    ///    AssemblyLoadContext, where assemblies can never be unloaded; the
    ///    loader then prefers that Default-ALC copy over its own collectible
    ///    PluginLoadContext copy (FilePluginLoader.cs, "Get assembly from
    ///    already loaded Default AssemblyLoadContext if possible"). In this
    ///    phase the collectible path is therefore not exercised and leak
    ///    indicators are EXPECTED: the measurement documents this honestly
    ///    rather than hiding it.
    ///
    /// B) "collectiblePath": FilePluginLoader with a non-loading stub
    ///    validator (NonLoadingSecurityValidator below) so plugin assemblies
    ///    load ONLY into the collectible PluginLoadContext (isCollectible:
    ///    true). This isolates the collectible ALC machinery the issue asks
    ///    about and verifies it does not leak. The stub validator performs
    ///    NO security checks - it exists purely to keep validation from
    ///    pinning assemblies into the Default ALC during this measurement
    ///    and is NOT a security configuration.
    /// </summary>
    public static class M5UnloadVerification
    {
        public static async Task<Dictionary<string, object>> RunAsync(string pluginPath, int cycles)
        {
            var report = new Dictionary<string, object>
            {
                ["measurement"] = "m5-unload-verification",
                ["description"] = "Repeated load/execute/unload cycles under concurrent traditional + containerized-seam (hybrid) execution; per-cycle leak detection via WeakReference survival, resident plugin assemblies (with their AssemblyLoadContext), managed memory and GC collection counts. Two phases: shipped configuration (documents expected leak) and collectible-path (stub validator, isolates the collectible ALC machinery)."
            };

            // Order matters: the collectible phase must run FIRST, because the
            // shipped configuration permanently pins plugin assemblies in the
            // default ALC and the loader then reuses those pinned copies in
            // any later cycle (they are not unloadable), which would defeat
            // the collectible-phase measurement.
            report["collectiblePath"] = await RunPhaseAsync(pluginPath, cycles, useDefaultValidator: false);
            report["shippedConfiguration"] = await RunPhaseAsync(pluginPath, cycles, useDefaultValidator: true);

            report["summary"] = new Dictionary<string, object>
            {
                ["shippedConfigurationVerdict"] = ((Dictionary<string, object>)report["shippedConfiguration"])["summary"],
                ["collectiblePathVerdict"] = ((Dictionary<string, object>)report["collectiblePath"])["summary"],
                ["architecturalFinding"] =
                    "In the shipped configuration every plugin assembly passes through DefaultPluginSecurityValidator.ValidateAssemblyAsync, which uses Assembly.LoadFrom(path) and therefore pins the assembly in the DEFAULT (non-collectible) AssemblyLoadContext; FilePluginLoader then reuses that pinned copy instead of its collectible PluginLoadContext copy. UnloadAllPlugins is consequently a no-op for the plugin assemblies actually in use, and the collectible ALC path is never exercised unless validation avoids loading the file. This is a measurable, reproducible property of the shipped code (see shippedConfiguration cycles), not a harness artifact.",
                ["verdictScope"] = "plugin implementation assemblies (the collectible ALC path); the shared plugin interface assembly (PluginImplementations.dll) intentionally resides in the default context in all phases",
                ["hybridExecutionNote"] = "Containerized-seam executions use an in-memory orchestrator (CRPCF dispatch path only, no container); a real container backend adds its own lifecycle that this measurement does not cover"
            };

            return report;
        }

        private static async Task<Dictionary<string, object>> RunPhaseAsync(string pluginPath, int cycles, bool useDefaultValidator)
        {
            var pluginFolder = Path.GetFullPath(pluginPath);
            cycles = Math.Max(5, cycles);

            var phase = new Dictionary<string, object>
            {
                ["validator"] = useDefaultValidator
                    ? "DefaultPluginSecurityValidator (loader default wiring)"
                    : "NonLoadingSecurityValidator (harness stub; performs NO security checks - measurement-only)"
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

                var loader = useDefaultValidator
                    ? M1RawThresholdData.CreateLoader(pluginPath)
                    : new FilePluginLoader<ISpecificInterface>(new Uri(Path.GetFullPath(pluginPath)), new NonLoadingSecurityValidator());
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
                    $"[m5{(useDefaultValidator ? "-shipped" : "-collectible")}] cycle {cycle + 1}/{cycles}: weakRefsAlive={instanceWeakRef.IsAlive || typeWeakRef.IsAlive} resident={residentAssemblies} gcAttempts={gcAttempts} leak={cycleReport["leakDetected"]}");
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
                    : $"leak indicators in all {leakedCycles.Length} cycles - plugin assemblies remain resident in the DEFAULT (non-collectible) AssemblyLoadContext; see architecturalFinding"
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

        /// <summary>
        /// Measurement-only stub: performs NO security checks. Exists so the
        /// collectible-path phase can measure the PluginLoadContext machinery
        /// without DefaultPluginSecurityValidator's Assembly.LoadFrom pinning
        /// plugin assemblies into the default ALC. NOT a security
        /// configuration - do not use outside this harness.
        /// </summary>
        private sealed class NonLoadingSecurityValidator : IPluginSecurityValidator
        {
            public Task<PluginSecurityValidationResult> ValidateAssemblyAsync(string? assemblyPath)
                => Task.FromResult(PluginSecurityValidationResult.CreateValid());

            public PluginSecurityValidationResult ValidateLoadedAssembly(Assembly? assembly)
                => PluginSecurityValidationResult.CreateValid();

            public PluginSecurityValidationResult ValidatePluginTypes(IEnumerable<Type>? pluginTypes)
                => PluginSecurityValidationResult.CreateValid();
        }
    }
}
