# Copeland TypeScript authoring guide

> **Canonical current guide.** This is the user-facing source of truth for
> Copeland TS in M1. Milestone records and architecture decisions are historical
> context; they do not override this guide.

Copeland is for authors who already know TypeScript. Keep its readable
declarations, functions, generics, modules, `async`/`await`, object-literal
notation, and `for...of`. Replace JavaScript's open-ended runtime assumptions
with explicit, portable language rules.

## Copeland in one page

Traditional TypeScript describes JavaScript values: one `number` type, dynamic
shapes and coercion, `Promise`-shaped runtime abstractions, and npm as the
primary ecosystem. Copeland keeps familiar syntax but owns a stricter language:

- `int` and `float` are distinct; `number` is a transparent alias for `float`.
- There is no implicit numeric, string, boolean, or host coercion.
- Records are nominal, immutable, and closed. Interfaces are erased generic
  requirements, not runtime values.
- npm calls cross declared static contracts. CLR calls use C#-shaped `using`
  directives and direct generated C#; neither route admits dynamic values.
- The compiler owns async semantics. `batch`, synchronous generators, and
  `flow`/`state` are language features.
- Source lowers through `.cope` MIR to generated C# or JavaScript. In a .NET
  project, generated C# compiles with ordinary authored C#.

Write ordinary-looking TypeScript when its meaning is already static and
explicit. Name a conversion or boundary where JavaScript would silently guess.

## Quick start in an SDK project

Install the `Copeland.TS.Sdk` package and opt in the Copeland source files. The
package and item names below are current; substitute a published version.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Copeland.TS.Sdk" Version="&lt;published-version&gt;" PrivateAssets="all" />
    <CopelandCompile Include="Copeland\**\*.ts" />
    <CopelandCompile Include="Copeland\**\*.tsx" />
  </ItemGroup>
</Project>
```

`CopelandCompile` is opt-in. The package emits C# below `obj` before Roslyn's
`CoreCompile`; `dotnet build`, `run`, `test`, and `publish` stay the workflow.

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

`Names` can be an authored C# type in the same project:

```csharp
namespace Demo;
public static class Names
{
    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
```

The call becomes direct C#. Authored C# can call generated Copeland module APIs
in the same final assembly. Do not create cross-language base, constraint,
partial-type, or recursive declaration cycles.

## Traditional TypeScript versus Copeland

### Numeric types and conversion

Traditional TypeScript instinct: use `number` for every numeric value and let
integers flow into floating arithmetic.

Copeland law:

- Integer literals infer `int`; decimal literals infer `float`.
- `number` is a transparent alias for `float`, not a third numeric type.
- Direct integer literals adapt to a known `float`/`number` destination. Stored
  `int` values never widen implicitly.
- Arithmetic is homogeneous: `int + int` and `float + float` work; stored
  `int + float` is rejected.
- `int` is signed 32-bit. `float`/`number` use binary64.

```ts
const count = 3;       // int
const ratio = 3.0;     // float
const legacy: number = 3.0;
const direct: float = 3;

const widened = Float.From(count);
const total = widened + ratio;
```

Rejected:

```ts
const count: int = 3;
const ratio: float = 3.0;
const total = count + ratio;
```

Repair it with `Float.From(count)`. `Float(count)` is accepted
TypeScript-shaped sugar. It does not parse text: `Float("3")` is rejected.
There is no `Int(value)`: float-to-int conversion names a policy.

```ts
const floor: int = Int.Floor(ratio);
const ceiling: int = Int.Ceil(ratio);
const rounded: int = Int.Round(ratio);
const truncated: int = Int.Truncate(ratio);
```

`Int.Round` is half-away-from-zero. Conversion rejects non-finite or
out-of-range results rather than silently changing the value.

### String conversion and formatting

Traditional TypeScript:

```ts
const message = "Count: " + count;
```

Copeland law: `+` concatenates only `string + string`; it never invokes
JavaScript string conversion.

```ts
const message = `Count: ${count}`;
const alsoMessage = "Count: " + String.From(count);
const compatibleSugar = "Count: " + String(count);
```

`String.From` is canonical and `String(value)` is bounded TypeScript-compatible
sugar. Both accept `string`, `boolean`, `int`, and `float`/`number`, use
invariant formatting, and are the conversion law used by interpolation.
Interpolation evaluates expressions left-to-right once. For localized/richer
formatting, call an explicitly typed CLR API; parsing and localization remain
deferred.

### Records and object shape

Traditional TypeScript instinct: an object literal is structural, and code can
add, delete, or replace properties. Copeland records are named immutable product
types. A brace literal needs an expected record type and supplies every declared
field exactly once.

```ts
export record Person {
    name: string;
    age: int;
}

function Birthday(person: Person): Person {
    return person with { age: person.age + 1 };
}

function Create(): Person {
    return { name: "Ada", age: 37 };
}
```

Rejected:

```ts
person.age = 38;
person.extra = 3;
delete person.name;
const anonymous = { name: "Ada" };
```

Use `with` only to update an existing field. To make a new shape, declare a
second record and construct it explicitly. Flat records cross current CLR/npm
boundaries; dynamic shapes and nested npm arrays do not.

### Payload enums and `match`

Use nominal payload enums and exhaustive `match` instead of structural
discriminated unions plus `switch`:

```ts
export enum Decision {
    Ready(person: Person),
    Skip(reason: string),
}

export function DescribeDecision(decision: Decision): string {
    return match decision {
        Ready(person) => `${person.name} is ready`,
        Skip(reason) => "Skipped: " + reason,
    };
}
```

Every case must be covered and each binding must match its payload. Ordinary
source `switch` is not supported; `match` is the exhaustive tagged-data form.

### Interfaces are constraints, not storage types

This is the most important surprise for TypeScript authors.

Traditional TypeScript:

```ts
interface HasName { name: string; }
function Read(value: HasName): string { return value.name; }
```

Copeland law: interfaces are erased field requirements. They cannot be a
parameter, local, field, array element, or any other storage type. Use a closed
generic constraint:

```ts
interface HasName { name: string; }

function Read<T extends HasName>(value: T): string {
    return value.name;
}

record PersonName { name: string; }
const person: PersonName = { name: "Ada" };
const inferred = Read(person);
const explicit = Read<PersonName>(person);
```

M1 interfaces have fields only, disappear before runtime, and never mean
TypeScript structural storage.

### Modules: local, npm, and CLR are different domains

```ts
import { BuildPlan as Build } from "./Planning"; // declared local source
import { normalize } from "@scope/package";      // declared npm contract
using System.Text.Json;                           // CLR namespace/type
```

Local imports resolve only within explicit `CopelandCompile` sources. `./Name`
tries `Name.ts` then `Name.tsx`; explicit `.ts`/`.tsx` and normalized `../` are
accepted only when the target is already declared in the source set.

```ts
// Planning.ts
export function BuildPlan(count: int): int { return count + 1; }

// Main.ts
import { BuildPlan as Build } from "./Planning";
export function Run(): int { return Build(3); }
```

Only named exported declarations cross a local module boundary. Aliases change
local spelling only. Cycles are rejected with their full path. Importing a flow
is deferred because M1 has no source-level flow value/session model.

M1 has no default imports/exports, namespace imports, re-exports/barrels,
side-effect or dynamic imports, CommonJS, `.d.ts`, `tsconfig` paths/baseUrl,
package export maps, directory `index.ts`, JSON/assets, or fallback between
local and npm resolution.

### CLR interop

Use module-level dotted `using` for CLR namespaces/types. It emits direct C#:

```ts
using System.IO;
using System.Text.Json;

function Serialize(person: Person): string {
    return JsonSerializer.Serialize(person);
}
```

Constructors, static/instance methods, readable properties, one-dimensional
arrays, bounded overload selection, and exact direct generic inference are
supported. Framework, package, `ProjectReference`, and supported same-project
C# declarations share this typed domain.

Do not confuse a CLR import with resource management:

```ts
using System.IO;                       // CLR import
using reader = new StreamReader(path); // IDisposable resource declaration
```

`await using` is parsed as resource syntax and rejected in M1. There is no
reflection, `dynamic`, object fallback, writable-property projection,
events/delegates, broad nullable/object/enum shapes, or automatic `Task`
adaptation.

### npm interop

Traditional TypeScript instinct: import declarations and pass arbitrary package
values. Copeland law: a project-owned, resolved, materialized static contract
names each npm export, positional parameter/result types, optional typed remote
error, and Promise-returning shape. It is not inferred from `.d.ts`, and source
does not name a transport.

```ts
import { sum as remoteSum } from "@fixture/math";

record RemoteError { message: string; }

async function Add(left: number, right: number): number ! RemoteError {
    const pending: Async<number ! RemoteError> = remoteSum(left, right);
    return await pending;
}
```

Only named imports and positional calls are supported. Arguments/results are
primitive values, one-dimensional arrays, or flat immutable records. Nested
arrays, arbitrary objects, callbacks, classes, constructors, overloads,
iterators, streams, default/namespace imports, and re-exports are rejected.
JavaScript uses native ESM; CLR uses a compiler-owned Node sidecar/TSON path.
That realization is backend-private.

### Async

Write `async function` and `await`, but keep the return annotation as the
eventual Copeland result. Calling an async function yields `Async<T>`:

```ts
async function Increment(value: int): int {
    return value + 1;
}

async function Load(value: int): int {
    const pending: Async<int> = Increment(value);
    return await pending;
}
```

`Async<T>` is compiler-owned, not `Promise<T>` or `Task<T>`. Await only inside
an async function and only an `Async<T>`. `await operation?` means
`(await operation)?`, preserving typed Result propagation. M1 supports named
async functions, awaits in statements/returns/assignments, arithmetic/calls,
conditions, loops, and short-circuit expressions. Async arrows, async
generators, source cancellation, and some match/table/TSON suspension forms are
deferred.

### Inline C#

Use a statement-only CLR migration block for a small typed native operation:

```ts
using Demo;

function Normalize(value: string): string {
    csharp {
        return LegacyNormalizer.Normalize(value);
    }
}
```

The block is direct project C#, with typed captures/results; captures are
read-only. It is not sandboxed, reflection, or scripting. It is CLR-only:
there is no inline JavaScript. Use a declared npm contract for JavaScript
interop. C# expression blocks, top-level C# declarations, `await`/`Task<T>`
adaptation, and mutable captures remain deferred.

### `batch`

`batch` is a finite, one-dimensional structured map:

```ts
function Double(values: int[]): int[] {
    return batch values as value {
        return value * 2;
    };
}
```

The input evaluates once; each item body returns one value; output length and
input order are stable; the expression joins before returning. CLR uses bounded
parallel work and JavaScript a sequential fallback with the same observed law.
Worker count and scheduling are runtime-owned.

The body may use its item, local `let`/`const`, and safe immutable captures. It
may not mutate outer state, call CLR/npm/callables, use inline C#/async, or nest
`batch`. Async batch, reduction, filter, flattening, and arbitrary iterables
are deferred.

### Generators

```ts
export function* Values(): Iterable<int> {
    yield 1;
    yield return 2;
    yield break;
}

for (const value of Values()) {
    // lazy pull consumption
}
```

`yield value` is canonical; `yield return value` is an equal C#-friendly alias.
`return;` and `yield break;` complete the sequence. `yield*` delegates lazily.
Generators are synchronous, lazy pull sequences with native early-close cleanup
and one active consumer. Async generators, `await` in generators, `next(value)`,
consumer `throw()`, final return values, and inline C# generator bodies are not
supported.

### Flow/state

Flows are synchronous typed automata with a board, events, named states, and
exactly one initial state:

```ts
export flow PantryRun -> int ! string {
    board { servings: int = 0; }
    event Add(amount: int);
    event Close();

    state Planning initial {
        on Add(amount) when amount > 0 -> Planning {
            board.servings = board.servings + amount;
        };
        on Close() -> Completed;
    }

    state Completed { finish board.servings; }
}
```

`flow Name -> ResultType` types `finish`; `! ErrorType` types `fail`; `void`
permits `finish;`. A state/event pair has one transition. A false guard is
unhandled and changes nothing. Guards and updates may use only pure literals,
local/event bindings, primitive operators, and board reads: calls, async, npm,
CLR, `batch`, and inline C# are rejected. Updates commit atomically with the
target state.

The compiler retains a dedicated Bound/MIR graph. Generated CLR/JavaScript
session APIs are provisional. Do not author `Flow.start()`, first-class event
values, uniform `session.send(event)`, or source-level typed inspection yet.
Persistence/replay, hierarchy, stacks, parallelism, timers/retries, utility,
and async suspension are deferred.

## Intentional incompatibilities

| Traditional instinct | Copeland replacement |
| --- | --- |
| `any`, `unknown`, dynamic host values | Closed records/enums/arrays or typed npm/CLR contracts. |
| `null` / `undefined` sentinels | A payload enum or typed Result; M1 has no built-in `Option` name. |
| `if (value)` truthiness | An actual boolean, e.g. `if (count != 0)`. |
| String/numeric `+` | Interpolation, `String.From`, or `String`. |
| Stored `int` with float arithmetic | `Float.From(integerValue)` first. |
| `Int(value)` | `Int.Floor`, `Int.Ceil`, `Int.Round`, or `Int.Truncate`. |
| Dynamic object mutation | A declared record plus construction or `with` update. |
| `===` / `!==` | Typed primitive `==` / `!=`. |
| `switch` narrowing | Exhaustive `match` over a payload enum. |
| Untyped npm import / inline JavaScript | Declared npm contract. |
| Effects in a flow guard | Pure condition; perform effects outside the flow. |

`var`, `eval`, optional chaining, ternary `?:`, general structural object
literals, implicit globals, and ambient nullability are outside the profile.

## Familiar syntax with different meaning

| Spelling | Copeland meaning |
| --- | --- |
| `using System.IO;` | CLR namespace/type import. |
| `using value = expression;` | Synchronous disposable resource declaration. |
| `number` | Transparent `float` alias. |
| `interface HasName` | Erased requirement for `T extends HasName`, never storage. |
| `String(value)` | Bounded `String.From(value)` sugar, not JS coercion. |
| `Float(value)` | Bounded `Float.From(value)` sugar; no text parsing. |
| `Int(value)` | Rejected: specify rounding policy. |
| `yield return value` | Alias for `yield value`. |
| `import { X } from "./Local"` | Declared project-owned source module. |
| `import { Y } from "@scope/package"` | Declared npm contract. |
| `async` / `await` | Compiler-owned `Async<T>`, not host Promise/Task. |
| Flow API names | Generated API is provisional; source API is deferred. |

## When the compiler rejects ordinary TypeScript

| Rejected instinct | Correct Copeland repair | Reason |
| --- | --- | --- |
| `const s = "count: " + count;` | ``const s = `count: ${count}`;`` | No implicit string conversion. |
| `function Read(value: HasName): string` | `function Read<T extends HasName>(value: T): string` | Interfaces are requirements. |
| `const x: float = storedInt;` | `const x = Float.From(storedInt);` | Stored ints never widen implicitly. |
| `value.extra = 3;` | Declare the target record with `extra`, then construct it. | Record shapes are fixed. |
| `value.field = next;` | `const updated = value with { field: next };` | Immutable existing-field update. |
| Default local import | `import { ExportedName } from "./Local";` | Named module surface only. |
| npm call with no setup | Declare and materialize the named npm contract. | Boundaries are static. |
| `await using` | Synchronous `using` for `IDisposable`, or code outside M1. | Async disposal is deferred. |

Diagnostics should name these repairs. In particular, mixed numeric arithmetic
points to `Float.From`, mixed string/numeric `+` lists interpolation and
`String.From`, interface storage misuse prints the generic-constraint form, and
module diagnostics distinguish a missing project module from a missing npm
contract. If a diagnostic conflicts with this guide, report a documentation or
diagnostic defect; do not invent an undocumented fourth rule.

## Feature support matrix

| Supported now | Bounded/deferred |
| --- | --- |
| Immutable nominal records and `with` | Dynamic shapes, recursive records, record equality/patterns/serialization |
| Payload enums and exhaustive `match` | General discriminated-union behavior and `switch` |
| Named local modules and aliases | Defaults, re-exports, barrels, `.d.ts`, index paths, path aliases |
| CLR `using` and same-project C# | Reflection/dynamic, broad CLR shapes, source generators visible to Copeland |
| Named static npm contracts | Dynamic values, callbacks/classes, nested arrays, package management from source |
| Named async functions | Async arrows/generators, source cancellation, full suspension coverage |
| `batch` map | Async/nested batch, reduction/filter/flatten, scheduling controls |
| Synchronous generators and `yield*` | `next(value)`, consumer `throw()`, final return values |
| Typed flows | Source session API, uniform events, persistence/replay/hierarchy/stacks/utility |
| Typed inline C# | Inline JavaScript, C# expressions/declarations, await in blocks |
| `.csproj` integration | React, Blazor, Machina UI language integration, IDE/LSP |

## LLM authoring checklist

1. Use `int` for whole-number domain data and `float`/`number` for binary64;
   convert stored ints with `Float.From`.
2. Use interpolation or `String.From`; never concatenate a string with a
   non-string directly.
3. Declare records/enums first, give record literals an expected named type,
   and use `with` instead of mutation.
4. Use interfaces only as `T extends Interface` constraints.
5. Keep local imports relative/named, npm imports bare/contract-backed, and CLR
   imports dotted `using` directives.
6. Use `Async<T>`, `async`, and `await`, never authored Promise/Task types.
7. Keep batch bodies and flow guards pure. Never invent inline JavaScript or
   deferred source-level flow APIs.

For focused detail, see [numeric conversion](numeric-conversion-m1.md) and
[local modules](local-modules-m1.md). The [Copeland documentation landing page](../README.md)
links the corresponding implementation records.
