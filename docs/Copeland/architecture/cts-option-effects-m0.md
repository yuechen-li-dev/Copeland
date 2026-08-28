# CTS-OPTION-EFFECTS-M0 architecture record

**Status:** implemented (2026-08-27)

## Baseline and audit

The milestone started from clean `main` at `ea7413a` with .NET SDK `10.0.302`.
The baseline solution test passed 1,648 tests and `git diff --check` was clean.

Copeland already had nominal payload enums, exhaustive expression-valued
`match`, single-evaluation match lowering, and private tagged realizations in
both C# and JavaScript. It also had structural `Result<T,E>` (spelled `T ! E`),
postfix Result propagation `?`, `try`/`except`, closed generic functions, generic
type syntax for compiler-owned families, fixed-shape records, ordinary call
binding, and explicit mutable arrays. It did not have generic enum declarations,
an absence type, or an ordinary-function effect summary. Template static
evaluation used a separate restricted bound algebra and could not classify
ordinary calls.

## Option semantic model

`Option<T>` is the one compiler-owned parametric enum family. Each closed type
encountered by binding synthesizes an ordinary nominal payload enum:

```text
Option<T> = None | Some(value: T)
```

The synthesized declaration is added to the normal bound enum list. Construction
uses `BoundEnumValueExpression`; inspection uses `BoundMatchExpression`; MIR uses
`MirEnum`, `MirEnumValueExpression`, and `MirMatchExpression`; both backends use
their existing private enum carriers. There are no Option-specific MIR nodes or
host-null sentinels. Closed carrier names are stable hashes of the semantic value
type, while diagnostics and tooling display `Option<T>`.

Generic enum declarations remain unsupported. Opening them solely for Option
would have expanded this milestone into a general generic-type-definition and
cross-module specialization project. The closed compiler-owned family is the
smallest parametric type needed here; it is not a set of hand-written
`OptionInt`/`OptionString` types.

`Some(value)` and `None` are contextual constructors at an expected `Option<T>`
boundary. Plain `T` also lifts to `Some(T)` only at such a boundary. There is no
implicit `Option<T> -> T`. An unconstrained `Some(value)` is rejected. Ordinary
unqualified enum case patterns provide `Some(value)` and `None` in `match`, so
normal enum exhaustiveness diagnostics apply unchanged.

Nested Option is preserved. `Option<Option<T>>` distinguishes `None`,
`Some(None)`, and `Some(Some(value))`.

## Optional records and operators

`field?: T` binds as a fixed record field of semantic type `Option<T>`. Omission
from an object literal synthesizes `None`; a supplied `T` is contextually lifted;
explicit `Some(...)` and `None` are accepted. The runtime record always owns the
field.

`left ?? fallback` lowers directly to an ordinary enum match. The left expression
is the single match scrutinee and the fallback exists only in the `None` arm, so
evaluation is once-only and lazy. It unwraps exactly one Option layer and never
uses truthiness.

`receiver?.member` and `receiver?.method()` lower to an ordinary match over the
receiver. Member/call binding occurs against the `Some` payload. A non-Option
projection is wrapped in `Some`; an already-Option projection is returned
directly. Thus `?.` flattens exactly one projected Option layer as TypeScript-
familiar chaining sugar, while explicit matching and all other Option operations
preserve nesting. Optional call uses the same rule. String `.length` is an
ordinary deterministic member and array/MutableArray operations compose normally.

Postfix `?` remains Result propagation only. `foo()?.bar` is type-directed: a
Result receiver means propagate once and then perform ordinary member access;
an Option receiver means optional chaining. `?.` and calls/member access bind at
postfix precedence; `??` is right-associative below logical/binary operators and
above assignment.

Optional parameters are deferred. The present parameter/default binder has no
omitted-argument plan, and adding only parameter type sugar would be a partial
feature. Default arguments remain separately unsupported and are not conflated
with absence.

## Option and Result

`Option<T>` means that an operation succeeded and a value may naturally be
absent. `Result<T,E>` means that an operation may fail for a typed reason. A
collection `first` helper can return `Option<T>`; parsing must return Result.
Result propagation and deterministic failure remain orthogonal to static safety.

Future JSON mapping is:

```text
required T: missing/error, null/error, value/T
Option<T>: missing/None, null/None, value/Some(T)
```

A transport domain that distinguishes missing, explicit null, and value needs a
separate explicit `JsonField<T>`-like type. Runtime JSON is not implemented here.

## Function effect summaries

Every ordinary bound function owns a `FunctionEffectSummary` before MIR lowering.
The internal dimensions are:

```text
LocalMutation
ReadsRuntimeState
WritesRuntimeState
IO
HostInterop
Suspension
UnknownCall
```

`LocalMutation` is recorded as a safe effect. Arithmetic, records, enums, Option,
Result operations, matches, deterministic traps, arrays, MutableArray allocation,
local reads/writes, and `freeze()` are StaticSafe when their operands/callees are.
CLR, JavaScript-host, npm/transport, resource, renderer, component-state, remote,
and suspension seams are RuntimeOnly. Indirect or unresolved calls fail closed.

The classifier uses an explicit NativeAOT-friendly bound-tree visitor—no
reflection and no backend rediscovery. It records direct call edges, then applies
a deterministic fixed point. Recursive and mutually recursive components with no
runtime effect remain StaticSafe; a runtime-only callee propagates to every caller.
Project compilation recomputes the summaries over all bound modules so resolved
source imports participate in the same transitive call graph; truly external or
unresolved calls still fail closed.
Summaries retain a function-name call chain ending in a concrete reason, such as:

```text
outer -> middle -> readHost -> CLR member access crosses the language boundary
```

This milestone does not execute ordinary functions statically. A later evaluator
can consume these summaries and add bounded execution plus immutable value
embedding without rediscovering effects.

## Backend and tooling boundary

C# and JavaScript continue using their existing private nominal enum
representations. JavaScript never uses `undefined` for None; C# never exposes
`null` as absence. Nested Option remains representationally distinct. Effect
classification occurs before MIR and is target-independent.

The semantic type name exposed by bound symbols is `Option<T>`, including fields
authored with `?` and results of optional chaining. Existing language-server type
display consumes those semantic names. No formatter-specific rewrite is needed;
the syntax tree preserves `?`, `?.`, and both `?` tokens of `??`.

## Next milestone

Implement bounded ordinary-function static evaluation and post-binding immutable
value embedding. Runtime JSON should then reuse Option plus the existing bounded
TSON/schema infrastructure rather than introduce nullable records.
