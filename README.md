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

This monorepo also contains Machina.UI and Aurelian. Copeland implementation
and tests live under `src/Copeland` and `tests/Copeland`; the focused Copeland
documentation landing page is [docs/Copeland](docs/Copeland/README.md).

```powershell
dotnet build Copeland.slnx --no-restore
dotnet test Copeland.slnx --no-build
```
