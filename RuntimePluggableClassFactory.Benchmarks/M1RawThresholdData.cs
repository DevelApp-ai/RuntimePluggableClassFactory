using DevelApp.RuntimePluggableClassFactory;
using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.RuntimePluggableClassFactory.FilePlugin;
using DevelApp.RuntimePluggableClassFactory.Security;
using RuntimePluggableClassFactory.Benchmarks.Harness;
using PluginImplementations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RuntimePluggableClassFactory.Benchmarks
{
    /// <summary>
    /// Measurement 1 (issue #28): raw benchmark data behind the performance
    /// threshold assertions that the xUnit suite enforces.
    ///
    /// The suite asserts thresholds (discovery &lt; 5 s, instantiation &lt; 100 ms avg,
    /// execution &lt; 10 ms avg, &gt; 100 exec/s concurrent, security validation
    /// &lt; 500 ms avg, memory growth &lt; 50 MB, typed execution &lt; 20 ms avg,
    /// load/unload cycle &lt; 2 s avg). This module re-runs the same scenarios with
    /// larger sample counts and records the raw per-iteration values so the
    /// article can state a measured distribution instead of a pass/fail verdict.
    ///
    /// Scenarios intentionally mirror PerformanceTests.cs (same plugin corpus,
    /// same security settings, same default policy) so the numbers are directly
    /// comparable to the enforced thresholds.
    /// </summary>
    public static class M1RawThresholdData
    {
        public static async Task<Dictionary<string, object>> RunAsync(string pluginPath, int iterations)
        {
            var report = new Dictionary<string, object>
            {
                ["measurement"] = "m1-raw-threshold-data",
                ["description"] = "Raw distributions behind the test-enforced performance thresholds (PerformanceTests.cs scenarios, Default security policy)"
            };

            // ------------------------------------------------------------------
            // Scenario 1: plugin discovery
            // ------------------------------------------------------------------
            {
                var loader = CreateLoader(pluginPath);
                var factory = new PluginClassFactory<ISpecificInterface>(loader);
                var samples = new List<double>();
                for (var i = 0; i < iterations; i++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    await factory.RefreshPluginsAsync();
                    var plugins = await factory.GetPossiblePlugins();
                    sw.Stop();
                    samples.Add(sw.Elapsed.TotalMilliseconds);
                    if (!plugins.Any())
                    {
                        throw new InvalidOperationException("No plugins discovered - PluginFolder not populated");
                    }
                }
                report["discovery"] = new SampleSet("ms", samples).ToSummary();
                report["discoveryThresholdMs"] = 5000.0;
                loader.Dispose();
            }

            // ------------------------------------------------------------------
            // Scenario 2: plugin instantiation (steady state after refresh)
            // ------------------------------------------------------------------
            {
                using var loader = CreateLoader(pluginPath);
                var factory = new PluginClassFactory<ISpecificInterface>(loader);
                await AllowAllAsync(factory);
                var plugins = await factory.GetPossiblePlugins();
                var plugin = plugins.First();

                // Warm-up (JIT), excluded from measurement
                Measure.Warmup(20, () => _ = factory.GetInstance(plugin.moduleName, plugin.pluginName));

                var samples = new List<double>();
                for (var i = 0; i < iterations * 10; i++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var instance = factory.GetInstance(plugin.moduleName, plugin.pluginName);
                    sw.Stop();
                    if (instance == null)
                    {
                        throw new InvalidOperationException("GetInstance returned null");
                    }
                    samples.Add(sw.Elapsed.TotalMilliseconds);
                }
                report["instantiation"] = new SampleSet("ms", samples).ToSummary();
                report["instantiationThresholdMeanMs"] = 100.0;
                report["instantiationThresholdMaxMs"] = 500.0;
            }

            // ------------------------------------------------------------------
            // Scenario 3: single plugin execution (in-process)
            // ------------------------------------------------------------------
            {
                using var loader = CreateLoader(pluginPath);
                var factory = new PluginClassFactory<ISpecificInterface>(loader);
                await AllowAllAsync(factory);
                var plugins = await factory.GetPossiblePlugins();
                var plugin = plugins.First();
                var instance = factory.GetInstance(plugin.moduleName, plugin.pluginName)
                    ?? throw new InvalidOperationException("GetInstance returned null");

                Measure.Warmup(100, () => instance.Execute("warmup"));

                var samples = new List<double>();
                for (var i = 0; i < iterations * 20; i++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    _ = instance.Execute($"m1-exec-{i}");
                    sw.Stop();
                    samples.Add(sw.Elapsed.TotalMilliseconds);
                }
                report["execution"] = new SampleSet("ms", samples).ToSummary();
                report["executionThresholdMeanMs"] = 10.0;
            }

            // ------------------------------------------------------------------
            // Scenario 4: concurrent execution throughput
            // ------------------------------------------------------------------
            {
                using var loader = CreateLoader(pluginPath);
                var factory = new PluginClassFactory<ISpecificInterface>(loader);
                await AllowAllAsync(factory);
                var plugins = await factory.GetPossiblePlugins();
                var plugin = plugins.First();
                _ = factory.GetInstance(plugin.moduleName, plugin.pluginName); // warm-up

                const int concurrentTasks = 50;
                const int executionsPerTask = 10;

                var samples = new List<double>();
                var totalSamples = new List<double>();
                for (var batch = 0; batch < Math.Max(5, iterations / 6); batch++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var tasks = new Task[concurrentTasks];
                    for (var i = 0; i < concurrentTasks; i++)
                    {
                        var taskId = i;
                        tasks[i] = Task.Run(() =>
                        {
                            for (var j = 0; j < executionsPerTask; j++)
                            {
                                var instance = factory.GetInstance(plugin.moduleName, plugin.pluginName);
                                _ = instance?.Execute($"m1-conc-{taskId}-{j}");
                            }
                        });
                    }
                    await Task.WhenAll(tasks);
                    sw.Stop();
                    var total = concurrentTasks * executionsPerTask;
                    samples.Add(sw.Elapsed.TotalMilliseconds);
                    totalSamples.Add(total * 1000.0 / sw.Elapsed.TotalMilliseconds);
                }
                report["concurrentWallMs"] = new SampleSet("ms", samples).ToSummary();
                report["concurrentExecPerSec"] = new SampleSet("exec/sec", totalSamples).ToSummary();
                report["concurrentExecPerTask"] = concurrentTasks;
                report["concurrentExecutionsPerTask"] = executionsPerTask;
                report["concurrentThresholdExecPerSec"] = 100.0;
            }

            // ------------------------------------------------------------------
            // Scenario 5: security validation latency (Strict policy)
            // ------------------------------------------------------------------
            {
                var strictValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreateStrict());
                var testFile = Directory.GetFiles(pluginPath, "*.dll", SearchOption.AllDirectories)
                    .FirstOrDefault() ?? throw new InvalidOperationException("No plugin DLL found for security validation");

                await strictValidator.ValidateAssemblyAsync(testFile); // warm-up

                var samples = new List<double>();
                for (var i = 0; i < iterations * 10; i++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    _ = await strictValidator.ValidateAssemblyAsync(testFile);
                    sw.Stop();
                    samples.Add(sw.Elapsed.TotalMilliseconds);
                }
                report["securityValidation"] = new SampleSet("ms", samples).ToSummary();
                report["securityValidationThresholdMeanMs"] = 500.0;
                report["securityValidationThresholdMaxMs"] = 2000.0;
                report["securityValidationPolicy"] = "Strict";
            }

            // ------------------------------------------------------------------
            // Scenario 6: memory under load (1000 create/execute iterations
            // with periodic unload, mirroring the suite's memory scenario)
            // ------------------------------------------------------------------
            {
                using var loader = CreateLoader(pluginPath);
                var factory = new PluginClassFactory<ISpecificInterface>(loader);
                await AllowAllAsync(factory);
                var plugins = await factory.GetPossiblePlugins();

                var memoryDeltasMb = new List<double>();
                const int innerIterations = 100;
                var cycles = Math.Max(3, iterations / 10);
                for (var cycle = 0; cycle < cycles; cycle++)
                {
                    ForceGc();
                    var initial = GC.GetTotalMemory(false);
                    for (var i = 0; i < innerIterations; i++)
                    {
                        foreach (var plugin in plugins.Take(2))
                        {
                            var instance = factory.GetInstance(plugin.moduleName, plugin.pluginName);
                            if (instance != null)
                            {
                                _ = instance.Execute($"m1-mem-{cycle}-{i}");
                            }
                        }
                        if (i % 25 == 0)
                        {
                            loader.UnloadAllPlugins();
                            await factory.RefreshPluginsAsync();
                        }
                    }
                    loader.UnloadAllPlugins();
                    ForceGc();
                    var final = GC.GetTotalMemory(false);
                    memoryDeltasMb.Add((final - initial) / (1024.0 * 1024.0));
                }
                report["memoryGrowth"] = new SampleSet("MB", memoryDeltasMb).ToSummary();
                report["memoryGrowthThresholdMb"] = 50.0;
                report["memoryInnerIterations"] = innerIterations;
            }

            // ------------------------------------------------------------------
            // Scenario 7: typed plugin execution
            // ------------------------------------------------------------------
            {
                using var loader = new FilePluginLoader<ITypedSpecificInterface>(new Uri(Path.GetFullPath(pluginPath)));
                var typedFactory = new TypedPluginClassFactory<ITypedSpecificInterface, WordGuessInput, WordGuessOutput>(loader);
                await typedFactory.RefreshPluginsAsync();
                var plugins = await typedFactory.GetPossiblePlugins();
                var typedPlugin = plugins.FirstOrDefault(p => p.Type.GetInterfaces()
                    .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ITypedPluginClass<,>)));
                if (typedPlugin.Equals(default))
                {
                    throw new InvalidOperationException("No typed plugins discovered - PluginFolder not populated");
                }

                var input = new WordGuessInput { Word = "measurement", CaseSensitive = false };
                _ = typedFactory.ExecutePlugin(typedPlugin.ModuleName, typedPlugin.PluginName, input); // warm-up

                var samples = new List<double>();
                for (var i = 0; i < iterations * 10; i++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    _ = typedFactory.ExecutePlugin(typedPlugin.ModuleName, typedPlugin.PluginName, input);
                    sw.Stop();
                    samples.Add(sw.Elapsed.TotalMilliseconds);
                }
                report["typedExecution"] = new SampleSet("ms", samples).ToSummary();
                report["typedExecutionThresholdMeanMs"] = 20.0;
            }

            // ------------------------------------------------------------------
            // Scenario 8: load/unload cycle
            // ------------------------------------------------------------------
            {
                using var loader = CreateLoader(pluginPath);
                var factory = new PluginClassFactory<ISpecificInterface>(loader);

                var samples = new List<double>();
                var cycles = Math.Max(5, iterations / 6);
                for (var i = 0; i < cycles; i++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    await factory.RefreshPluginsAsync();
                    var plugins = await loader.ListAllPossiblePluginsAsync();
                    foreach (var plugin in plugins.Take(2))
                    {
                        var instance = factory.GetInstance(plugin.ModuleName, plugin.PluginName);
                        _ = instance?.Execute($"m1-cycle-{i}");
                    }
                    loader.UnloadAllPlugins();
                    sw.Stop();
                    samples.Add(sw.Elapsed.TotalMilliseconds);
                }
                report["loadUnloadCycle"] = new SampleSet("ms", samples).ToSummary();
                report["loadUnloadCycleThresholdMeanMs"] = 2000.0;
            }

            return report;
        }

        internal static FilePluginLoader<ISpecificInterface> CreateLoader(string pluginPath)
        {
            var securityValidator = new DefaultPluginSecurityValidator(PluginSecuritySettings.CreateDefault());
            return new FilePluginLoader<ISpecificInterface>(new Uri(Path.GetFullPath(pluginPath)), securityValidator);
        }

        internal static async Task AllowAllAsync(PluginClassFactory<ISpecificInterface> factory)
        {
            foreach (var possiblePlugin in await factory.GetPossiblePlugins())
            {
                factory.AllowPlugin(possiblePlugin.moduleName, possiblePlugin.pluginName, possiblePlugin.version);
            }
            await factory.RefreshPluginsAsync();
        }

        internal static void ForceGc()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
