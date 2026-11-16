using RuntimePluggableClassFactory.Benchmarks.Harness;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RuntimePluggableClassFactory.Benchmarks
{
    /// <summary>
    /// CLI entry point for the issue #28 measurement harness.
    ///
    /// Examples:
    ///   dotnet run -c Release -- --measurement all --iterations 30 --seed 20261116
    ///   dotnet run -c Release -- --measurement m4 --out data/run
    ///
    /// Outputs, per measurement and in total:
    ///   &lt;out&gt;/&lt;measurement&gt;.json    raw report (committed raw data)
    ///   &lt;out&gt;/environment.json      hardware/software environment capture
    ///   &lt;out&gt;/summary.md             run header + one summary line per measurement
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var measurement = "all";
            var iterations = 30;
            var seed = 20261116;
            var outDir = DefaultOutDir();

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--measurement":
                    case "-m":
                        measurement = ArgValue(args, ref i);
                        break;
                    case "--iterations":
                    case "-n":
                        iterations = int.Parse(ArgValue(args, ref i));
                        break;
                    case "--seed":
                    case "-s":
                        seed = int.Parse(ArgValue(args, ref i));
                        break;
                    case "--out":
                    case "-o":
                        outDir = ArgValue(args, ref i);
                        break;
                    case "--help":
                    case "-h":
                        Console.WriteLine(
                            "Usage: dotnet run -c Release -- --measurement m1|m2|m4|m5|all [--iterations N] [--seed S] [--out DIR]");
                        return 0;
                    default:
                        Console.Error.WriteLine($"Unknown argument '{args[i]}'.");
                        return 2;
                }
            }

            return await RunAsync(measurement, iterations, seed, outDir);
        }

        private static string ArgValue(string[] args, ref int i)
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {args[i]}.");
            }
            i++;
            return args[i];
        }

        private static async Task<int> RunAsync(string measurement, int iterations, int seed, string outDir)
        {
            measurement = measurement.ToLowerInvariant();
            var valid = new[] { "m1", "m2", "m4", "m5", "all" };
            if (!valid.Contains(measurement))
            {
                Console.Error.WriteLine($"Unknown measurement '{measurement}'. Valid: {string.Join(", ", valid)}");
                return 2;
            }

            var pluginPath = FindPluginFolder();
            if (pluginPath == null)
            {
                Console.Error.WriteLine("PluginFolder not found (walked up from the executable directory). Build the solution first so the plugin projects populate it.");
                return 2;
            }

            Directory.CreateDirectory(outDir);
            var environment = EnvironmentInfo.Capture(seed, "Release");
            ReportWriter.WriteJson(Path.Combine(outDir, "environment.json"), environment);

            var summaryPath = Path.Combine(outDir, "summary.md");
            File.WriteAllText(summaryPath,
                $"# Measurement run {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC{Environment.NewLine}" +
                $"- Seed: {seed}{Environment.NewLine}" +
                $"- Iterations per scenario: {iterations}{Environment.NewLine}" +
                $"- PluginFolder: {Path.GetFileName(Path.GetDirectoryName(pluginPath) ?? pluginPath)}{Environment.NewLine}" +
                $"- Raw data: this directory (JSON), reproducible with the committed harness source and the recorded seed/iteration counts{Environment.NewLine}");

            Console.WriteLine($"[harness] measurement={measurement} iterations={iterations} seed={seed}");
            Console.WriteLine($"[harness] pluginFolder={pluginPath}");
            Console.WriteLine($"[harness] out={Path.GetFullPath(outDir)}");

            var selected = measurement == "all" ? new[] { "m1", "m2", "m4", "m5" } : new[] { measurement };
            foreach (var m in selected)
            {
                Console.WriteLine($"[harness] running {m} ...");
                Dictionary<string, object> report = m switch
                {
                    "m1" => await M1RawThresholdData.RunAsync(pluginPath, iterations),
                    "m2" => await M2ModeComparison.RunAsync(pluginPath, iterations),
                    "m4" => await M4SecurityEscapeRate.RunAsync(outDir),
                    "m5" => await M5UnloadVerification.RunAsync(pluginPath, iterations),
                    _ => throw new InvalidOperationException($"unreachable: {m}")
                };

                var jsonPath = Path.Combine(outDir, $"{m}.json");
                ReportWriter.WriteJson(jsonPath, report);
                ReportWriter.AppendMarkdownLine(summaryPath, $"- {m}: `{jsonPath}` ({DateTime.UtcNow:HH:mm:ss} UTC)");
                Console.WriteLine($"[harness] {m} written to {jsonPath}");
            }

            Console.WriteLine("[harness] done.");
            return 0;
        }

        /// <summary>
        /// Walks up from the executable location to find the repository's
        /// PluginFolder (same convention as the test suite, which resolves
        /// ....\..\PluginFolder relative to the test assembly).
        /// </summary>
        private static string? FindPluginFolder()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var current = dir; current != null; current = current.Parent)
            {
                var candidate = Path.Combine(current.FullName, "PluginFolder");
                if (Directory.Exists(candidate) && Directory.GetDirectories(candidate).Length > 0)
                {
                    return candidate;
                }
            }
            return null;
        }

        private static string DefaultOutDir() =>
            Path.Combine("data", $"{DateTime.UtcNow:yyyyMMdd}_{Guid.NewGuid().ToString()[..8]}");
    }
}
