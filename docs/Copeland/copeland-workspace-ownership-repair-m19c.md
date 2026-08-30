# Copeland workspace ownership repair — M19c

## Root cause

`Copeland.TS.Workspace.csproj` disabled default compile items and linked `../Copeland.Cli/WorkspaceCommand.cs`. `Copeland.Cli.csproj` also compiled that physical file normally and referenced `Copeland.TS`, which references `Copeland.TS.Workspace`. The result was two assemblies exporting the same five public workspace contract types under namespace `Copeland.Cli`.

When `JointTaskForce.slnx` built the full graph, `Copeland.Cli` saw its source definitions and imported definitions from `Copeland.TS.Workspace`, producing approximately 30 `CS0436` use-site conflicts. This was a source-ownership conflict, not two historically divergent implementations.

## Conflicting types

| Type | Old effective owners | Public/API role | Compatibility consumers |
|---|---|---|---|
| `CopelandWorkspaceOwnership` | `Copeland.Cli` and `Copeland.TS.Workspace` | Public side-effect-free ownership resolver | CLI build contract, Copeland TS compiler, MSBuild task |
| `CopelandWorkspaceOwnershipResult` | both assemblies | Public result projection | same consumers |
| `CopelandWorkspaceTarget` | both assemblies | Public configured backend/runtime target | CLI build contract, compiler |
| `CopelandWorkspaceOwnedSource` | both assemblies | Public resolved TSCL source record | compiler, MSBuild |
| `CopelandWorkspaceOwnershipDiagnostic` | both assemblies | Public diagnostic projection | compiler, MSBuild, CLI |

The same linked file also contained internal parser, value syntax, glob, resolver, manifest, ownership, result, and generated-artifact helpers plus the CLI `WorkspaceCommand`. Those internal types did not create imported public-type conflicts, but compiling them twice meant implementation ownership was still ambiguous.

## Canonical owner

`Copeland.TS.Workspace` is now the one semantic and physical owner.

- `WorkspaceCommand.cs` moved from `src/Copeland/Copeland.Cli` to `src/Copeland/Copeland.TS.Workspace`.
- `Copeland.TS.Workspace.csproj` now uses ordinary default compile discovery instead of linking a CLI-owned source file.
- `Copeland.Cli.csproj` now has an explicit project reference to `Copeland.TS.Workspace`.
- `Copeland.TS.Workspace` grants only `Copeland.Cli` internal visibility so the existing command adapter can reuse parser/resolver/artifact internals without widening the public API or cloning logic.

The namespace remains `Copeland.Cli` for source and binary compatibility. Renaming the namespace was unnecessary and would have widened the migration. The assembly identity is the ownership boundary.

## Ownership rationale

The shared logic is specifically Copeland TS workspace semantics: restricted `tsconfig.tsx` parsing, compiler owner partitioning, TSCL targets, workspace glob law, source ownership, diagnostics, and generated workspace projections. `Copeland.TS`, `Copeland.TS.MSBuild`, and the CLI all consume it. A new neutral “workspace framework” would add a project and abstraction without introducing a second domain.

The CLI command remains an internal orchestration adapter inside the workspace assembly for now because it directly uses the internal parser/result/artifact model. The executable owns command dispatch and explicitly references the assembly. Splitting that adapter behind a new public service would create API solely to avoid a narrow friend boundary and is not needed to establish one semantic owner.

## Public API and serialization

Public type full names, constructor shapes, namespaces, and record serialization shapes are unchanged. Consumers now bind those types only from `Copeland.TS.Workspace.dll`. JSON emitted by workspace commands and generated `tsconfig.generated.json`, `tscl-files.generated.props`, and `editor-ownership.generated.json` are unchanged because the implementation moved without semantic edits.

There is no warning suppression, alias, renamed duplicate, persistence redesign, CLI refactor, or global warning-policy change.

## Regression coverage

`WorkspaceOwnershipAssemblyTests.Workspace_ownership_contract_has_one_assembly_owner` asserts:

- `typeof(CopelandWorkspaceOwnership).Assembly` is `Copeland.TS.Workspace`;
- all five public contract types exist in that assembly;
- none of the five exists in `Copeland.Cli`.

Existing CLI, compiler, and MSBuild integration suites continue to exercise parsing, ownership, target selection, generated artifacts, diagnostics, and command behavior.

## Validation

Initial failure:

```text
JointTaskForce.slnx: approximately 30 CS0436 conflicts in WorkspaceCommand.cs
and TsclBuildContract.cs between source types in Copeland.Cli and imported types
from Copeland.TS.Workspace.
```

After repair:

```text
dotnet test Copeland.slnx -m:1            PASS
dotnet test JointTaskForce.slnx -m:1      PASS
focused ownership assembly test           PASS (1/1)
```

The full M19c validation record is in `artifacts/m19c/m19c-agent-presentation-recon-manifest.txt`.
