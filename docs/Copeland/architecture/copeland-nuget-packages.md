# Copeland TS native NuGet packages (M1)

NuGet is the native package system for Copeland TS libraries. A native package
is an ordinary NuGet package with a CLR binary and a small, versioned Copeland
package contract. NuGet continues to own identity, versions, restore,
transitivity, target-framework and runtime asset selection, cache layout,
locks, provenance, and CLR assembly resolution. Copeland does not implement a
NuGet resolver and does not inspect the global NuGet cache.

## M1 package shape

```text
lib/net10.0/Example.Copeland.dll
ref/net10.0/Example.Copeland.dll       optional
buildTransitive/Example.Copeland.targets
copeland/contract.v1.json
```

The conventional build-transitive target contributes the exact installed
contract path:

```xml
<ItemGroup>
  <CopelandPackageContract Include="$(MSBuildThisFileDirectory)..\copeland\contract.v1.json" />
</ItemGroup>
```

The Copeland SDK target passes `@(CopelandPackageContract)` to
`CopelandCompile`, alongside normal `@(ReferencePath)` inputs. The law is:

```text
NuGet restore → buildTransitive exact item → CopelandCompile contract input
```

There is no directory probing, package-cache scanning, guessed install path, or
private dependency graph in that path. Package contracts naturally flow through
NuGet `buildTransitive`; M1's executable fixture proves a direct
PackageReference.

## `contract.v1.json`

Schema version 1 contains only Copeland semantic metadata: package id, minimum
compiler version, module specifier, nominal scope, named exports and function
signatures, and declared realizations. For the M1 CLR function surface, each
export also has an explicit static-facade mapping:

```json
{
  "schemaVersion": 1,
  "package": { "id": "Example.Copeland" },
  "compiler": { "minimum": "1.0" },
  "modules": [{
    "specifier": "example/parser",
    "nominalScope": "Example.Copeland/example/parser",
    "exports": [{
      "name": "Parse",
      "kind": "function",
      "contract": {
        "parameters": [{ "name": "value", "type": "string" }],
        "returnType": "int"
      },
      "clr": {
        "type": "Example.Copeland.Copeland.Parser",
        "method": "Parse"
      }
    }],
    "realizations": {
      "clr": { "kind": "binary", "assembly": "Example.Copeland" }
    }
  }]
}
```

The implementation validates the selected assembly, public facade type, static
method, parameter types/count, and return type before lowering. The generated
C# makes a normal direct call such as
`global::Example.Copeland.Copeland.Parser.Parse(value)`—no reflection or
runtime registry is emitted. The bound package symbol retains the package id,
assembly identity, module specifier, nominal scope, and export identity; short
export names are not nominal identities.

## `import` is not `using`

```ts
import { Parse } from "example/parser";
using Example.Runtime;
```

`import` first treats relative specifiers as local module semantics. A bare
specifier is resolved only through an explicit Copeland package contract map
(or the existing distinct npm contract path). It must have exactly one owner;
two native contracts—or native and npm ownership—are reported as ambiguity and
are never chosen by item order. A native package module does not fall back to
npm after an incomplete native match.

`using` remains normal CLR namespace/type lookup over assemblies selected by
MSBuild/NuGet. A PackageReference does not make every namespace in that
assembly a Copeland package module. A package can deliberately expose both
surfaces without merging their lookup spaces.

M1 supports `clr.binary` only. A Node or browser compilation importing a
CLR-only package reports the module, package, requested backend, and available
realizations before emission. The schema leaves room for later `js.node` and
`js.browser` declarations but implements neither.

## Diagnostics and boundaries

The package reader diagnoses missing item paths, malformed JSON, unsupported
schema versions, and incompatible compiler minimums. Binding diagnoses absent
modules/exports, duplicate ownership, unsupported export kinds, missing CLR
realizations, unavailable assemblies/facades, and contract/binary mismatch at
the authored import or imported name.

M1 deliberately does not include Copeland source packages, MIR packages,
hybrid packages, npm publication, TSPack integration, Node sidecars, or a
custom lockfile. Source and MIR are deferred until their compiler-version,
schema, ABI, nominal-identity, portability, and diagnostics ownership policies
are stable. An npm JavaScript compatibility package is a future separate
artifact; it is not the native NuGet package.

`ProjectReference` contract propagation is not implemented in M1. Local
development can use normal local Copeland modules and CLR ProjectReferences;
the canonical distribution proof is ordinary NuGet `PackageReference`.

## Executable fixture

[`tests/fixtures/copeland-nuget-m1/README.md`](../../../tests/fixtures/copeland-nuget-m1/README.md)
contains the producer, consumer, isolated feed/configuration, and exact
pack/restore/build/run commands. It prints `42`, proving both the package
module import and CLR `using` surface from the same package assembly.
