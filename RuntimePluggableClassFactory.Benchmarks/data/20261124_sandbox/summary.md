# Measurement run — 2026-11-24 (sandbox vCPU, post-#51-fix)

Raw data produced by `RuntimePluggableClassFactory.Benchmarks` on 2026-11-24.
This run is the **first run on the fixed build** (issue #51: metadata-only
security validation) and re-runs all six modules on the same seed discipline as
the 2026-11-16 and 2026-11-23 tranches, so all three runs are comparable
run-for-run. Its primary purpose is the #51 acceptance verification: the M5
shipped configuration must be leak-free now that the real validator no longer
loads plugin files during validation. It also documents that the other five
measurement scenarios are unaffected by the fix (M4 escape rates and M2c/M3
numbers reproduce within run-to-run noise).

- Seed: `20261124`
- Iterations per scenario: `30` (M5: 30 load/unload cycles; M2c: 30 cold-start
  deploy/undeploy cycles)
- Configuration: Release, .NET 8.0 (see `environment.json`)
- Invocations (same seed and iteration count for every measurement):
  - `dotnet run -c Release -- --measurement m1 --iterations 30 --seed 20261124 --out .`
  - `dotnet run -c Release -- --measurement m2 --iterations 30 --seed 20261124 --out .`
  - `dotnet run -c Release -- --measurement m2c --iterations 30 --seed 20261124 --out .`
  - `dotnet run -c Release -- --measurement m3 --iterations 30 --seed 20261124 --out .`
  - `dotnet run -c Release -- --measurement m4 --iterations 30 --seed 20261124 --out .`
  - `dotnet run -c Release -- --measurement m5 --iterations 30 --seed 20261124 --out .`

## Files

- `environment.json` — hardware/software environment capture (sandbox vCPU; honest environment note below)
- `m1.json` — measurement 1: raw distributions behind the test-enforced performance thresholds
- `m2.json` — measurement 2, seam half: in-process vs containerized-seam dispatch (in-memory orchestrator) + HybridPluginFactory auto-resolution overhead
- `m2c.json` — measurement 2, container leg: plugin execution through the real out-of-process plugin host (`RuntimePluggableClassFactory.PluginHost`), with per-call JSON-over-pipe IPC and serialization; cold start, protocol floor, throughput, and auto-resolution over a live orchestrator
- `m3.json` — measurement 3: migration study — live promotion of `Test.SpecificClassImpl` v1.2.1 from in-process to containerized(host) execution in one running hybrid system, contract-test parity (baseline/promoted/rolled-back), and rollback
- `m4.json` — measurement 4: security validator escape rate (Default/Strict/Permissive) over the 8-item adversarial corpus, with payload verification
- `m4-sandbox/*.cs` — the adversarial corpus sources exactly as compiled during measurement 4 (the .dll binaries and marker files are reproducible and are not committed)
- `m5.json` — measurement 5: unload verification under sustained hybrid operation (single phase: shipped configuration with the real, now metadata-only validator — the measurement-only NonLoadingSecurityValidator stub was removed with the #51 fix because it is no longer needed)

## Honest environment note

These numbers were produced on a shared **sandbox vCPU environment (2 vCPUs,
containerized Linux, not Lars's Z13 hardware)**. They are valid as relative
comparisons and as evidence that the scenarios run and what they reveal
structurally. Absolute latencies on dedicated hardware will differ; re-running
the harness with the same seed and iteration counts reproduces the scenario
shapes.

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

- **M1**: all threshold scenarios comfortably inside the enforced thresholds
  (discovery mean 19.5 ms vs 5,000 ms threshold; execution mean ~0.0001 ms vs
  10 ms; security-validation mean 0.51 ms vs 10 ms). Note for #51: the
  metadata-only validator reads the plugin file on every path validation
  instead of reusing a pinned assembly, so the validation micro-benchmark
  moved from 0.29 ms (2026-11-23 run) to 0.51 ms — still two orders of
  magnitude below the threshold, and the price of not pinning plugins.
- **M2 (seam)**: containerized-seam dispatch adds a constant per call across
  8 B–100 KB payloads; HybridPluginFactory auto-resolution adds sub-0.01 ms
  over a direct traditional lookup (in-memory orchestrator numbers).
- **M2c (container leg)**: out-of-process host execution adds **+0.11 ms mean
  at 8 B, +0.20 ms at 10 KB, +0.61 ms at 100 KB** per call over the in-process
  baseline; the IPC protocol floor (ping/pong) is ~0.04 ms. Cold start
  (package staging + host spawn to ready) is **~95 ms mean**, teardown
  ~50 ms. Sequential throughput through the single-replica host is **~8.4k
  exec/s vs ~53M exec/s in-process**; under 8 concurrent workers the host path
  serializes at the pipe (~9.2k exec/s) while in-process scales to
  ~3.2M exec/s — the single-replica queueing behavior, not parallel plugin
  execution. Consistent with the 2026-11-23 run.
- **M3**: promotion of `Test.SpecificClassImpl` v1.2.1 in a running hybrid
  system with **0 call-site lines changed**; contract parity held for all 8
  cases in all 3 phases (`behaviorPreserved = true`); rollback without
  restarting the factory. Consistent with the 2026-11-23 run.
- **M4**: 5 of 7 malicious corpus items escaped validation at Default
  (71.4%), the same 5 at Strict (71.4%), and 7 of 7 at Permissive (100%);
  both escapes with executable payloads verified to produce their forbidden
  effect. **Identical escape sets and rates to the 2026-11-16/2026-11-23
  runs** — the metadata-only validator makes the same accept/reject decisions
  as the pre-fix LoadFrom-based validator on this corpus (semantic parity
  evidence for #51).
- **M5 (#51 acceptance verification)**: the **shipped configuration is now
  leak-free — 30/30 cycles clean**: every cycle's plugin instance and plugin
  Type weak references die after **one** forced GC, **0 resident plugin
  assemblies** above baseline after unload, no leak indicators in any cycle.
  The real `DefaultPluginSecurityValidator` is in the loop (loader default
  wiring) — the collectible `PluginLoadContext` machinery is now exercised by
  the shipped path itself. The pre-fix runs (`data/20261116_sandbox/`,
  `data/20261123_sandbox/`) document the shipped-configuration leak that
  `Assembly.LoadFrom`-based validation caused for 30/30 cycles.
