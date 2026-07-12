# Copeland monorepo

Copeland is a compiler-infrastructure repository with three physically separated subsystem lanes: Copeland, Machina.UI, and Aurelian. Assembly names and namespaces retain their existing identities during the JTF-M0 topology milestone.

## Subsystems

- `src/Copeland` and `tests/Copeland`: compiler conventions, diagnostics, source provenance, frontend/parsing/lowering, MIR, artifacts, Script/Markdown lanes, and compiler CLI surfaces.
- `src/Machina.UI` and `tests/Machina.UI`: C# UI authoring, layout, text/font work, presenter composition, input routing, hit testing, local UI state, and the existing Machina renderer-facing projects.
- `src/Aurelian` and `tests/Aurelian`: engine lifecycle, world and game-object models, actuation, frame coordination, renderer-neutral contracts, renderer backends, assets, shaders, and Dominatus-backed engine runtime.
- `src/Integrations` and `tests/Integrations`: reserved for explicitly named cross-subsystem adapters such as the future `Aurelian.Machina` lane. JTF-M0 adds no production bridge API.

Samples are grouped under `samples/Machina.UI` and `samples/Aurelian`. The umbrella solution includes them for repository-wide validation.

The authoritative current doctrine is [JTF-M0 topology and ownership](docs/architecture/jtf-m0-topology-and-ownership.md). Subsystem documentation is available in [Copeland docs](docs/Copeland/README.md), [Machina.UI docs](docs/Machina.UI/README.md), and [Aurelian docs](docs/Aurelian/README.md). Historical milestone records remain under subsystem `history` directories and `docs/migrations`.

## Build and test lanes

```powershell
dotnet build Copeland.slnx
dotnet test Copeland.slnx --no-build

dotnet build Machina.UI.slnx
dotnet test Machina.UI.slnx --no-build
dotnet build Machina.UI.Slow.slnx
dotnet test Machina.UI.Slow.slnx --no-build

dotnet build Aurelian.slnx
dotnet test Aurelian.slnx --no-build

dotnet build JointTaskForce.slnx
dotnet test JointTaskForce.slnx --no-build

pwsh ./tools/Validate-DependencyBoundaries.ps1
```

`Copeland.slnx`, `Machina.UI.slnx`, and `Aurelian.slnx` are independent reviewer lanes. `JointTaskForce.slnx` is the repository-wide lane and includes production projects, tests, and samples.

## Compiler pipeline

The Copeland compiler lanes currently include TypeScript-like Script compilation and the bounded Markdown frontend. The implementation remains lane-specific; no universal compiler IR is introduced by JTF-M0.

```text
source -> frontend/parser -> lane MIR -> lowering -> artifacts or CLR proof
```

The CLI entry point is `src/Copeland/Copeland.Cli`.

## Ownership and history

Reviewers should default to these write scopes:

- Copeland: `src/Copeland`, `tests/Copeland`, `docs/Copeland`
- Machina.UI: `src/Machina.UI`, `tests/Machina.UI`, `docs/Machina.UI`
- Aurelian: `src/Aurelian`, `tests/Aurelian`, `docs/Aurelian`
- architecture/orchestration: `src/Integrations`, `tests/Integrations`, root solution/build files, and repository-wide architecture/decision documents

JTF-M0 is organizational only. It does not rename assemblies or namespaces, split projects, change dependency direction by semantic migration, add the `Aurelian.Machina` bridge, or intentionally change runtime behavior or public APIs. See the [migration record](docs/migrations/jtf-m0-topology.md) for the exact scope.
