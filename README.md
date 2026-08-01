# Copeland

Copeland TS is TypeScript redesigned as a coherent application language. It is
for TypeScript developers who want familiar source syntax without inheriting
JavaScript's coercion and dynamic-shape semantics, and for C# developers who
want that syntax to compile into an ordinary .NET project.

The product thesis is simple: keep TypeScript's readable surface, make the
language rules explicit and closed-world, and make npm and .NET deliberate,
typed boundaries. Copeland source lowers through `.cope` MIR to generated C#
or JavaScript. In a .NET project, MSBuild compiles the generated C# alongside
authored C#; `dotnet build`, `run`, `test`, and `publish` remain the normal
workflow.

## Start here

## Install, create, open, run

M0 is packaged for a local feed; it is not yet published to NuGet or the VS
Code Marketplace. With `<feed>` set to the directory containing the generated
`.nupkg` and `.vsix` artifacts:

```console
dotnet tool install --global Copeland.TS.Tool --version 0.1.0 --add-source <feed>
dotnet new install Copeland.TS.Templates@0.1.0 --nuget-source <feed>
code --install-extension <feed>/copeland-ts-0.1.0.vsix
dotnet new copeland-react -n HelloCopeland
cd HelloCopeland
dotnet run
```

The React starter opens its local URL and calls a Copeland-compiled CLR API.
Open the folder in VS Code and run **Copeland: Workspace Sync** where a
`tsconfig.tsx` workspace is present. The extension discovers `tscl` on PATH,
checks its compatibility, and launches `tscl language-server`; no server DLL
is copied into the project. Use `tscl doctor --format json` for a stable
installation diagnosis.

See [installation](docs/Copeland/installation.md), [templates](docs/Copeland/templates.md),
[version compatibility](docs/Copeland/version-compatibility.md), and
[troubleshooting](docs/Copeland/troubleshooting.md) for the local-feed M0
contract.

[The Copeland TypeScript authoring guide](docs/Copeland/authoring/copeland-typescript-guide.md)
is the **canonical current language guide**. It is written for people who
already know TypeScript and explains what stays familiar, what changes, why,
and the exact supported spelling to use. Architecture decisions and milestone
records are historical/design context, not competing language references.

Copeland differs from ordinary TypeScript in intentionally visible ways:

- `int` and `float` are distinct; `number` is an alias for `float`.
- Strings, numbers, booleans, records, and boundaries do not use implicit
  JavaScript coercion.
- Records are nominal, immutable, and closed; interfaces are constraints, not
  storage types.
- npm is available through declared static contracts; CLR APIs use C#-shaped
  `using` directives and direct generated C#.

## A small Copeland program

```ts
using System.Text.Json;
using Demo;

export record Person {
    name: string;
    age: int;
}

export function Describe(person: Person): string {
    const normalized = Names.Normalize(person.name);
    const json = JsonSerializer.Serialize(person);

    return `${normalized} is ${person.age}. ${json}`;
}
```

`Names` may be an authored C# type in the same project. The `using` directives
resolve CLR namespaces/types, while `import { Name } from "./Local"` resolves a
declared Copeland source module and `import { value } from "@scope/package"`
resolves a declared npm contract. They are three different domains.

## Use Copeland in a `.csproj`

Install the `Copeland.TS.Sdk` package, then opt in the Copeland sources:

```xml
<ItemGroup>
  <PackageReference Include="Copeland.TS.Sdk" Version="&lt;published-version&gt;" PrivateAssets="all" />
  <CopelandCompile Include="Copeland\**\*.ts" />
  <CopelandCompile Include="Copeland\**\*.tsx" />
</ItemGroup>
```

`CopelandCompile` is explicit: ordinary web `.ts`/`.tsx` files do not become
Copeland sources accidentally. The package emits generated C# below `obj`
before `CoreCompile`. Authored C# and Copeland can call supported declarations
from each other in the same final assembly; unresolved cross-language type and
inheritance cycles remain outside M1.

## Current maturity

The M1 authoring surface includes immutable records, payload enums and
exhaustive `match`, local modules, bounded CLR and npm interop, compiler-owned
async, `batch`, synchronous generators, typed flows, inline C#, and normal
SDK-project integration. Important deliberate limits include no default or
re-exports, no `.d.ts`/dynamic JavaScript boundary, no async generators, no
source-level flow session API, no React/Blazor integration, and no inline
JavaScript. See the [support matrix](docs/Copeland/authoring/copeland-typescript-guide.md#feature-support-matrix)
for the exact M1 boundary.

## Repository lanes

## Typed template artifacts (M0)

A Copeland template binds typed parameters and produces a typed entity. Angle
brackets express construction either compactly (`instantiate Bootstrap<...>`) or
hierarchically where TS-XML makes a tree clearer. `tscl template materialize`
evaluates that typed result and dispatches to a supported artifact materializer;
it is not a project-only command.

Generated code uses a declared language rather than an opaque string:

```tsx
sourceFile<CSharp>("src/Helper.cs", { ProjectNamespace: name }, code {
    namespace ProjectNamespace;
    public static class Helper { }
})

sourceFile<CopelandTS>("src/View.tsx", code {
    export function view(): Document {
        return <Document><Paragraph>Hello</Paragraph></Document>;
    }
})
```

M0 recognizes `CopelandTS`, `CopelandTest`, and `CSharp`. Source parameters are
explicit imports: no enclosing template local is ambiently visible. Imported
M0 values are identifier-role strings, validated before identifier replacement,
so they cannot inject declarations or arbitrary tokens. C# bodies
are syntax-validated by Roslyn; Copeland bodies are parsed as modules (including
nested TS-XML). Raw `sourceFile(path, text)` and `testFile(path, text)` remain
available as explicit low-level escape hatches.

Documents and components remain ordinary typed template results. A future
component consumer is explicit in the type family (`Component<React>`); `.tsx`
alone does not select React. Unsupported result types report that no filesystem
artifact materializer is available. M0 intentionally does not provide arbitrary
language embedding, XML control flow, token pasting, or arbitrary renderer
materialization.

This monorepo also contains Machina.UI and Aurelian. Copeland implementation
and tests live under `src/Copeland` and `tests/Copeland`; the focused Copeland
documentation landing page is [docs/Copeland](docs/Copeland/README.md).

```powershell
dotnet build Copeland.slnx --no-restore
dotnet test Copeland.slnx --no-build
```
