# Measurement run — 2026-11-23 (sandbox vCPU)

Raw data for issue #28 measurements produced by
`RuntimePluggableClassFactory.Benchmarks` on 2026-11-23. This run adds the
**M2 container leg** (measurement 2, out-of-process half) and the **M3
migration study**, and re-runs M1/M2/M4/M5 from the 2026-11-16 tranche on the
same seed discipline so both tranches are comparable run-for-run.

- Seed: `20261123`
- Iterations per scenario: `30` (M5: 30 load/unload cycles per phase; M2c: 30 cold-start deploy/undeploy cycles)
- Configuration: Release, .NET 8.0 (see `environment.json`)
- Invocations (same seed and iteration count for every measurement):
  - `dotnet run -c Release -- --measurement m1 --iterations 30 --seed 20261123 --out .`
  - `dotnet run -c Release -- --measurement m2 --iterations 30 --seed 20261123 --out .`
  - `dotnet run -c Release -- --measurement m2c --iterations 30 --seed 20261123 --out .`
  - `dotnet run -c Release -- --measurement m3 --iterations 30 --seed 20261123 --out .`
  - `dotnet run -c Release -- --measurement m4 --iterations 30 --seed 20261123 --out .`
  - `dotnet run -c Release -- --measurement m5 --iterations 30 --seed 20261123 --out .`

## Files

- `environment.json` — hardware/software environment capture (sandbox vCPU; honest environment note below)
- `m1.json` — measurement 1: raw distributions behind the test-enforced performance thresholds
- `m2.json` — measurement 2, seam half: in-process vs containerized-seam dispatch (in-memory orchestrator) + HybridPluginFactory auto-resolution overhead
- `m2c.json` — measurement 2, container leg: plugin execution through a REAL out-of-process plugin host (the `RuntimePluggableClassFactory.PluginHost` binary a container image would run) dispatched over the shipped CRPCF proxy path, with per-call JSON-over-pipe IPC and serialization; includes cold start, protocol floor, throughput, and auto-resolution over a live/deployment-aware orchestrator
- `m3.json` — measurement 3: migration study — live promotion of `Test.SpecificClassImpl` v1.2.1 from in-process to containerized(host) execution in one running hybrid system, contract-test parity (baseline/promoted/rolled-back), and rollback
- `m4.json` — measurement 4: security validator escape rate (Default/Strict/Permissive) over an 8-item adversarial corpus, with payload verification
- `m4-sandbox/*.cs` — the adversarial corpus sources exactly as compiled during measurement 4 (the .dll binaries and marker files are reproducible and are not committed)
- `m5.json` — measurement 5: unload verification under sustained hybrid operation (two phases: shipped configuration, collectible path)

## Honest environment note

These numbers were produced on a shared **sandbox vCPU environment (2 vCPUs, containerized Linux, not Lars's Z13 hardware)**. They are valid as relative comparisons (in-process vs host process, policy vs policy, cycle vs cycle) and as evidence that the scenarios run and what they reveal structurally. Absolute latencies on dedicated hardware will differ; re-running the harness with the same seed and iteration counts reproduces the scenario shapes.

## Honest scope note for the M2 container leg and M3 (repeated in `m2c.json`/`m3.json`)

The "containerized" target of this run is a **real out-of-process plugin host**
(separate OS process, separate memory, separate lifetime, per-call JSON
serialization over pipes, real spawn/teardown on deploy/undeploy) — the process
hosting half of containerized execution. It is **not** a Docker/Kubernetes
deployment: image build/pull, cgroup/sandbox overhead, network transport for
non-local pods, and scheduler time are NOT measured and MUST NOT be presented
as container-runtime numbers (a real single-replica pod adds all of those on
top of what is measured here). The comparison payload on both sides is
`Test.SpecificClassImpl` v1.2.1 (same assembly, same machine, same run).

## Headline results (details in the JSON files)

- **M1**: all threshold scenarios measured comfortably inside the enforced thresholds on this environment (e.g. discovery mean 11.5 ms vs 5,000 ms threshold; execution mean ~0.0001 ms vs 10 ms).
- **M2 (seam)**: containerized-seam dispatch adds a constant per call across 8 B–100 KB payloads; HybridPluginFactory auto-resolution adds sub-0.01 ms over a direct traditional lookup (in-memory orchestrator numbers, as in the 2026-11-16 run).
- **M2c (container leg)**: out-of-process host execution adds **+0.11 ms mean at 8 B, +0.19 ms at 10 KB, +0.65 ms at 100 KB** per call over the in-process baseline; the IPC protocol floor (ping/pong) is ~0.04 ms. Cold start (package staging + host spawn to ready) is **~94 ms mean**, teardown ~50 ms. Sequential throughput through the single-replica host is **~8.5k exec/s vs ~50M exec/s in-process**; under 8 concurrent workers the host path serializes at the pipe (~10.7k exec/s) while in-process scales to ~3.1M exec/s — the single-replica queueing behavior, not parallel plugin execution.
- **M3**: promotion of `Test.SpecificClassImpl` v1.2.1 in a running hybrid system took **~103 ms (deploy incl. host spawn) + 0.31 ms (switchover lookup)** with **0 call-site lines changed** (deploy call + `HybridPluginFactoryOptions.PreferContainerized`, same `GetPluginAsync` signature); contract parity held for all 8 cases in all 3 phases (baseline / promoted / rolled-back: `behaviorPreserved = true`); rollback (undeploy + auto fallback to in-process) took **~52.6 ms + 0.02 ms** without restarting the factory. The container host binary is 114 LOC.
- **M4**: 5 of 7 malicious corpus items escaped validation at Default (71.4%), the same 5 at Strict (71.4%), and 7 of 7 at Permissive (100%). Both escapes with executable payloads were verified to produce their forbidden effect (process spawn, file write). Consistent with the 2026-11-16 run.
- **M5**: the collectible ALC path is leak-free (30/30 cycles clean, weak references die after one GC, 0 resident plugin assemblies). The shipped configuration leaks by design: every cycle pins all 4 plugin assemblies in the DEFAULT AssemblyLoadContext via DefaultPluginSecurityValidator's `Assembly.LoadFrom` — see `m5.json` `architecturalFinding`. Consistent with the 2026-11-16 run.
