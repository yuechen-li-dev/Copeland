# Copeland monorepo

Copeland is a compiler-infrastructure repository with three physically separated subsystem lanes: Copeland, Machina.UI, and Aurelian. Assembly names and namespaces retain their existing identities during the JTF-M0 topology milestone.

## Subsystems

- `src/Copeland` and `tests/Copeland`: compiler conventions, diagnostics, source provenance, frontend/parsing/lowering, MIR, artifacts, Copeland TS/Markdown lanes, and compiler CLI surfaces.
- `src/Machina.UI` and `tests/Machina.UI`: C# UI authoring, layout, text/font work, presenter composition, input routing, hit testing, local UI state, and the existing Machina renderer-facing projects.
- `src/Aurelian` and `tests/Aurelian`: engine lifecycle, world and game-object models, actuation, frame coordination, renderer-neutral contracts, renderer backends, assets, shaders, and Dominatus-backed engine runtime.
- `src/Integrations` and `tests/Integrations`: reserved for explicitly named cross-subsystem adapters such as the future `Aurelian.Machina` lane. JTF-M0 adds no production bridge API.

Samples are grouped under `samples/Machina.UI` and `samples/Aurelian`. The umbrella solution includes them for repository-wide validation.

The authoritative current doctrine is [JTF-M0 topology and ownership](docs/architecture/jtf-m0-topology-and-ownership.md). Subsystem documentation is available in [Copeland docs](docs/Copeland/README.md), [Machina.UI docs](docs/Machina.UI/README.md), and [Aurelian docs](docs/Aurelian/README.md). Historical milestone records remain under subsystem `history` directories and `docs/migrations`.

## Build and test lanes

```powershell
dotnet build Copeland.TS.slnx
dotnet test Copeland.TS.slnx --no-build

dotnet build Copeland.slnx
dotnet test Copeland.slnx --no-build

dotnet build Machina.UI.slnx
dotnet test Machina.UI.slnx --no-build

dotnet build Aurelian.slnx
dotnet test Aurelian.slnx --no-build

dotnet build JointTaskForce.slnx
dotnet test JointTaskForce.slnx --no-build

# Explicit expensive lanes
dotnet build Machina.UI.Slow.slnx
dotnet test Machina.UI.Slow.slnx --no-build --blame-hang-timeout 180s
dotnet build JointTaskForce.Integration.slnx
dotnet test JointTaskForce.Integration.slnx --no-build

pwsh ./tools/Validate-DependencyBoundaries.ps1
pwsh ./tools/Validate-CopelandTsTopology.ps1
```

`Copeland.TS.slnx`, `Copeland.slnx`, `Machina.UI.slnx`, and `Aurelian.slnx` are independent fast reviewer lanes. `JointTaskForce.slnx` is the repository-wide fast lane and includes production projects, contract tests, and samples. `Machina.UI.Slow.slnx` owns visual, artifact, font-diagnostic, gallery, presenter, and playback proofs. `JointTaskForce.Integration.slnx` owns explicit Aurelian integration and visible-sample proofs. See the [test-lane doctrine](docs/architecture/jtf-test-lane-doctrine.md).

## Compiler pipeline

The Copeland compiler lanes currently include Copeland TS and the bounded Markdown frontend. The TS lane lowers to independently owned Cope MIR, then uses the C# proof backend; a JavaScript backend remains future work. The implementation remains lane-specific; no universal compiler IR is introduced.

```text
source -> frontend/parser -> lane MIR -> lowering -> artifacts or CLR proof
```

The CLI entry point is `src/Copeland/Copeland.Cli`.

Copeland TS also supports synchronous structured mapping with `batch values as
value { return transform(value); }`. The compiler owns scheduling: CLR uses a
bounded parallel realization, while JavaScript preserves the same semantics
with a sequential fallback. See the [batch language decision](docs/Copeland/language/copeland-ts-batch-cts-batch-m1.md).

## Copeland TS in an SDK project

Copeland TS can be added as explicit source items to a normal SDK-style project.
The package contributes an MSBuild target that emits C# under `obj` before
Roslyn's `CoreCompile`; `dotnet build`, `run`, `test`, and `publish` remain the
only commands required. See the [MSBuild integration decision](docs/decisions/copeland-msbuild-cts-msbuild-m1.md)
for the package shape, source ownership, generated API mapping, and current
language limitations.

Authored C# declarations in that same project are also available to Copeland's
CLR `using` domain. For example:

```csharp
// Names.cs
namespace Demo;
public static class Names
{
    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
```

For incremental migration, a function may contain an explicitly delimited,
typed native C# block:

```typescript
using Demo;
function Normalize(value: string): string {
    csharp {
        return Names.Normalize(value);
    }
}
```

The block is compiled as ordinary project C# and is not sandboxed. It can capture
only values with an existing CLR projection and cannot assign to those captures.
It is unavailable for the JavaScript backend; arbitrary inline JavaScript is not
supported (declared npm contracts remain the JavaScript interop boundary). See
[the inline-C# decision](docs/decisions/copeland-inline-csharp-cts-csharp-blocks-m1.md).

```typescript
// Greeting.ts
using Demo;
function Message(name: string): string {
    return Names.Normalize(name);
}
```

The generated C# calls `Names.Normalize` directly, while authored C# may call
the generated `Demo.Copeland.Greeting.Message` in the same final assembly. See
the [same-project C# declaration projection decision](docs/decisions/copeland-mixed-cts-mixed-m1.md)
for supported members and deferred cross-language cycles.

## Ownership and history

Reviewers should default to these write scopes:

- Copeland: `src/Copeland`, `tests/Copeland`, `docs/Copeland`
- Machina.UI: `src/Machina.UI`, `tests/Machina.UI`, `docs/Machina.UI`
- Aurelian: `src/Aurelian`, `tests/Aurelian`, `docs/Aurelian`
- architecture/orchestration: `src/Integrations`, `tests/Integrations`, root solution/build files, and repository-wide architecture/decision documents

JTF-M0 is organizational only. It does not rename assemblies or namespaces, split projects, change dependency direction by semantic migration, add the `Aurelian.Machina` bridge, or intentionally change runtime behavior or public APIs. See the [migration record](docs/migrations/jtf-m0-topology.md) for the exact scope.
