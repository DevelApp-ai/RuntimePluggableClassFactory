using System;
using System.Collections.Generic;
using System.Linq;

namespace RuntimePluggableClassFactory.Benchmarks.Harness
{
    /// <summary>
    /// A set of raw measured values with summary statistics.
    /// Raw per-sample values are always preserved in the report so the
    /// distribution (not just a mean) is inspectable by third parties
    /// (ground rule 4 of the measurement program: reproducibility).
    /// </summary>
    public sealed class SampleSet
    {
        public string Unit { get; }
        public IReadOnlyList<double> Raw { get; }

        public SampleSet(string unit, IReadOnlyList<double> raw)
        {
            Unit = unit;
            Raw = raw;
        }

        public double Min => Raw.Min();
        public double Max => Raw.Max();
        public double Mean => Raw.Average();
        public double Median => Percentile(50);
        public double P90 => Percentile(90);
        public double P95 => Percentile(95);
        public double P99 => Percentile(99);

        public double Stdev
        {
            get
            {
                if (Raw.Count < 2)
                {
                    return 0;
                }
                var mean = Mean;
                return Math.Sqrt(Raw.Sum(v => (v - mean) * (v - mean)) / (Raw.Count - 1));
            }
        }

        public double Percentile(int p)
        {
            if (Raw.Count == 0)
            {
                throw new InvalidOperationException("No samples");
            }
            var sorted = Raw.OrderBy(v => v).ToArray();
            // Nearest-rank percentile (simple, deterministic, documented)
            var rank = (int)Math.Ceiling(p / 100.0 * sorted.Length);
            if (rank < 1) rank = 1;
            if (rank > sorted.Length) rank = sorted.Length;
            return sorted[rank - 1];
        }

        public Dictionary<string, object> ToSummary()
        {
            return new Dictionary<string, object>
            {
                ["unit"] = Unit,
                ["n"] = Raw.Count,
                ["min"] = Min,
                ["mean"] = Mean,
                ["median"] = Median,
                ["p90"] = P90,
                ["p95"] = P95,
                ["p99"] = P99,
                ["max"] = Max,
                ["stdev"] = Stdev,
                ["raw"] = Raw.ToArray()
            };
        }
    }

    /// <summary>
    /// Utility for measuring scenarios with Stopwatch and collecting raw samples.
    /// </summary>
    public static class Measure
    {
        public static SampleSet Collect(string unit, int iterations, Action<int> scenario)
        {
            var samples = new List<double>(iterations);
            for (var i = 0; i < iterations; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                scenario(i);
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }
            return new SampleSet(unit, samples);
        }

        public static async System.Threading.Tasks.Task<SampleSet> CollectAsync(string unit, int iterations, Func<int, System.Threading.Tasks.Task> scenario)
        {
            var samples = new List<double>(iterations);
            for (var i = 0; i < iterations; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await scenario(i);
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }
            return new SampleSet(unit, samples);
        }

        /// <summary>
        /// Warm-up invocation excluded from measurement (JIT + first-use effects
        /// are a separate, documented phenomenon; scenarios measure steady state).
        /// </summary>
        public static void Warmup(int iterations, Action action)
        {
            for (var i = 0; i < iterations; i++)
            {
                action();
            }
        }
    }
}
