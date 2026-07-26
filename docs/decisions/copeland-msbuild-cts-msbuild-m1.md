# CTS-MSBUILD-M1: Copeland as an ordinary SDK project source language

> **Historical decision record.** For current authoring guidance, see the
> [canonical Copeland TypeScript authoring guide](../Copeland/authoring/copeland-typescript-guide.md).
> The same-project C# deferral below was superseded by
> [CTS-MIXED-M1](copeland-mixed-cts-mixed-m1.md).

## Decision

Copeland TS is integrated into an existing SDK-style `.csproj` as an explicit
MSBuild item, not as a second project system or package graph. The integration
package is `Copeland.TS.Sdk`; its implementation assembly is
`Copeland.TS.MSBuild`, with `build` and `buildTransitive` imports suitable for
NuGet distribution.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Copeland.TS.Sdk" Version="0.1.0" PrivateAssets="all" />
    <CopelandCompile Include="Copeland/**/*.ts" />
    <CopelandCompile Include="Copeland/**/*.tsx" />
  </ItemGroup>
</Project>
```

`CopelandCompile` is deliberately opt-in. A web project's ordinary `.ts` and
`.tsx` assets are not inputs merely because of their extension.

## Build model

The imported `CopelandCompile` target runs before `CoreCompile` and depends on
`ResolveReferences`. It receives `@(ReferencePath)` and
`@(ReferencePathWithRefAssemblies)` as structured MSBuild items, then passes
the existing compile-time metadata paths to `CopelandCompilationOptions.ClrReferences`.
This preserves the containing project's target framework, framework reference
assemblies, NuGet references, ProjectReference outputs, configuration/platform,
and normal restore ownership. It never scans runtime directories or restores
packages itself.

For each source the task retains the established pipeline:

```text
.ts/.tsx -> frontend -> .cope MIR -> generated C# -> Roslyn CoreCompile
```

The task writes `.cope`, generated `.g.cs`, and an incremental stamp beneath
`$(IntermediateOutputPath)Copeland` (normally `obj/<configuration>/<tfm>/Copeland`).
They are never authored files. The generated C# item is appended to `@(Compile)`
before Roslyn runs, so C# in the same project can call it normally. `Clean` and
rebuild own `obj`; additionally, each Copeland task execution removes stale
generated outputs for sources no longer in `@(CopelandCompile)`.

The M1 generated public surface is file-module based: a source named
`Greeting.ts` with a top-level `function Message(...)` is exposed as
`<RootNamespace>.Copeland.Greeting.Message(...)`. For the usual `Demo` root
namespace, C# can write:

```csharp
using Demo.Copeland;
System.Console.WriteLine(Greeting.Message("Copeland"));
```

This adapts the example to Copeland's current class law: associated functions
are declared without TypeScript's `static` modifier, and exported module-class
syntax is not yet an M1 source form. Two same-basename source files receive a
stable path-hash suffix rather than overwriting one another. A collision with an
authored C# type is intentionally a normal Roslyn duplicate-type error.

## Incrementality and diagnostics

The target always evaluates enough to register current generated source items,
but the compiler is skipped when a source's content, root namespace/module
mapping, compiler assembly version, and resolved reference path/identity inputs
match its stamp. Generated paths are stable; unchanged artifacts retain their
timestamps. A changed source or compile-reference input rebuilds its source.

Copeland parser/binder/lowering diagnostics are logged by the task with the
authored `.ts`/`.tsx` path and computed line/column, preserving their existing
`COPE-*` codes. Generated-C# diagnostics remain possible only for a backend
defect or a normal C# collision.

`.tsx` is accepted as an explicit item and uses the existing TS-XML parse mode.
This milestone does not promise React, JSX runtime, or frontend-tooling
compatibility.

## Supported and deferred directions

M1 proves ordinary console/app and test projects, `dotnet build`, `run`,
`test`, and `publish`; C# to generated Copeland calls; framework CLR calls; and
public `ProjectReference` CLR calls. NuGet references flow through the same
MSBuild reference projection, without a Copeland-specific restore path.

Copeland-to-authored-C# in the same project is now supported through the
bounded declaration projection in [CTS-MIXED-M1](copeland-mixed-cts-mixed-m1.md).
Framework/package/ProjectReference assemblies remain supported as before.
Source generators, direct IL, reflection/dynamic dispatch, custom launchers,
npm/package management changes, IDE/LSP work, async Task adaptation, React/JSX
compatibility, and cross-language source cycles remain outside M1.
