# Current Copeland compiler topology

This page describes the maintained source/build graph. Older milestone topology
records preserve the graph at the time of that work; they are not setup guides.

## Source ownership

| Project or directory | Responsibility |
| --- | --- |
| `Copeland.TS` | Syntax, binding/type checking, static evaluation, lowering, module compilation, and integrated domain authoring |
| `Copeland.TS.Mir` | Cope MIR, validation, and deterministic text; BCL-only, with no project/package references |
| `Copeland.TS.Backend.CSharp` | MIR-to-C# emission, CLR sidecar contracts/hosting, and table query source generation |
| `Copeland.TS.Backend.JavaScript` | MIR-to-JavaScript emission profiles and compiler-produced browser/component artifacts |
| `Copeland.TS.Backend.Wgsl` | Managed WGSL projection of the GPU authoring lane |
| `Copeland.Markdown` | Markdown and document compiler model |
| `Copeland.Profile`, `Copeland.SpanAllocation` | Reusable profile/geometry and span allocation services |
| `Copeland.TS.Workspace` | Workspace ownership and orchestration contracts |
| `Copeland.Cli`, `Copeland.TS.LanguageServer` | CLI and editor-facing consumers of compiler/workspace facts |
| `Copeland.TS.MSBuild`, `.Templates`, `.Npm`, `.VSCode` | SDK integration, project creation, launcher, and editor packaging |

These projects live under `src/Copeland`; corresponding regression projects live
under `tests/Copeland`. Applications and browser integration specimens live under
`samples/copeland-ts`. Maintained executable qualification/measurement tools live
under `tools`; the [JIT benchmarks](../../../tools/Copeland.Benchmarks.Jit/README.md)
are one example. Application libraries such as `Copeland.Editing` live under
`libraries`.

## Dependency contracts

`Copeland.TS` directly references Cope MIR, Profile, SpanAllocation, Markdown,
and Workspace. It does not reference a concrete output backend. Cope MIR remains
independent of frontend syntax and of the document/GPU models. There is no
universal compiler/backend interface or forced common IR across these domains.

Both C#/JavaScript backend projects currently reference Cope MIR **and**
`Copeland.TS`: their sidecar APIs use frontend-colocated Manifest contracts.
Their emitters consume MIR. This co-location is an existing dependency, not a
claim that the assemblies are frontend-independent. Extracting shared contracts
is separate architecture work; this cleanup neither duplicates nor relocates
them to make an old dependency-count assertion pass.

The [semantic ownership map](semantic-ownership.md) governs meaning. CLI/LSP,
backends, browser materialization, and renderer adapters consume or project
compiler facts; they must not become competing binders. Generated output remains
a projection as described in [artifact contracts](../reference/generated-artifacts.md).

## Build and verification lanes

Run these commands from the repository root with .NET 10:

```powershell
dotnet build Copeland.slnx -m:1
dotnet test Copeland.slnx --no-build -m:1
pwsh -NoProfile -File tools/Validate-CopelandTsTopology.ps1
pwsh -NoProfile -File tools/Test-CopelandReleaseClosure.ps1
```

`Copeland.TS.slnx` is the narrower compiler/backend lane; its project references
still bring in the frontend's required services. `Copeland.slnx` additionally
includes the CLI, SDK, and selected applications. Some browser integration
samples require TSPack/materialized inputs; see their own runners.
`Copeland.Release.slnx` declares package-root dependencies and release tests;
membership/closure validation is not a package-publication qualification.

The topology guard enumerates tracked and new unignored maintained files. It
validates Copeland-owned projects and their reference closure, missing paths,
cycles, permitted dependencies, and source/fixture ownership. It accepts absolute
or relative project references. Ignored build/scratch/generated trees and
unrelated optional tool projects are not source inputs to this check. The TSON
runtime API restriction is checked on emitted TSON fixtures, separately from
host-side JSON serialization, browser JSON boundaries, and CLR sidecar hosting.

Cross-system ownership is checked separately by
`tools/Validate-DependencyBoundaries.ps1`. It currently reports three production
edges and eight integration-test ownership issues, recorded in the
[5S audit](../reviews/copeland-ts-repository-5s-audit.md#repository-checks-need-repair-before-a-large-move).
Those remain explicit architecture follow-ups; the topology guard does not
silently grant an exception to the cross-system policy.
