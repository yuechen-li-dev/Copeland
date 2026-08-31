# CTS-STATIC-META-M1 architecture record

**Status:** meaningful progression (2026-08-27)

## Mental model

```text
static
    runs ordinary deterministic Copeland code during compilation

template
    runs typed compile-time metaprogramming over values, types, and artifacts

generic
    expresses reusable typed abstraction through Copeland's existing closed
    generic-function and template type-parameter machinery

runtime
    receives only the resolved ordinary program
```

`static expression` does **not** mean a runtime static member, global lifetime,
an optimization hint, C# `static`, or linker storage. It is an explicit request
to evaluate an ordinary expression in the compiler's target-independent
post-binding phase.

## Baseline and existing architecture

The work started from clean `main` at `6f64b57`, .NET SDK `10.0.302`. The
required `dotnet test Copeland.slnx --configuration Debug --no-restore` baseline
passed 1,660 tests. `git diff --check` was clean and no unrelated validator debt
was present.

The compiler already owned a syntax-free `BoundTemplate*` plan evaluator for
`ProjectTree` artifacts, `static if`, `static match`, and finite `static for`.
It also already owned normal bound functions, immutable arrays and records,
payload enums/match, structural `Result<T,E>`, compiler-owned nominal
`Option<T>`, explicit `MutableArray<T>`, and transitive `FunctionEffectSummary`
values. Runtime MIR and both backends were template-free. The missing boundary
was ordinary expression evaluation plus immutable value replacement between
effect analysis and MIR.

## Final syntax and eligibility

The source spelling is:

```ts
function makeTable(size: int): int[] {
    const temporary: MutableArray<int> = MutableArray<int>(size);
    let index: int = 0;
    while (index < size) {
        temporary[index] = index * index;
        index = index + 1;
    }
    return temporary.freeze();
}

function answer(): int {
    const table: int[] = static makeTable(5);
    return table[4];
}
```

The parser represents `static` as `StaticExpressionSyntax`; the binder produces
`BoundStaticExpression`. Static eligibility is exactly:

1. every required value is available in the evaluator environment;
2. every reached ordinary function has an existing `StaticSafe` summary;
3. every reached operation is implemented by the bounded evaluator; and
4. the computation and final value remain within deterministic limits.

The evaluator consumes `BoundProgram.FunctionEffects`. It does not walk calls to
rediscover host effects. A runtime-only summary produces `COPE-STATIC-0012` and
includes its existing provenance chain. CLR/JavaScript host calls, packages,
remote calls, IO, runtime state, suspension, and unknown indirect calls remain
forbidden. Local variable and `MutableArray` mutation remains allowed.

## Static semantic and value model

The compiler-owned value algebra is independent of C# object identity:

```text
StaticPrimitiveValue
StaticArrayValue
StaticMutableArrayValue       (temporary only)
StaticRecordValue
StaticEnumValue               (also Option)
StaticResultValue
```

The evaluator supports booleans, `int`, binary64 numbers, strings, immutable
arrays, local `MutableArray` temporaries, records, payload enums, Option, Result,
`if`, enum/Result match, `while`, `for`, `for...of`, locals, ordinary calls,
recursion, checked indexing, `length`, `freeze`, arithmetic, comparison, and
short-circuit boolean operators. Option remains an ordinary synthesized payload
enum. Result remains the existing Result representation. No host null or C#
object identity becomes language state.

Only immutable results embed. Returning a live `MutableArray` is a static
failure; callers must `freeze()` it. Deterministic bounds errors, division by
zero, and Result unwrap errors produce `COPE-STATIC-0014`. Eligibility failures
use `COPE-STATIC-0012`; unsupported evaluator operations use
`COPE-STATIC-0013`; resource exhaustion uses `COPE-STATIC-0015`.

## Determinism, budgets, and cache identity

M1 fixed limits are:

| Resource | Limit |
| --- | ---: |
| evaluation steps | 100,000 |
| call depth | 128 |
| loop iterations | 100,000 |
| temporary/value allocations | 200,000 |
| one array | 65,536 elements |
| final embedded tree | 100,000 values |

Calls are memoized after successful completion by function stable identity and
the structural identity of immutable arguments. Active identical call keys are
tracked, so a recursive same-argument cycle reports a bounded diagnostic rather
than hanging. The cache deliberately excludes mutable-array arguments. The
identity is local and deterministic; it leaves room for a future persistent
cache to add reachable-body, compiler-version, and dependency hashes.

Static execution cannot observe time, randomness, locale, environment,
filesystem enumeration, process identity, scheduling, or backend target. Number
formatting/conversion uses invariant culture.

## Post-static phase and backend boundary

The implemented phase boundary is:

```text
parse
  -> bind + typecheck
  -> function effect fixed point
  -> StaticEvaluationPass
  -> BoundStaticExpression.EvaluatedExpression (ordinary bound value)
  -> MIR lowering
  -> JavaScript / C# / RyuJIT / NativeAOT
```

`StaticEvaluationPass` runs after project-wide effect classification so imported
source calls use the same transitive graph. Direct `MirLowerer` entry points also
enforce the pass as an idempotent phase guard. MIR lowering only unwraps an
already resolved ordinary expression; an unresolved node is an internal phase
error. MIR has no static opcode and no backend evaluates static code.

Primitives, records, enums, Result, and arrays embed through their existing
ordinary bound and MIR aggregate nodes. Current large arrays use generated
literal/aggregate initialization. Specialized blobs, `ReadOnlySpan` data, and
typed JS storage are deliberately deferred; semantics are correct but large
value emission is not yet size-optimized.

Generated-output tests prove that `makeSquares(5)` appears only in the retained
dual-use function declaration, not in the consuming function, while the
consumer contains `0, 1, 4, 9, 16`. C# and Node execute the embedded program and
return `16`. The existing real NativeAOT CLI publish test now evaluates a local
mutable table and Option during compilation, publishes reflection-free generated
C#, and prints the same expected text.

## Typed metaprogramming

Templates remain explicitly compile-time and keep their compiler-bound artifact
plan. They do not become runtime functions or a build-system actuator. TSPack
still owns project realization.

The typed metadata surface is now:

```ts
reflect nameOf<T>()
reflect fieldsOf<T>()
reflect enumCasesOf<T>()
```

`reflect fieldsOf<T>()` returns typed structural entries with `name`, `typeName`,
`optional`, and `readonly`. Optional record fields expose `Option<T>` as their
semantic type. `reflect enumCasesOf<T>()` returns declaration-ordered entries with
`name`, `payloadCount`, and ordered `payloadTypes`. `reflect nameOf<T>()` is the source
display name; it is not promised as a stable semantic identity.

All three operations accept template type parameters. The binder emits a typed
deferred metadata value carrying the parameter ordinal and metadata kind. At
instantiation, the evaluator inspects the supplied compiler-owned `TypeSymbol`
and produces structural values; user code never receives AST nodes or compiler
implementation classes. Invalid reflection reports local `COPE-STATIC-0011`.

Template identity remains `TemplateSymbol.StableIdentity` plus type and static
arguments. Completed identical nested instantiations are memoized. Direct and
indirect recursion retains readable `COPE-TEMPLATE-0004` chains. M1 additionally
bounds depth (64), total instantiations (4,096), metadata iterations (100,000),
and generated artifacts (100,000) with `COPE-TEMPLATE-0016`.

The ordinary evaluator and artifact evaluator now compose through the same
semantic types, metadata contracts, deterministic identity law, and phase
boundary, but their execution objects have not been mechanically collapsed into
one class. Artifact constructors require compiler-owned values that ordinary
runtime code cannot produce. A future extraction can share more primitive
evaluation without weakening that separation.

## Generic and reflection boundary

Copeland already supports the familiar closed generic-function subset and
template type parameters with existing requirement/constraint checking. This
milestone does not add user-defined generic type declarations, type-kind match,
runtime type descriptors, overload SFINAE, partial specialization, or mandatory
monomorphization. Compiler-owned `Option<T>` remains the only general parametric
type family. Backend sharing versus specialization remains a lowering and
optimization decision, not a source semantic law.

There is no `System.Reflection`, `dynamic`, JavaScript property enumeration,
raw AST API, token macro, source-string eval, or runtime metaprogram library in
the generated program. Generated identifiers continue using compiler semantic
identities and existing backend naming.

## Editor and grammar status

The syntax adds one prefix expression and no new angle-bracket grammar. Existing
generic call parsing and TSX lookahead are unchanged, so this slice introduces no
new JSX ambiguity. Static result types are the ordinary bound expression type;
existing hover/type display therefore sees the resolved semantic type, including
`Option<T>`. Full compile-time value visualization and go-to-definition through
generated semantic declarations are not implemented.

## Anti-goals audit

The implementation adds no preprocessor, textual substitution, SFINAE, ADL,
header instantiation model, specialization maze, unrestricted host `constexpr`,
mandatory monomorphization, procedural token macro, trait/lifetime system,
build-script execution, or runtime reflection. It borrows Zig's useful lesson—
ordinary language execution can happen earlier—without copying Zig syntax.

## Dogfood and deviations

Proven in focused tests:

- static table construction with local mutation and freeze;
- static Option/record/match and Result embedding;
- runtime-only effect provenance and distinct deterministic failure diagnostics;
- bounded recursive-cycle diagnostics;
- project-wide imported StaticSafe calls;
- C# and Node result parity and runtime reconstruction elimination;
- NativeAOT publish/run through the real CLI target;
- concrete and type-parameter-driven record metadata;
- declaration-ordered payload-enum metadata;
- identical template-instantiation memoization;
- existing artifact-template focused regression suite.

Not completed in this progression:

- the requested ordinary/static 5x5 convolution benchmark and V8/RyuJIT/
  NativeAOT timing/size report;
- specialized large readonly-data emission;
- a single mechanical evaluator class for both runtime-shaped values and
  artifact constructors;
- general user-defined generic type declarations, type predicates/type-kind
  match, generated semantic declarations, and formatter-specific tests;
- compile-time wall-clock benchmarks for small/large/nested workloads.

The next isolated compiler task is large-value data ownership: introduce a
backend-neutral readonly-data MIR carrier, then emit compact C#/NativeAOT and JS
representations. That foundation makes the convolution experiment meaningful;
without it, the experiment would measure literal reconstruction shape rather
than the intended static-data path. After that, primitive evaluator operations
can be extracted beneath both ordinary static expressions and templates without
allowing ordinary code to construct artifact capabilities.

This is meaningful progression rather than full milestone closeout. The
load-bearing generalized static evaluator and typed type-parameter metadata are
real and backend-proven; the performance qualification and compact-data carrier
remain explicitly bounded follow-up work before primary focus returns fully to
TSPack.
