# Copeland

This repository brings together three projects in the Visionary stack:
the Copeland compiler, Machina.UI, and Aurelian. Each has its own source,
tests, documentation, and solution lanes. Cross-system adapters live under
[`src/Integrations`](src/Integrations).

## Copeland compiler

Copeland owns compiler infrastructure and explicit language frontends.
**Copeland TS is its first frontend**: TypeScript redesigned as a coherent
application language, with familiar syntax, explicit semantics, and typed
boundaries to JavaScript and .NET. The repository also contains a Markdown
compiler lane and compiler support used by the shader stack.

Copeland TS lowers through Cope MIR to C# or JavaScript. Generated C# joins
ordinary authored C# in an SDK project, so `dotnet build`, `run`, `test`, and
`publish` remain normal workflows. Records are nominal and immutable;
payload enums and exhaustive `match`, `Option`, typed errors, explicit captures,
and `flow`/`state` make application behavior visible in source. TableScript adds
typed columnar tables and derived calculations. Browser authoring includes
layout, streams, state/events, and explicit framework component boundaries.

### Start here

- [Documentation and current language references](docs/Copeland/README.md)
- [Copeland TS authoring guide](docs/Copeland/authoring/copeland-typescript-guide.md)
- [File profiles and traditional TypeScript ownership](docs/Copeland/language/copeland-ts-file-profiles.md)
- [SDK projects and templates](src/Copeland/Copeland.TS.Templates/README.md)
- [Preview release scope and validation boundary](docs/Copeland/releases/0.1.0-preview.1-scope.md)
- [CLI](src/Copeland/Copeland.Cli/README.md) and [VS Code extension](src/Copeland/Copeland.TS.VSCode/README.md)
- [Current examples and sample entry points](samples/copeland-ts/README.md)
- [TableScript café: editing, creation, deletion, and empty tables](samples/copeland-ts/tablescript-rows-m15/README.md)

Language capabilities and release qualification are separate: a development
sample does not establish that a feature ships in a particular package.

### Source and validation

| Location | Responsibility |
| --- | --- |
| [`src/Copeland`](src/Copeland) | Compiler, MIR, backends, CLI, workspace, SDK, editor tooling |
| [`tests/Copeland`](tests/Copeland) | Compiler, backend, CLI, and SDK regressions |
| [`samples/copeland-ts`](samples/copeland-ts/README.md) | Applications, integration fixtures, and milestone specimens |
| [`libraries`](libraries/README.md) | Shared typed editing and framework adapters |
| [`docs/Copeland`](docs/Copeland/README.md) | Language references, architecture, and review evidence |

From the repository root, with .NET 10:

```powershell
dotnet build Copeland.slnx -m:1
dotnet test Copeland.slnx --no-build -m:1
```

[`Copeland.TS.slnx`](Copeland.TS.slnx) is the narrower compiler/backend lane.
[`Copeland.slnx`](Copeland.slnx) also includes CLI, SDK, and selected integration
samples; some samples require separate browser/TSPack setup.
[`Copeland.Release.slnx`](Copeland.Release.slnx) declares the preview package
validation lane. See the [test-lane doctrine](docs/architecture/jtf-test-lane-doctrine.md)
for repository-wide lanes. [Repository maintenance](docs/Copeland/repository-maintenance.md)
and [current compiler topology](docs/Copeland/architecture/compiler-topology.md)
explain source ownership, cleanup policy, and remaining repository-check findings.

## Machina.UI for C#

Machina.UI is the C# UI stack: typed UI and document authoring, layout, text and
fonts, presentation, input routing, hit testing, and local UI state. Its
presentation contracts are renderer-neutral; concrete engine adapters belong
in integration projects.

- [Machina.UI documentation](docs/Machina.UI/README.md)
- [Source](src/Machina.UI) and [tests](tests/Machina.UI)
- [Core solution](Machina.UI.slnx) and [slow diagnostics/sample solution](Machina.UI.Slow.slnx)
- [Oblivion](docs/Oblivion/oblivion-first-class-extraction-m18c.md), the persistent technical-work application built with these UI layers

```powershell
dotnet build Machina.UI.slnx -m:1
dotnet test Machina.UI.slnx --no-build -m:1
```

## Aurelian application runtime

Aurelian supplies application and game runtime services: lifecycle and frame
coordination, worlds, simulation and actuation, assets, graphics and shader
execution, audio, and ML inference. Renderer implementations and optional
inference/agent integrations have explicit contracts and adapters. Individual
backends and experiments have their own qualification boundaries.

- [Aurelian documentation](docs/Aurelian/README.md)
- [Source](src/Aurelian) and [tests](tests/Aurelian)
- [Runtime solution](Aurelian.slnx) and [inference solution](Aurelian.Inference.slnx)
- [Games and runnable applications](Games/README.md)
- [Game starter](docs/Aurelian/game-starter.md) and [scene composition](docs/Aurelian/scene-composition.md)

```powershell
dotnet build Aurelian.slnx -m:1
dotnet test Aurelian.slnx --no-build -m:1
```

The [topology and ownership doctrine](docs/architecture/jtf-m0-topology-and-ownership.md)
and [semantic boundaries](docs/architecture/jtf-target-semantic-boundaries.md)
describe the intended ownership across all three projects. Current deviations
must be resolved explicitly; moving directories alone does not change ownership.
