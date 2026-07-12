# Joint Task Force test-lane doctrine

## Purpose

The default test lane answers one question: did an ordinary code change break a meaningful subsystem contract? It must be fast, deterministic, self-contained, independently runnable, and quiet with respect to repository artifacts.

Historical evidence generation, visual investigation, optional toolchain probing, hardware proofs, sample galleries, and full subsystem integration remain useful, but they are explicitly invoked work rather than unit-test overhead.

## Lane definitions

### Fast

A fast test protects a public contract or a stable internal seam with a minimal representative input. Fast tests may use temporary files when the filesystem is the contract, but must not write beneath the repository. They must not depend on browsers, optional executables, network access, display servers, human inspection, test ordering, or mutable machine-wide state.

The fast solutions are:

- `Copeland.slnx`
- `Machina.UI.slnx`
- `Aurelian.slnx`
- `JointTaskForce.slnx`, which is the union of the fast subsystem contracts plus repository production/sample builds

### Integration

Integration tests exercise more than one production component, a complete sample path, or a lifecycle proof whose setup is materially broader than a unit contract. `JointTaskForce.Integration.slnx` owns Aurelian compositor/runtime and visible-triangle sample proofs. Integration projects must not create hidden production dependency edges.

### Slow and diagnostic

Slow and diagnostic tests produce visual evidence, execute multi-size raster sweeps, compare reference renderers, export galleries, run playback suites, or investigate fonts. `Machina.UI.Slow.slnx` owns these projects:

- `Machina.Fonts.Diagnostics.Tests`
- `Machina.Fonts.Tooling.Tests`
- `Machina.ComponentGallery.Sample.Tests`
- `Machina.Presenter.Sample.Tests`

Diagnostic scripts under `tools/` are retained for intentional artifact generation. They target diagnostics projects, never the fast font project.

## Membership rules

- A test project in a fast solution contains only fast unit or subsystem-contract tests.
- A mixed project must be split when an expensive test family cannot be excluded by physical project ownership. Traits alone are not a solution-membership boundary.
- Sample end-to-end, artifact, integration, external-toolchain, and hardware-dependent projects do not belong in fast solutions.
- A new expensive project must be added only to the narrowest explicit expensive solution.
- `JointTaskForce.slnx` must not acquire a slow project transitively through a test-project reference.

## Artifact policy

Fast tests do not write to `artifacts/` or any other repository path. Use a unique directory beneath `Path.GetTempPath()` and delete it when practical. Direct structural assertions replace screenshots, galleries, reports, and golden files unless the serialized artifact format itself is the current contract.

Artifact exporters are invoked from `Machina.UI.Slow.slnx` or the named `tools/Export-*.ps1` workflow. Generated evidence is not a reason to place the exporter in a fast lane.

## Subprocess policy

Every subprocess test must:

1. execute an already-built binary rather than invoke a nested build;
2. redirect and drain stdout and stderr concurrently;
3. close or explicitly manage stdin;
4. use a documented timeout appropriate to the operation;
5. kill the complete child process tree on timeout or failure;
6. wait for termination and include captured output in the failure;
7. avoid sleeps and shell-string argument construction where `ArgumentList` is available.

A larger timeout is not a repair for a flaky lifecycle.

## Toolchain, platform, and hardware policy

Pure command construction, validation, and availability-state contracts may remain fast. A test that requires an optional executable, browser, window system, GPU feature, physical device, or machine-specific font belongs in an explicit integration/diagnostic lane. Availability probes must return a deterministic typed outcome and must not silently turn absence into a passing proof of behavior that did not run.

Aurelian's current Vulkan project uses the installed loader when available while preserving deterministic unavailable-path assertions. It remains inside the 30-second subsystem budget, but tests that require a particular adapter or presentation surface must be added to `JointTaskForce.Integration.slnx`, not that mixed project.

## Deletion and consolidation

Keep a test because it protects a current contract. Delete or consolidate it when it duplicates stronger coverage, asserts only milestone metadata, preserves a superseded implementation, merely emits evidence for inspection, or repeats the same renderer/compiler path with cosmetic inputs.

Before deleting the only test for live behavior, add a smaller contract test. Migration records must identify the removed family, why it was ineffective or redundant, the remaining protection, and any intentionally uncovered behavior.

## Reviewer expectations

New tests should:

- assert observable behavior or a stable internal boundary;
- use the smallest input that proves the contract;
- share expensive immutable setup when safe;
- use deterministic IDs, paths, clocks, and ordering;
- preserve parallel execution by isolating the actual shared resource;
- fail with contract-relevant messages;
- avoid broad exception swallowing and global mutable state;
- declare cancellation for asynchronous work.

Reviewers should reject a test that generates inspection artifacts in a fast project or shells out without bounded lifecycle management.

## Runtime budgets and workflow

On the JTF-M1b Windows development machine, warm `--no-build` budgets are:

- individual fast projects: generally a few seconds; renderer/font projects may take up to 15 seconds;
- `Copeland.slnx`: 30 seconds;
- `Machina.UI.slnx`: 60 seconds;
- `Aurelian.slnx`: 60 seconds;
- `JointTaskForce.slnx`: 120 seconds.

Build once, then iterate without rebuilding unchanged outputs:

```powershell
dotnet build <solution>.slnx
dotnet test <solution>.slnx --no-build
```

Report cold build, warm build, test execution, and expensive-lane timings separately. Before/after comparisons must use equivalent commands.
