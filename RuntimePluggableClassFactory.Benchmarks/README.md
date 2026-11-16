# RuntimePluggableClassFactory.Benchmarks

Measurement harness for [issue #28](https://github.com/DevelApp-ai/RuntimePluggableClassFactory/issues/28):
committed, reproducible data for the five measurements required by the
*hybrid-plugin-execution-modular-monolith* article (Research-article-builder#27 ground rules: every
number in the article must trace to committed raw data with environment, seeds and a producing
script).

The harness is a plain console app (`net8.0`, `IsPackable=false`) added to the solution. CI builds
it but does not run it (CI test steps cover only the two test projects).

## Running

```bash
dotnet build RuntimePluggableClassFactory.sln -c Release   # populates PluginFolder via post-build copies
dotnet run -c Release --project RuntimePluggableClassFactory.Benchmarks -- \
  --measurement all --iterations 30 --seed 20261116 --out data/my_run
```

Options: `--measurement m1|m2|m4|m5|all`, `--iterations N` (default 30; M5 uses max(5, N) cycles
per phase), `--seed S` (recorded into `environment.json`), `--out DIR` (default `data/<date>_<id>`).
Output: `<out>/<measurement>.json`, `<out>/environment.json`, `<out>/summary.md`.

Committed run: `data/20261116_sandbox/` (sandbox vCPU environment — see its `summary.md` for the
honest environment note).

## Measurements

| Module | Issue #28 item | What it measures |
|---|---|---|
| `M1RawThresholdData` | 1 — raw benchmark data behind perf thresholds | The eight PerformanceTests.cs scenarios with larger sample counts, recording raw distributions (min/mean/median/P90/P95/P99/max/stdev) instead of pass/fail verdicts, next to the enforced threshold values. |
| `M2ModeComparison` | 2 — mode comparison | Payload sweep (8 B / 10 KB / 100 KB) in-process vs containerized-seam dispatch, sequential + 8-worker-concurrent throughput, and HybridPluginFactory auto-resolution overhead (direct vs traditional-first vs containerized-first hit vs miss-fallback). |
| `M4SecurityEscapeRate` | 4 — security validator escape rate | An 8-item adversarial plugin corpus (committed C# source in `M4SecurityEscapeRate.cs`, compiled at run time with Roslyn) validated against Default/Strict/Permissive policies; per-level escape rates with issue codes, plus payload verification proving escaped items have the real capability. |
| `M5UnloadVerification` | 5 — unload verification under sustained hybrid operation | Repeated load/execute/unload cycles with traditional and containerized-seam execution running concurrently; per-cycle leak detection via WeakReference survival, resident plugin assemblies with their AssemblyLoadContext, managed memory and GC collection counts. |

## Scope and honest limitations

- **Containerized-seam ≠ container execution.** M2 and M5 use an in-memory
  `IContainerizedPluginOrchestrator` (Harness/InMemoryOrchestrator.cs). Numbers produced through it
  measure the CRPCF dispatch code path (proxy → orchestrator → result mapping) only, and are
  labeled `containerizedSeam` everywhere. The true containerized leg requires a container runtime
  and is **remaining work** (docker-capable hardware / the Z13 machines).
- **M3 (migration study)** is not implemented here; it is a workflow/effort study rather than a
  harness measurement and is tracked as remaining work on issue #28.
- **Environment**: the committed run was produced on a sandbox vCPU environment, not dedicated
  hardware; `environment.json` and the run's `summary.md` record this. Relative comparisons and
  structural findings transfer; absolute latencies may not.
- **M5 architectural finding** (shipped configuration): `FilePluginLoader` always attaches a
  `DefaultPluginSecurityValidator` (constructor fallback), whose path-based
  `ValidateAssemblyAsync` calls `Assembly.LoadFrom(path)`. This pins every validated plugin
  assembly in the DEFAULT, non-collectible AssemblyLoadContext, and the loader then reuses that
  pinned copy instead of its collectible `PluginLoadContext` copy. `UnloadAllPlugins` is
  consequently a no-op for the assemblies actually in use. M5 measures this honestly in the
  `shippedConfiguration` phase (leak indicators in every cycle) and separately verifies the
  collectible machinery itself is leak-free in the `collectiblePath` phase (non-loading stub
  validator; measurement-only, performs no security checks).

## Reproducibility

- All measurement inputs are committed: the harness source, the adversarial corpus source (M4),
  the seed, the iteration counts, and the environment capture.
- The M4 corpus DLLs are compiled at run time from the committed source; the harness writes the
  exact source it compiled next to the data (`m4-sandbox/*.cs`).
- `data/` is excluded from compilation (`DefaultItemExcludes` in the csproj) — measurement output
  must never be built, only read.
