using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace RuntimePluggableClassFactory.Benchmarks.Harness
{
    /// <summary>
    /// Captures the hardware/software environment a measurement ran in,
    /// so the raw data can be interpreted and reproduced by a third party
    /// (measurement ground rule 4).
    /// </summary>
    public static class EnvironmentInfo
    {
        public static Dictionary<string, object> Capture(int seed, string configuration)
        {
            var info = new Dictionary<string, object>
            {
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["osDescription"] = RuntimeInformation.OSDescription,
                ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
                ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
                ["dotnetVersion"] = Environment.Version.ToString(),
                ["frameworkDescription"] = RuntimeInformation.FrameworkDescription,
                ["processorCount"] = Environment.ProcessorCount,
                ["configuration"] = configuration,
                ["seed"] = seed,
                ["invariantGlobalization"] = IsInvariantGlobalization(),
                ["machineName"] = Environment.MachineName
            };

            var cpu = ReadLinuxProc("/proc/cpuinfo", "model name");
            if (cpu != null)
            {
                info["cpuModel"] = cpu;
            }
            else
            {
                var procId = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
                if (!string.IsNullOrEmpty(procId))
                {
                    info["cpuModel"] = procId;
                }
            }

            var mem = ReadLinuxMeminfo();
            if (mem != null)
            {
                info["totalMemoryMb"] = mem;
            }

            return info;
        }

        private static bool IsInvariantGlobalization()
        {
            try
            {
                _ = new System.Globalization.CultureInfo("da-DK");
                return false;
            }
            catch (System.Globalization.CultureNotFoundException)
            {
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string? ReadLinuxProc(string path, string key)
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return null;
                }
                foreach (var line in File.ReadLines(path))
                {
                    if (line.StartsWith(key, StringComparison.OrdinalIgnoreCase) && line.Contains(':'))
                    {
                        return line[(line.IndexOf(':') + 1)..].Trim();
                    }
                }
            }
            catch (Exception)
            {
                // Best effort only; absence is recorded by omission
            }
            return null;
        }

        private static double? ReadLinuxMeminfo()
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return null;
                }
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    if (line.StartsWith("MemTotal", StringComparison.OrdinalIgnoreCase))
                    {
                        var kb = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        return Math.Round(double.Parse(kb[1]) / 1024.0, 0);
                    }
                }
            }
            catch (Exception)
            {
                // Best effort only
            }
            return null;
        }
    }

    /// <summary>
    /// Writes measurement reports as JSON (raw data) and Markdown (summary).
    /// </summary>
    public static class ReportWriter
    {
        public static void WriteJson(string path, Dictionary<string, object> report)
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            };
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(report, options));
        }

        public static void AppendMarkdownLine(string path, string line)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
