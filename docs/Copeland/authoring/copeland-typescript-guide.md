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

## xUnit tests

Copeland test modules use the `.tsxtest` extension and run through ordinary
xUnit.NET. They are not TS-XML documents and do not use TSPack `xtest`.

```ts
using Xunit;

import { Add } from "./Calculator";

[Fact]
export function Add_returns_sum(): void {
    Assert.Equal(42, Add(20, 22));
}

[Theory]
[InlineData(1, 2, 3)]
export function Add_returns_expected(left: number, right: number, expected: number): void {
    Assert.Equal(expected, Add(left, right));
}
```

The SDK discovers `**/*.tsxtest`. In a production project it creates an
auxiliary test project under `obj/CopelandTests` and invokes it through normal
`dotnet test`; the production assembly is referenced rather than recompiled.
`dotnet build` and `dotnet publish` compile and publish only production code.
The auxiliary project supplies the supported xUnit, runner, and test SDK
packages through normal NuGet restore, and the generated wrapper uses xUnit's
real discovery, filtering, execution, and reporting.

For the current SDK-style first slice, a colocated production project declares
`<IsTestProject>true</IsTestProject>` when it contains `.tsxtest` files. This
lets the ordinary test target invoke the generated auxiliary project; it does
not put test methods or xUnit references into the production assembly.

M1 accepts module-level exported functions with `[Fact]`, `[Theory]`, repeated
`[InlineData(...)]`, and `[Trait(...)]`. Fact functions must be parameterless;
Theory functions require InlineData with literal primitive arguments. Attribute
wrappers emit `#line` directives so xUnit failures name the authored
`.tsxtest`. Current M1 is synchronous and supports public production APIs.

For a dedicated xUnit project, add `xunit` and `xunit.runner.visualstudio`, and set
`<CopelandCompileTestsInProject>true</CopelandCompileTestsInProject>`. A
`.tsxtest` is discovered automatically and compiles into that test project, so
ordinary C# tests and Copeland tests coexist.

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

### Exhaustive `match` and `switch`

Use nominal payload enums and exhaustive pattern arms instead of structural
discriminated unions. `match` and `switch` are interchangeable forms of the
same exhaustive pattern construct: they share patterns, payload bindings, arm
type checks, diagnostics, lowering, and have no implicit fallthrough.

By convention, `match` often reads naturally for value decomposition, enums,
unions, and fallibility, while `switch` often reads naturally for event dispatch
and control flow. The compiler does not enforce that stylistic distinction.

Use fat-arrow arms; do not write `case` or `break`:

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

Every enum case must be covered and each binding must match its payload. A
missing arm is an error, and each arm must return a compatible result type.

For example, this reducer uses `switch` because the enum represents commands:

```ts
export record CounterState {
    count: int;
}

export enum CounterEvent {
    Increment,
    Reset,
}

export function Reduce(state: CounterState, event: CounterEvent): CounterState {
    return switch event {
        Increment => state with { count: state.count + 1 },
        Reset => state with { count: 0 },
    };
}
```

### Browser dispatch: explicit retained state

For the narrow browser host, `dispatch<State, Event>` expresses the whole
durable-state law:

```text
current state + event -> reducer -> replacement state -> render
```

```ts
const send: (event: CounterEvent) => void = dispatch<CounterState, CounterEvent>(
    { count: 0 },
    Reduce,
    state => setText("count", `Count: ${state.count}`));

onClick("increment", capture { send } () => SendIncrement(send));
```

The browser host retains one current immutable state value, invokes the typed
reducer with an event, replaces that value only with the reducer result, and
renders after a changed identity. It never inspects state fields. The reducer
is ordinary Copeland code and can be reused by tests or another host. The
returned sender remains a typed Copeland callable even though the browser host
provides the native event boundary.

Keep reducers deterministic and free of rendering, DOM writes, network calls,
timers, retries, and orchestration. Put browser writes in the render callback.
The dispatch form is intentionally smaller than `flow` and Dominatus: it has no
table DSL, middleware, async dispatch, effect queue, router, store, hooks, or
state-machine hierarchy. A future host may add a deliberately narrow
data-oriented table helper, but CTS-DISPATCH provides the general reducer only.

Do not replace dispatch with a mutable captured local. Callback captures remain
immutable; durable browser state belongs at the explicit dispatch boundary.

### Nominal unions are not inline TypeScript unions

Traditional TypeScript instinct: write `string | int` anywhere a value can be
either shape. Copeland's `|` declares a top-level nominal union of nominal
record alternatives; it is not a general inline type operator.

```ts
record Circle { radius: number; }
record Rectangle { width: number; height: number; }
type Shape = Circle | Rectangle;

function Area(shape: Shape): number {
    return match shape {
        Circle(value) => value.radius * value.radius,
        Rectangle(value) => value.width * value.height,
    };
}
```

Alternatives must be records, construction is contextual (`const shape: Shape =
circle`) or named (`Shape.Circle(circle)`), and imports use the normal
named-export law. Primitive alternatives and `value: string | int` are
rejected. See `Language/Valid/tagged-data/nominal-union-pipe.cl-valid.ts`.

### Pure classes and value-first operations

Copeland classes are immutable product values with one primary `constructor`
and associated functions. They are not JavaScript/C# receiver objects: there
is no `this`, inheritance, instance dot-call, or field-initializer law. Fields
are declared in the class, construction is `Person(...)`, and an operation
names its input explicitly.

```ts
class Person {
    name: string;
    age: number;
    constructor(name: string, age: number): Person { return { name, age }; }
    birthday(value: Person): Person { return value with { age: value.age + 1 }; }
}

const older: Person = Person("Ada", 41) |> Person.birthday;
```

`person.birthday()` is deliberately not a receiver call. `Person.birthday` is
an associated function reference; the pipe spells `Person.birthday(person)`.
See `Language/Valid/classes/person.cl-valid.ts` and
`Language/Valid/pipeline/pipeline-associated-callable-and-arrow.cl-valid.ts`.

### Fallibility: `try`/`except`, `err`, and `?`

Typed failure is an expression law, not JavaScript exception control flow.
`err(...)` constructs a Result where a Result type is expected; postfix `?`
propagates its typed error; and `try` consumes a value expression with an
`except` fallback.

```ts
function Read(): number ! string { return err("missing"); }
function Main(): number {
    return try { Read()? } except (error) { 0 };
}
```

Use `except`, not `catch`; the blocks are supported value blocks, not arbitrary
statement-oriented exception handlers. `throw` is not the normal typed-failure
mechanism. See `Language/Valid/fallibility/try-except-handled-err.cl-valid.ts`.

### Pipeline application

`value |> f` is exactly `f(value)`. It has lower precedence than calls, member
access, Result propagation (`?`), and record `with`, but higher precedence than
assignment. Chains associate left-to-right: `20 |> Increment |> Double` means
`Double(Increment(20))`.

The right side must already be a one-argument callable reference: a named or
imported function, associated function, callable value, or arrow. It creates
no receiver, partial application, slot filling, or runtime feature. For extra
arguments, use an arrow:

```ts
const renamed = person |> ((value: Person) => Rename(value, "Jr"));
```

`person |> Rename("Jr")` is rejected because the right side is a completed
call. Fixtures for chains, generic inference, precedence, classes, and batch
are under `Language/**/pipeline/`.

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

### Arrays

Arrays are finite, ordered `T[]` values. They expose only the small
consumption surface needed for ordinary application code:

```ts
const count: int = items.length;
const first: Item = items[0];

for (const item of items) {
    consume(item);
}
```

`length` returns `int`; array indexes must be `int`; and arrays satisfy the
same supported `Iterable<T>` protocol used by `for...of`. An index below zero
or at/after `length` deterministically fails with `Copeland array index is out
of bounds.` on both CLR and JavaScript. JavaScript never turns an out-of-range
read into `undefined`.

Arrays are not record tables or table columns: `items[index]` returns an array
element directly, whereas table and column indexing keep their table-specific
result/bounds semantics. Arrays can be returned from `batch`, stored in
records, and passed through supported local, CLR, and npm contracts.

`map`, `filter`, `reduce`, `find`, sorting, slicing, mutable array methods,
and iterator-helper APIs remain intentionally deferred.

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
| C-style `switch`/fallthrough | Exhaustive `switch` or `match` with `pattern => body` arms. |
| Untyped npm import / inline JavaScript | Declared npm contract. |
| Effects in a flow guard | Pure condition; perform effects outside the flow. |
| Optional field `name?: string` | A nominal payload enum that makes absence explicit. |
| `value ?? fallback` | Typed Result handling or a nominal payload enum; no JS null semantics. |
| `readonly T[]` | `T[]`; arrays expose no mutable array API. |
| `[int, string]` tuple type | A nominal record with named fields. |
| `try/catch` | `try { value? } except (error) { fallback }`. |
| Default parameter | Pass the value explicitly or define a small helper. |

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

## Canonical language fixture corpus

The executable language definition lives in
`tests/Copeland/Copeland.TS.Tests/Language`. Discovery is recursive and a
fixture's complete suffix is authoritative:

| Suffix | Meaning |
| --- | --- |
| `.cl-valid.ts` | Plain Copeland source that binds and lowers successfully. |
| `.cl-invalid.ts` | Plain Copeland source intentionally rejected. |
| `.cl-valid.tsx` | TS-XML source accepted by the current TS-XML parser boundary. |
| `.cl-invalid.tsx` | TS-XML source intentionally rejected. |

Folders are topical only. Add a current small specimen under the relevant
topic; invalid fixtures use a nearby `// expect: COPE-...` comment for the
primary diagnostic. The shared test reads only the full suffix and checks any
declared diagnostic IDs. TS-XML tests additionally inspect node shape and
source spans. Search this corpus—not binder internals—to find compiling class,
fallibility, union, yield, module, pipeline, and TypeScript-difference examples.
