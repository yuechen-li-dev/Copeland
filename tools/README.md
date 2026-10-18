# Repository tools

Production compiler/tooling source belongs under `src/Copeland`. This directory
contains maintained qualification, release, conformance, and measurement programs.
Every maintained tool needs an owner, an executable entry point, and an output
lifetime. Generated hosts and verbose output belong under ignored `artifacts/local`.

## Copeland entry points

| Purpose | Entry point | Output/qualification boundary |
| --- | --- | --- |
| TableScript browser qualification | [Browser runner](Copeland.Qualification.Browser/README.md) | Shared bootstrap/build/server/session; sample-owned scenario assertions, local reports |
| TableScript integration qualification | [TableScript runner](Copeland.Qualification.TableScript/README.md) | Canonical CLI/C# calculations, guarded edits and immutable previews; local output |
| C#/JavaScript JIT workload measurements | [JIT benchmarks](Copeland.Benchmarks.Jit/README.md) | Five shared authored workloads, checksum agreement and measured host behavior |
| Current JavaScript emission measurements | [Emission benchmark](Copeland.Benchmarks.Emission/README.md) | Fresh Production output with reachability off/on; Node output agreement |
| Compiler/fixture topology | `Validate-CopelandTsTopology.ps1` | Maintained source/project graph, ownership, and fixtures |
| Preview package-root closure | `Test-CopelandReleaseClosure.ps1` | Solution membership, not a publication qualification |
| Artifact policy | `Test-ArtifactBudget.ps1` | Changed tracked artifacts/evidence, prohibited scratch/cache/payload paths |
| Artifact policy regression cases | `../tests/Repository/Test-ArtifactBudget.ps1` | Small maintained fixtures; runs in artifact-policy CI |
| Preview packaging | `Build-CopelandPreviewRelease.ps1` | Build/test/pack/smoke through the declared release workflow |
| Instinct corpus and historical measurements | [Instinct proof](copeland-typescript-instinct-m26/README.md) | Shares maintained compiler corpus; historical evidence replay has separate prerequisites |
| Compiler burn-in | `Copeland.TS.BurnIn` | Maintained compiler consumer; see its project entry point |

Profile evidence projects remain explicit historical/domain qualification tools;
they are not production dependencies. Their consolidation requires checking
unique geometry/typography assertions and is separate from the TableScript pass.
Machina, Aurelian, games, font-reference, and ML research tools keep their owning
subsystem's scope; this index does not retire them by age or milestone name.

See [repository maintenance](../docs/Copeland/repository-maintenance.md) for the
normal build/check sequence and [artifact retention](../docs/Copeland/reference/artifact-retention.md)
for what belongs in Git versus local output.
