# CTS-CLR-M1: bounded CLR metadata interop

> **Historical decision record.** Read the [canonical Copeland TypeScript
> authoring guide](../Copeland/authoring/copeland-typescript-guide.md) for the
> current language surface. This decision predates ordinary `.csproj`
> integration and same-project authored C# declaration projection.

## Decision

Copeland TypeScript retains its existing compilation route:

```text
.ts/.tsx -> Copeland frontend -> .cope MIR -> generated C# -> normal .NET build
```

CLR interop is a frontend binding capability, not an IL backend and not a generated-C# text heuristic. The compiler reads framework assemblies already available to its process plus explicit, already-built assembly paths supplied through `CopelandCompilationOptions.ClrReferences`. It does not restore NuGet packages, discover MSBuild graphs, load authored project C#, or create cross-language cycles.

## Resolution ownership and grammar

`import` remains exclusively the npm/module mechanism. It is validated only through the npm dependency contract and never probes CLR metadata. Module-level:

```ts
using System.Text.Json;
using System.IO;
```

is exclusively a CLR namespace/type directive. It never resolves npm, local TypeScript, or Copeland modules. A missing CLR namespace/type produces `COPE-CLR-0001`; missing npm imports retain their `COPE-NPM-*` diagnostics.

The grammar is deterministic by syntax and placement:

- module-level `using Qualified.Name;` is `ClrUsingDirectiveSyntax`;
- `using identifier = expression;` is `ResourceUsingDeclarationStatementSyntax`;
- `await using identifier = expression;` is also a resource declaration, not a CLR import.

Synchronous resource declarations bind only CLR `IDisposable` values and emit `using var`. `await using` is parsed but deliberately diagnoses `COPE-CLR-0008`: async disposal and the broader async integration remain deferred.

## Binding, projection, and MIR

The binder resolves imported namespaces/types, selects public constructors, public static methods, public instance methods, and readable public properties. A resolved CLR invocation/property carries assembly identity, namespace, declaring metadata type, member identity, static/instance/constructor facts, parameter/result types, and inferred generic arguments through `BoundClr*` into `MirClr*`. The C# backend consumes that identity directly; it does not search metadata or rerun overload resolution.

CTS-CLR-M1 projection is intentionally conservative:

- `boolean`, `string`, `void`, and numeric CLR integral/float results map to Copeland primitives; authored `number` arguments bind only to CLR `double`.
- One-dimensional arrays project recursively.
- Public non-generic CLR named types are retained as CLR types for receivers and exact CLR-type flow.
- A generic method may infer a type parameter only from an exact direct parameter occurrence; this covers `JsonSerializer.Serialize<T>(record)`.
- Flat Copeland records may cross this generic boundary. When System.Text.Json is used, generated record properties are exposed with compiler-emitted Json metadata so authored field names serialize predictably.
- `object`, nullable types, enums, pointers, `ref`/`out`, params arrays, generic CLR named types, unresolved generic inference, delegates/events/callbacks, write-only properties, and inaccessible/non-public members are rejected.

Overload selection is deterministic: exact projected match first; the only authored numeric argument conversion is `number -> System.Double`; otherwise a candidate is inapplicable. One candidate wins, zero produces `COPE-CLR-0005`, and multiple produce `COPE-CLR-0006`.

## C# realization and diagnostics

Emission uses fully-qualified direct C# such as:

```csharp
using var reader = new global::System.IO.StreamReader(path);
var json = global::System.Text.Json.JsonSerializer.Serialize<__CopeRecord_r1>(person);
```

There is no emitted reflection, `dynamic`, generic CLR bridge, expression-tree dispatch, or stringly host call.

The CLR diagnostic family currently distinguishes unavailable namespace/type (`0001`), ambiguous imported type (`0002`), missing member (`0003`), inaccessible member (`0004`), no applicable overload (`0005`), ambiguous overload (`0006`), unsupported projection/member shape (`0007`), deferred async resource disposal (`0008`), local/imported name conflict (`0009`), and invalid directive placement (`0010`).

## Explicit boundary

This milestone does not implement ordinary `.csproj` integration, authored C# projection, source generators/analyzers, NuGet restore, delegates/events, P/Invoke, COM, arbitrary reflection, writable properties, general nullable/object/enums/generic CLR types, or Task adaptation. Those later integrations can consume the same explicit CLR symbol/MIR identity rather than replacing this binding boundary.
