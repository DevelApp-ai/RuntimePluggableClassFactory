using DevelApp.RuntimePluggableClassFactory;
using DevelApp.RuntimePluggableClassFactory.Containerized;
using DevelApp.RuntimePluggableClassFactory.Containerized.Implementations;
using DevelApp.RuntimePluggableClassFactory.Containerized.Interfaces;
using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using Microsoft.Extensions.Logging.Abstractions;
using PluginImplementations;
using RuntimePluggableClassFactory.Benchmarks.Harness;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RuntimePluggableClassFactory.Benchmarks
{
    /// <summary>
    /// Measurement 3 (issue #28): migration study - promote ONE plugin from
    /// in-process to containerized(host) execution in a RUNNING system, then
    /// roll it back, recording (a) the effort of every step in wall-clock ms,
    /// (b) behavior preservation via contract-test parity between the two
    /// dispatch paths, and (c) the rollback path cost.
    ///
    /// The "running system" is one long-lived harness process holding a
    /// HybridPluginFactory over the traditional loader plus a (initially
    /// empty) orchestrator. Promotion and rollback happen without restarting
    /// it, through the shipped public API only:
    ///   DeployContainerizedPluginAsync + GetPluginAsync(Auto).
    ///
    /// The promoted plugin is SpecificClassImpl v1.2.1 (module Test, secret
    /// "Monster"), pinned to the same assembly on both dispatch paths so the
    /// parity comparison measures the DISPATCH, not version differences.
    ///
    /// SCOPE: the containerized target is the ProcessIsolatedOrchestrator over
    /// the PluginHost binary (see M2ContainerLeg scope notes). The promotion
    /// steps below are the framework-overhead steps; a real K8s promotion adds
    /// registry push, image pull and scheduling (not measured, not claimed).
    /// </summary>
    public static class M3MigrationStudy
    {
        private const string PayloadAssemblyDirName = "PluginImplementations_1_2_1";

        public static async Task<Dictionary<string, object>> RunAsync(string pluginPath, int iterations)
        {
            var report = new Dictionary<string, object>
            {
                ["measurement"] = "m3-migration-study",
                ["description"] =
                    "Promote SpecificClassImpl v1.2.1 (Test module) from in-process to containerized(host) " +
                    "execution in a running hybrid system; measure step effort, contract parity, and rollback.",
                ["scopeWarning"] =
                    "Containerized target = local plugin host process (see m2-container-leg scope). No K8s/Docker " +
                    "orchestration in this environment; registry push/pull, scheduling and image build add on top " +
                    "in a real promotion and are explicitly NOT claimed here.",
                ["promotedPlugin"] = "Test.SpecificClassImpl v1.2.1 (same assembly on both paths)"
            };

            var hostDll = M2ContainerLeg.FindHostBinary()
                ?? throw new InvalidOperationException("PluginHost binary not found - build the solution first.");

            // Pin the promoted payload to v1.2.1 so both paths run the same code
            var pluginDll = Directory.GetFiles(Path.Combine(pluginPath, PayloadAssemblyDirName), "*.dll",
                    SearchOption.TopDirectoryOnly)
                .FirstOrDefault() ?? throw new InvalidOperationException(
                    $"Plugin assembly for {PayloadAssemblyDirName} not found in plugin folder.");

            var steps = new List<Dictionary<string, object>>();
            var contract = ContractCases();
            var parity = new List<Dictionary<string, object>>();

            // ------------------------------------------------------------------
            // Running system: one hybrid factory, no deployment yet
            // ------------------------------------------------------------------
            using var orchestrator = new Harness.ProcessIsolatedOrchestrator(hostDll);
            using var baseLoader = new DevelApp.RuntimePluggableClassFactory.FilePlugin.FilePluginLoader<IPluginClass>(
                new Uri(Path.GetFullPath(pluginPath)));
            var traditional = new PluginClassFactory<IPluginClass>(baseLoader);

            // Pin the promoted plugin to SpecificClassImpl v1.2.1 by its ASSEMBLY
            // (Location), and reuse the loader-produced version object. Only
            // that one version is allowed+loaded: the factory's version store
            // retains just retainOldVersions (=1 default) versions per plugin,
            // so allowing all four shipped versions would evict 1.2.1 and the
            // pinned lookup would fail. One allowed version = a running system
            // that has exactly one version of the plugin, which is also the
            // realistic migration starting point.
            var pluginEntry = (await traditional.GetPossiblePlugins())
                .First(p => p.pluginName.ToString() == "SpecificClassImpl"
                          && p.Type.Assembly.Location.EndsWith("PluginImplementations_1_2_1.dll"));
            traditional.AllowPlugin(pluginEntry.moduleName, pluginEntry.pluginName, pluginEntry.version);
            await traditional.RefreshPluginsAsync();

            var pluginId = new PluginIdentifier
            {
                Namespace = pluginEntry.moduleName,
                Name = pluginEntry.pluginName,
                Version = pluginEntry.version
            };

            var hybrid = new HybridPluginFactory<IPluginClass>(
                traditional, orchestrator,
                new Microsoft.Extensions.Logging.Abstractions.NullLogger<HybridPluginFactory<IPluginClass>>(),
                new HybridPluginFactoryOptions { PreferContainerized = true });

            var ns = pluginId.Namespace;
            var name = pluginId.Name;
            var version = pluginId.Version;

            // Step 0: baseline resolution (containerized miss -> traditional)
            var sw = Stopwatch.StartNew();
            var resolved = await hybrid.GetPluginAsync(ns, name, version);
            sw.Stop();
            if (resolved == null)
            {
                throw new InvalidOperationException("Baseline Auto lookup returned null.");
            }
            var baselineMode = resolved is ContainerizedPluginProxy<IPluginClass> ? "containerized" : "traditional";
            steps.Add(Step("0-baseline-auto-resolution", sw.Elapsed.TotalMilliseconds,
                $"First Auto lookup (v{version}) with an empty orchestrator: registry miss + traditional fallback " +
                $"(resolved {baselineMode})."));

            // Baseline contract parity (in-process path, version-pinned)
            foreach (var c in contract)
            {
                var outcome = ExecuteInProcess(resolved, c.Input);
                parity.Add(Case(c, outcome, "baseline"));
            }

            // ------------------------------------------------------------------
            // Promotion step 1: package + deploy the plugin to the orchestrator
            // ------------------------------------------------------------------
            var deploy = await M2ContainerLeg.DeployAsync(orchestrator, pluginDll, pluginId);
            if (!deploy.result.Success)
            {
                throw new InvalidOperationException("Promotion deploy failed: " + string.Join("; ", deploy.result.Errors));
            }
            steps.Add(Step("1-deploy-containerized-plugin", deploy.elapsedMs,
                $"DeployPluginAsync: package staging ({orchestrator.LastDeployment.StagingMs:F1} ms) + host spawn-to-ready " +
                $"({orchestrator.LastDeployment.SpawnToReadyMs:F1} ms). The v1.2.1 plugin assembly stream is the " +
                "deployment payload (stands in for the signed plugin package; signature verification not exercised)."));

            // Step 2: health check the deployment
            sw.Restart();
            var health = await orchestrator.GetPluginHealthAsync(pluginId);
            sw.Stop();
            steps.Add(Step("2-health-check", sw.Elapsed.TotalMilliseconds,
                $"GetPluginHealthAsync: {health.Status}, ping round trip {orchestrator.LastPingRoundTripMs:F3} ms."));

            // Step 3: switchover - next Auto lookup resolves containerized
            sw.Restart();
            var promoted = await hybrid.GetPluginAsync(ns, name, version);
            sw.Stop();
            if (promoted == null)
            {
                throw new InvalidOperationException("Post-deploy Auto lookup returned null.");
            }
            var promotedIsProxy = promoted is ContainerizedPluginProxy<IPluginClass>;
            steps.Add(Step("3-switchover-auto-lookup", sw.Elapsed.TotalMilliseconds,
                $"Next GetPluginAsync(Auto) resolved {(promotedIsProxy ? "containerized proxy" : "traditional (UNEXPECTED)")}. " +
                "No call-site change: same method, same signature; only the resolution target changed."));

            // ------------------------------------------------------------------
            // Behavior preservation: contract parity on the containerized path
            // ------------------------------------------------------------------
            Measure.Warmup(5, () => _ = ExecuteContainerized(promoted, "Monster"));
            foreach (var c in contract)
            {
                var outcome = ExecuteContainerized(promoted, c.Input);
                parity.Add(Case(c, outcome, "promoted"));
            }

            // Latency of both paths for the same contract cases (deviation only;
            // absolute latencies live in the m2-container-leg report)
            var inProcLatencies = new List<double>();
            var containerLatencies = new List<double>();
            var inProcResolved = await hybrid.GetPluginAsync(ns, name, version,
                executionMode: PluginExecutionMode.Traditional);
            foreach (var c in contract.Take(2)) // positive + case-sensitivity cases
            {
                for (var i = 0; i < Math.Max(iterations, 10); i++)
                {
                    var s1 = Stopwatch.StartNew();
                    _ = ExecuteInProcess(inProcResolved!, c.Input);
                    s1.Stop();
                    inProcLatencies.Add(s1.Elapsed.TotalMilliseconds);

                    var s2 = Stopwatch.StartNew();
                    _ = ExecuteContainerized(promoted, c.Input);
                    s2.Stop();
                    containerLatencies.Add(s2.Elapsed.TotalMilliseconds);
                }
            }
            report["contractCaseLatencyInProcess"] = new SampleSet("ms", inProcLatencies).ToSummary();
            report["contractCaseLatencyContainerized"] = new SampleSet("ms", containerLatencies).ToSummary();

            // ------------------------------------------------------------------
            // Rollback: undeploy, verify Auto falls back to traditional, parity
            // ------------------------------------------------------------------
            sw.Restart();
            await orchestrator.UndeployPluginAsync(pluginId);
            sw.Stop();
            steps.Add(Step("4-rollback-undeploy", sw.Elapsed.TotalMilliseconds,
                $"UndeployPluginAsync: host teardown ({orchestrator.LastUndeployMs:F1} ms)."));

            sw.Restart();
            var rolledBack = await hybrid.GetPluginAsync(ns, name, version);
            sw.Stop();
            if (rolledBack == null)
            {
                throw new InvalidOperationException("Rollback Auto lookup returned null.");
            }
            var rolledBackIsTraditional = rolledBack is not ContainerizedPluginProxy<IPluginClass>;
            steps.Add(Step("5-rollback-auto-lookup", sw.Elapsed.TotalMilliseconds,
                $"Auto lookup after undeploy resolved {(rolledBackIsTraditional ? "traditional (fallback restored)" : "containerized (UNEXPECTED)")}. " +
                "The running HybridPluginFactory instance itself was never restarted."));

            foreach (var c in contract)
            {
                var outcome = ExecuteInProcess(rolledBack, c.Input);
                parity.Add(Case(c, outcome, "rolled-back"));
            }

            // ------------------------------------------------------------------
            // Reporting
            // ------------------------------------------------------------------
            var baselinePass = parity.Where(p => p["after"]!.Equals("baseline")).All(p => (bool)p["matchesExpected"]!);
            var promotedPass = parity.Where(p => p["after"]!.Equals("promoted")).All(p => (bool)p["matchesExpected"]!);
            var rolledBackPass = parity.Where(p => p["after"]!.Equals("rolled-back")).All(p => (bool)p["matchesExpected"]!);

            report["migrationSteps"] = steps;
            report["contractParity"] = parity;
            report["paritySummary"] = new Dictionary<string, object>
            {
                ["contractCaseCount"] = contract.Count,
                ["baselineParityPass"] = baselinePass,
                ["promotedParityPass"] = promotedPass,
                ["rolledBackParityPass"] = rolledBackPass,
                ["behaviorPreserved"] = baselinePass && promotedPass,
                ["normalizationNote"] =
                    "The containerized path returns JSON-decoded output; the in-process path returns the CLR bool. " +
                    "Outcomes are normalized to true/false before comparison and recorded raw as well, so a third " +
                    "party can audit the comparison."
            };
            report["effortSummary"] = new Dictionary<string, object>
            {
                ["callSiteLinesChanged"] = 0,
                ["callSiteNote"] =
                    "Both paths are consumed through HybridPluginFactory.GetPluginAsync with the same signature; " +
                    "the consumer code that resolves the plugin did not change for the promotion.",
                ["configDelta"] = "HybridPluginFactoryOptions.PreferContainerized = true (1 property), already set before promotion",
                ["promotionCodePath"] = "HybridPluginFactory.DeployContainerizedPluginAsync (shipped API), 1 call",
                ["containerHostBinaryLoc"] = CountLoc(
                    Path.Combine(FindRepoRoot() ?? ".", "RuntimePluggableClassFactory.PluginHost", "Program.cs")),
                ["rollbackCodePath"] = "UndeployPluginAsync (shipped orchestrator API), 1 call, no restart",
                ["stepsMeasured"] = steps.Count,
                ["totalStepWallMs"] = steps.Sum(s => (double)s["ms"]!)
            };

            traditional.Dispose();
            return report;
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        /// <summary>
        /// Word-guess contract for SpecificClassImpl v1.2.1: Execute returns
        /// true exactly for the secret "Monster" (ordinal, case-sensitive).
        /// </summary>
        private static List<(string Input, bool Expected)> ContractCases() => new()
        {
            ("Monster", true),              // positive case
            ("monster", false),             // case sensitivity
            ("", false),                    // empty payload
            ("Monste", false),              // near miss
            ("Monster ", false),            // trailing whitespace
            ("Möñster", false),             // non-ASCII input (encoding boundary)
            (new string('x', 10_240), false),   // 10 KB payload
            (new string('x', 102_400), false)   // 100 KB payload
        };

        private static string ExecuteInProcess(IPluginClass plugin, string input)
        {
            if (plugin is ISpecificInterface specific)
            {
                return Normalize(specific.Execute(input));
            }
            return $"<unavailable: {plugin.GetType().Name} is not ISpecificInterface>";
        }

        private static string ExecuteContainerized(IPluginClass plugin, string input)
        {
            if (plugin is ContainerizedPluginProxy<IPluginClass> proxy)
            {
                var result = proxy.ExecuteTyped(BenchExecutionContext.Instance, input);
                return result.Success
                    ? Normalize(result.Data)
                    : $"<proxy-failure: {result.ErrorMessage}>";
            }
            return $"<unavailable: {plugin.GetType().Name} is not a containerized proxy>";
        }

        /// <summary>
        /// Normalizes outcomes across the two paths: the containerized proxy
        /// deserializes JSON, so a bool may arrive as the string "True" or as
        /// a JsonElement depending on the decode path.
        /// </summary>
        private static string Normalize(object? value)
        {
            var s = value?.ToString() ?? "<null>";
            return s.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true"
                 : s.Equals("false", StringComparison.OrdinalIgnoreCase) ? "false"
                 : s;
        }

        private static Dictionary<string, object> Step(string id, double ms, string note) => new()
        {
            ["step"] = id,
            ["ms"] = ms,
            ["note"] = note
        };

        private static Dictionary<string, object> Case((string Input, bool Expected) c,
            string outcome, string after) => new()
        {
            // Large generated payloads are recorded as compact descriptors
            // (length + character): the full input is deterministically
            // reproducible from ContractCases() in the committed harness
            // source, and verbatim copies would bloat the report ~5x.
            ["case"] = c.Input.Length > 128
                ? $"payload-{c.Input.Length}B('{c.Input[0]}')"
                : c.Input,
            ["expected"] = Normalize(c.Expected),
            ["after"] = after,
            ["result"] = outcome,
            ["matchesExpected"] = outcome == Normalize(c.Expected)
        };

        private static string? FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var current = dir; current != null; current = current.Parent)
            {
                if (File.Exists(Path.Combine(current.FullName, "RuntimePluggableClassFactory.sln")))
                {
                    return current.FullName;
                }
            }
            return null;
        }

        private static int CountLoc(string path)
        {
            if (!File.Exists(path))
            {
                return -1;
            }
            return File.ReadAllLines(path)
                .Count(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("//"));
        }
    }
}
