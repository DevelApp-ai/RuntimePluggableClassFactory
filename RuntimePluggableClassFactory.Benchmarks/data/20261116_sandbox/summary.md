# Measurement run — 2026-11-16 (sandbox vCPU)

Raw data for issue #28 measurements produced by
`RuntimePluggableClassFactory.Benchmarks` on 2026-11-16.

- Seed: `20261116`
- Iterations per scenario: `30` (M5: 30 load/unload cycles per phase)
- Configuration: Release, .NET 8.0 (see `environment.json`)
- Invocations (same seed and iteration count for every measurement):
  - `dotnet run -c Release -- --measurement m1 --iterations 30 --seed 20261116 --out .`
  - `dotnet run -c Release -- --measurement m2 --iterations 30 --seed 20261116 --out .`
  - `dotnet run -c Release -- --measurement m4 --iterations 30 --seed 20261116 --out .`
  - `dotnet run -c Release -- --measurement m5 --iterations 30 --seed 20261116 --out .`

## Files

- `environment.json` — hardware/software environment capture (sandbox vCPU; honest environment note below)
- `m1.json` — measurement 1: raw distributions behind the test-enforced performance thresholds
- `m2.json` — measurement 2: in-process vs containerized-seam dispatch + HybridPluginFactory auto-resolution overhead
- `m4.json` — measurement 4: security validator escape rate (Default/Strict/Permissive) over an 8-item adversarial corpus, with payload verification
- `m4-sandbox/*.cs` — the adversarial corpus sources exactly as compiled during measurement 4 (the .dll binaries and marker files are reproducible and are not committed)
- `m5.json` — measurement 5: unload verification under sustained hybrid operation (two phases: shipped configuration, collectible path)

## Honest environment note

These numbers were produced on a shared **sandbox vCPU environment (2 vCPUs, containerized Linux, not Lars's Z13 hardware)**. They are valid as relative comparisons (in-process vs seam, policy vs policy, cycle vs cycle) and as evidence that the scenarios run and what they reveal structurally. Absolute latencies on dedicated hardware will differ; re-running the harness with the same seed and iteration counts reproduces the scenario shapes.

## Headline results (details in the JSON files)

- **M1**: all threshold scenarios measured comfortably inside the enforced thresholds on this environment (e.g. discovery mean 11.6 ms vs 5,000 ms threshold; instantiation mean 0.0005 ms vs 100 ms).
- **M2**: containerized-seam dispatch adds a ~0.033 ms constant per call across 8 B–100 KB payloads; HybridPluginFactory auto-resolution adds ~0.001 ms over a direct traditional lookup. These are seam numbers (in-memory orchestrator), NOT container-execution numbers.
- **M4**: 5 of 7 malicious corpus items escaped validation at Default (71.4%), the same 5 at Strict (71.4%), and 7 of 7 at Permissive (100%). Both escapes with executable payloads were verified to produce their forbidden effect (process spawn, file write).
- **M5**: the collectible ALC path is leak-free (30/30 cycles clean, weak references die after one GC, 0 resident plugin assemblies). The shipped configuration leaks by design: every cycle pins all 4 plugin assemblies in the DEFAULT AssemblyLoadContext via DefaultPluginSecurityValidator's `Assembly.LoadFrom` — see `m5.json` `architecturalFinding`.
