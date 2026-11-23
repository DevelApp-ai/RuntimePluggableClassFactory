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
dotnet build RuntimePluggableClassFactory.sln -c Release   # populates PluginFolder via post-build copies; also builds the PluginHost binary
dotnet run -c Release --project RuntimePluggableClassFactory.Benchmarks -- \
  --measurement all --iterations 30 --seed 20261116 --out data/my_run
```

Options: `--measurement m1|m2|m2c|m3|m4|m5|all`, `--iterations N` (default 30; M5 uses
max(5, N) cycles per phase; M2c uses N cold-start cycles), `--seed S` (recorded into
`environment.json`), `--out DIR` (default `data/<date>_<id>`).
Output: `<out>/<measurement>.json`, `<out>/environment.json`, `<out>/summary.md`.

Committed runs:

- `data/20261116_sandbox/` — first tranche (M1, M2 seam, M4, M5)
- `data/20261123_sandbox/` — second tranche (M2 container leg, M3) plus a fresh M1/M2/M4/M5 run
- `data/20261124_sandbox/` — post-#51-fix full run (all six modules), primarily the M5 fix-verification run

All on a sandbox vCPU environment — see each run's `summary.md` for the honest
environment note.

## Measurements

| Module | Issue #28 item | What it measures |
|---|---|---|
| `M1RawThresholdData` | 1 — raw benchmark data behind perf thresholds | The eight PerformanceTests.cs scenarios with larger sample counts, recording raw distributions (min/mean/median/P90/P95/P99/max/stdev) instead of pass/fail verdicts, next to the enforced threshold values. |
| `M2ModeComparison` | 2 — mode comparison (seam half) | Payload sweep (8 B / 10 KB / 100 KB) in-process vs containerized-seam dispatch, sequential + 8-worker-concurrent throughput, and HybridPluginFactory auto-resolution overhead (direct vs traditional-first vs containerized-first hit vs miss-fallback). The seam half runs an in-memory orchestrator and measures the CRPCF dispatch path only. |
| `M2ContainerLeg` | 2 — mode comparison (container leg) | The out-of-process half: the same plugin shipped to a REAL separate plugin host process (`RuntimePluggableClassFactory.PluginHost`, the binary a plugin container image would run as ENTRYPOINT) dispatched over the shipped CRPCF proxy path with per-call JSON-over-pipe IPC; cold start (package staging + spawn-to-ready) over N deploy/undeploy cycles, the IPC protocol floor, payload sweep against a same-run in-process baseline, sequential + concurrent throughput, and auto-resolution over a live/deployment-aware orchestrator. |
| `M3MigrationStudy` | 3 — migration study | Live promotion of `Test.SpecificClassImpl` v1.2.1 from in-process to containerized(host) execution inside one long-running `HybridPluginFactory`: per-step wall-clock effort (deploy, health, switchover, rollback), zero-code call-site preservation (same `GetPluginAsync` signature), 8-case contract-test parity across baseline/promoted/rolled-back phases, and the rollback path (undeploy + auto fallback, factory never restarted). |
| `M4SecurityEscapeRate` | 4 — security validator escape rate | An 8-item adversarial plugin corpus (committed C# source in `M4SecurityEscapeRate.cs`, compiled at run time with Roslyn) validated against Default/Strict/Permissive policies; per-level escape rates with issue codes, plus payload verification proving escaped items have the real capability. |
| `M5UnloadVerification` | 5 — unload verification under sustained hybrid operation | Repeated load/execute/unload cycles with traditional and containerized-seam execution running concurrently, in the shipped loader wiring (real `DefaultPluginSecurityValidator`, metadata-only since the #51 fix); per-cycle leak detection via WeakReference survival, resident plugin assemblies with their AssemblyLoadContext, managed memory and GC collection counts. |

## Scope and honest limitations

- **Containerized seam vs containerized host vs container.** Three distinct
  things, kept apart everywhere in the data:
  - `containerizedSeam` (M2, M5): numbers through an in-memory orchestrator
    (Harness/InMemoryOrchestrator.cs) measure the CRPCF dispatch code path
    (proxy → orchestrator → result mapping) only — no process boundary.
  - `containerHost` (M2c, M3): numbers through
    Harness/ProcessIsolatedOrchestrator.cs and the
    `RuntimePluggableClassFactory.PluginHost` binary — a REAL separate OS
    process with per-call JSON-over-pipe IPC and serialization, real
    spawn/teardown on deploy/undeploy. This is the process-hosting half of
    containerized execution.
  - **Not measured in this environment** (no container runtime in the sandbox):
    Docker image build/pull, cgroup/sandbox isolation overhead, network
    transport for non-local pods, Kubernetes scheduling, and registry round
    trips. `containerHost` numbers must not be presented as container-runtime
    numbers; a real single-replica pod adds all of the above on top. The
    Docker/K8s leg is the **remaining container work** (docker-capable
    hardware / the Z13 machines).
- **M3 scope**: the migration study covers the framework-level steps
  (package+deploy, switchover, parity, rollback) against the process host.
  A real promotion additionally involves registry push, image build and
  scheduling, which are not measured here.
- **M3 version pinning**: the promoted plugin is v1.2.1 pinned by assembly, one
  version allowed/loaded in the traditional factory (its version store retains
  only `retainOldVersions` = 1 per plugin name by default — see M3 comments).
- **Environment**: the committed runs were produced on a sandbox vCPU
  environment, not dedicated hardware; `environment.json` and each run's
  `summary.md` record this. Relative comparisons and structural findings
  transfer; absolute latencies may not.
- **M5 unload finding — fixed by the #51 remediation, verified by the
  `data/20261124_sandbox/` run**: through the 2026-11-23 run,
  `DefaultPluginSecurityValidator.ValidateAssemblyAsync` called
  `Assembly.LoadFrom(path)`, pinning every validated plugin assembly in the
  DEFAULT, non-collectible AssemblyLoadContext; `FilePluginLoader` then reused
  that pinned copy instead of its collectible `PluginLoadContext` copy, making
  `UnloadAllPlugins` a no-op for the assemblies actually in use (leak
  indicators in every shipped-configuration cycle of the 2026-11-16 and
  2026-11-23 runs). Since #51 the path-based validation is metadata-only
  (`PEReader` + `MetadataReader`; no ALC load), the measurement-only
  non-loading stub validator was removed, and the shipped configuration
  itself exercises the collectible path: 30/30 leak-free cycles, weak
  references dying after one GC, 0 resident plugin assemblies.

## Reproducibility

- All measurement inputs are committed: the harness source, the adversarial corpus source (M4),
  the seed, the iteration counts, and the environment capture.
- M2c and M3 additionally need the `RuntimePluggableClassFactory.PluginHost` binary; building the
  solution in Release produces it and the harness locates it by walking up to the repository root
  (no path configuration required).
- The M4 corpus DLLs are compiled at run time from the committed source; the harness writes the
  exact source it compiled next to the data (`m4-sandbox/*.cs`).
- `data/` is excluded from compilation (`DefaultItemExcludes` in the csproj) — measurement output
  must never be built, only read.
