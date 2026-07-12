# JTF-M1b test runtime rationalization

## Outcome

JTF-M1b separated ordinary contracts from sample proofs and font diagnostics, repaired the Copeland CLI process harness, and established explicit fast, integration, and slow solution ownership. No production source file, assembly, namespace, or dependency direction changed.

Measurements were taken on Windows 10.0.26200 with .NET SDK 10.0.301. Solution comparisons use `dotnet build <solution>` followed by `dotnet test <solution> --no-build`. Wall time includes test-host startup; runner project time is included where it exposed the dominant cost.

## Before-state measurements

| Solution | Build wall time | Test wall time | Result and dominant cost |
| --- | ---: | ---: | --- |
| `Copeland.slnx` | 4.89s | 19.95s | green; `Copeland.Cli.Tests` reported 18.68s |
| `Machina.UI.slnx` | about 10s | about 100s | green; font tests reported 98.7s, presenter 52.2s, gallery 43.2s |
| `Aurelian.slnx` | about 4s | about 16s | green; graphics tests reported 14.63s |
| `JointTaskForce.slnx` | about 4s | about 100s | green; repeated the 98s font, 65s tooling, 52s presenter, and 43s gallery projects in parallel |
| `Machina.UI.Slow.slnx` | about 3s | did not complete during the initial several-minute audit run | only the tooling diagnostics project; no bounded hang collector on the command |

Project runner times are not additive because solution projects run concurrently. The audit inventory was:

| Test project | Before runner time | Classification before JTF-M1b |
| --- | ---: | --- |
| `Copeland.Cli.Tests` | 18.68s | CLI/subprocess plus artifact export; mixed |
| `Copeland.Markdown.Tests` | 2.21s | fast contract plus historical doctrine proofs |
| `Copeland.Script.Tests` | 3.41s | fast compiler/runtime contract |
| `Machina.ComponentGallery.Sample.Tests` | 43.24s | sample integration, artifact/golden, visual proof |
| `Machina.Core.Tests` | 2.97s | fast unit and subsystem contract |
| `Machina.Dominatus.Tests` | 3.00s | subsystem contract |
| `Machina.Fonts.Tests` | 98.7s | fast unit mixed with artifact, font, visual, browser-reference, and milestone proof |
| `Machina.Fonts.Tooling.Tests` | 64.6s | font/visual diagnostic and artifact export |
| `Machina.Fonts.Tooling.Unit.Tests` | 5.79s | fast tooling boundary contract |
| `Machina.Layout.Tests` | 2.04s | fast unit and subsystem contract |
| `Machina.Pipeline.Tests` | 3.63s | subsystem contract |
| `Machina.Presenter.Sample.Tests` | 52.25s | sample integration, playback, artifacts, historical milestone proof |
| `Machina.Renderer.Raster.Dominatus.Tests` | 2.83s | subsystem contract and small golden-format checks |
| `Machina.Renderer.Raster.Tests` | 3.16s | fast raster contract |
| `Machina.Renderer.Raster.Text.Tests` | 3.30s | fast text-renderer contract |
| `Machina.Runtime.Tests` | 2.31s | fast unit contract |
| `Machina.Standard.Tests` | 3.32s | fast UI contract |
| `Machina.Testing` | n/a | shared test helper library, not a test assembly |
| `Aurelian.Actuation.Tests` | 1.83s | fast contract |
| `Aurelian.Assets.Tests` | 2.07s | fast artifact-loader contract |
| `Aurelian.AssetTool.Tests` | 1.74s | fast tool contract |
| `Aurelian.Core.Tests` | 2.64s | fast unit contract |
| `Aurelian.Graphics.Tests` | 14.63s | fast mapping/lifecycle contracts with platform-availability branches |
| `Aurelian.Integration.Tests` | 3.22s | integration |
| `Aurelian.Rendering.Contracts.Tests` | 1.82s | fast contract |
| `Aurelian.Rendering.Null.Tests` | 1.96s | fast contract |
| `Aurelian.Runtime.Tests` | 2.21s | fast contract |
| `Aurelian.Shaders.Tests` | 2.41s | fast parser/emitter contracts plus optional DXC availability paths |
| `Aurelian.VisibleTriangle.Tests` | 2.01s | sample integration and historical closeout proof |
| `Aurelian.World.Tests` | 1.70s | fast unit contract |

## Root causes

The measured costs were structural rather than one slow API:

- `Machina.Fonts.Tests` ran three full 32/48/64px shape-diff exports at 17–18s each plus a 22–24s script proof, along with reference reports and image output.
- Gallery and presenter sample projects repeatedly rendered/exported equivalent historical proof surfaces. Individual gallery exports took 14–19s; presenter navigation, playback, and outline proof cases took up to 14s each.
- `JointTaskForce.slnx` included the explicitly slow font-tooling project and Aurelian integration/sample proof projects.
- The CLI project launched `dotnet run` eleven times. That rebuilt or re-evaluated the project for every test and made a nested build process part of the test lifecycle.
- The CLI harness drained stdout synchronously before stderr, waited without a timeout, never closed stdin, and did not terminate a failed child tree.
- A presenter proof used the same unbounded sequential pipe pattern for a PowerShell child.
- Playback failure tests wrote into tracked `artifacts/m16d`, changing a golden file to contain a random temporary path.
- Several projects mixed direct contract assertions with artifact generation, environment-variable serialization, external reference workflows, and disabled-parallel collections.

## Copeland CLI hang diagnosis

The preserved sequence files at `tests/Copeland/Copeland.Cli.Tests/TestResults` recorded `EmitMirToStdout` as incomplete in two runs. The stored testhost dump showed the test thread in:

```text
System.IO.StreamReader.ReadToEnd()
Copeland.Cli.Tests.CliIntegrationTests.RunCli(...)
Copeland.Cli.Tests.CliIntegrationTests.EmitMirToStdout()
```

Before repair, the individual test passed five consecutive runs in 2.58–2.72s; the containing class passed twice in 16.18s and 16.60s; the full solution passed once in 19.95s. This isolated-versus-suite difference, the dump, and the harness ordering identify a test lifecycle defect rather than a production CLI deadlock. Nested `dotnet run` increased contention and the sequential EOF read allowed the testhost to wait forever whenever the child did not close stdout.

The repaired helper executes the already-built `Copeland.Cli.dll`, closes stdin, starts stdout and stderr reads concurrently, enforces a 10-second cancellation budget, kills the complete child tree on timeout, waits for termination, and reports both streams. The CLI project now reports about 0.69s for ten retained process contracts. No production change was needed.

The artifact-only `MarkdownExportCorpusWritesArtifacts` CLI test was deleted. It regenerated milestone evidence and asserted only four filenames. Current Markdown parsing/lowering and docs-dogfood tests protect the live compiler contract; deliberate corpus export remains a CLI workflow, but its historical file bundle is intentionally no longer part of ordinary regression coverage.

## Reorganization and consolidation

- `Machina.Fonts.Tests` excludes full font-proof, reference-oracle, reference-diff, and three-way shape-diff workflow test files. It retains generation, metrics, packing, layout, renderer, serialization, and small representative MSDF/raster contracts.
- `ShapeDiffContractTests` replaces the multi-size workflow in the fast lane with five direct assertions for curve flattening, fill-rule holes, baseline-guide exclusion, identical masks, and shifted-mask metrics.
- `Machina.Fonts.Diagnostics.Tests` physically owns the complete retained diagnostic source set. Existing font export scripts now target this project.
- `Machina.ComponentGallery.Sample.Tests` and `Machina.Presenter.Sample.Tests` moved from fast solutions to `Machina.UI.Slow.slnx` because they are sample integration/playback/proof suites and intentionally generate artifacts.
- `Aurelian.Integration.Tests` and `Aurelian.VisibleTriangle.Tests` moved from `Aurelian.slnx` and `JointTaskForce.slnx` to `JointTaskForce.Integration.slnx`.
- `Machina.Fonts.Tooling.Tests` left `JointTaskForce.slnx`; it remains in `Machina.UI.Slow.slnx`.
- Playback failure output now uses a process-local temporary directory instead of rewriting tracked evidence.
- The presenter PowerShell proof now drains both redirected streams concurrently and kills the process tree after 30 seconds.

No font diagnostic was discarded: the full original font diagnostic test set, tooling tests, scripts, and sample proof suites remain explicitly runnable. Only the obsolete CLI corpus filename proof was removed.

## After-state measurements

Equivalent after runs produced:

| Solution | Build wall time | Test wall time | Result |
| --- | ---: | ---: | --- |
| `Copeland.slnx` | 1.19s warm | 3.02s | 228 passed; repeat 2.91s |
| `Machina.UI.slnx` | 1.72s warm | 9.16s | 771 passed; repeat 8.86s |
| `Aurelian.slnx` | 1.53s warm | 14.23s | 569 passed; repeat 12.84s |
| `JointTaskForce.slnx` | 2.31s warm | 15.66s | complete fast suite passed; repeat 15.48s |
| `JointTaskForce.Integration.slnx` | 1.20s warm | 1.19s | 15 passed |
| `Machina.UI.Slow.slnx` | 2.37s warm | 111.18s | 1,119 passed under the 180s per-test hang collector |

## Commands

Fast subsystem loops:

```powershell
dotnet build Copeland.slnx
dotnet test Copeland.slnx --no-build

dotnet build Machina.UI.slnx
dotnet test Machina.UI.slnx --no-build

dotnet build Aurelian.slnx
dotnet test Aurelian.slnx --no-build

dotnet build JointTaskForce.slnx
dotnet test JointTaskForce.slnx --no-build
```

Retained expensive lanes:

```powershell
dotnet build Machina.UI.Slow.slnx
dotnet test Machina.UI.Slow.slnx --no-build --blame-hang-timeout 180s

dotnet build JointTaskForce.Integration.slnx
dotnet test JointTaskForce.Integration.slnx --no-build
```

Intentional diagnostic exports remain available through:

```powershell
pwsh ./tools/Export-MachinaFontProofs.ps1
pwsh ./tools/Export-MachinaFontReferenceComparison.ps1
pwsh ./tools/Export-MachinaFontReferenceDiff.ps1
pwsh ./tools/Export-MachinaFontShapeDiff.ps1
pwsh ./tools/Export-MachinaFontDiagnostics.ps1
pwsh ./tools/Export-MachinaMsdfAlignmentRepairM9f.ps1
```

## Remaining known costs

- `Aurelian.Graphics.Tests` is the dominant Aurelian fast project at roughly 13 seconds. Its tests cover current Vulkan mapping, validation, ownership, unavailable-state, and installed-loader lifecycle contracts. Moving the entire project would remove meaningful backend protection; future tests that require a specific adapter, surface, or visible output must instead be created in the integration lane.
- The slow font diagnostics project deliberately recompiles the diagnostic test assembly and performs complete raster/reference exports. At about two minutes it is suitable for intentional proof work, not ordinary edits.
- Some retained historical manifest assertions remain inside the relocated sample proof projects. They no longer affect fast iteration and can be deleted independently when those proof workflows are retired.

## Final validation

- All four required fast build/test pairs passed, with the final timings in the after-state table.
- A second `--no-build` pass of every fast solution passed.
- Repaired `EmitMirToStdout` passed 10/10 isolated runs at 1.03–1.09s wall time per test-host invocation.
- The complete repaired CLI class passed 5/5 runs at 1.47–1.56s per invocation.
- `Machina.UI.Slow.slnx` built and passed all 1,119 tests in 111.18s.
- `JointTaskForce.Integration.slnx` built and passed all 15 tests in 1.19s.
- `Validate-DependencyBoundaries.ps1` passed for 24 production projects with the same three recorded JTF-M1 exceptions.
- Every project path in every `.slnx` exists; every `ProjectReference` path exists.
- An explicit ownership check found no diagnostics, sample-proof, or integration test project in a fast solution.
- `git diff --check` passed.

No production file changed. The validation baseline is fast and repeatable enough for JTF-M2.
