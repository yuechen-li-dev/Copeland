# Copeland TS direct generic-call inference (CTS-TYPE-M2a)

**Status:** accepted documentation-only architecture and repository audit. CTS-TYPE-M1b remains the implemented contract: generic calls require a complete explicit closed type-argument list. CTS-TYPE-M2a authorizes no compiler, MIR, backend, fixture, corpus, package, or tooling behavior change. It selects the bounded M2b implementation target.

## Decision

M2b may infer a complete ordered closed type-argument list for a named generic function call with no `<...>` list:

```ts
function first<T>(values: T[]): T {
    return values[0];
}

const answer: number = first([42]);
```

Inference is local to that call, uses only value arguments and declared parameter-type patterns, and produces exactly the closed instantiation that `first<number>([42])` would produce. It is not TypeScript-style constraint solving. There is no return-context inference, overload selection, best-common-type calculation, backtracking, or backend participation.

An explicit list remains the deterministic escape hatch and bypasses inference:

```ts
identity<number>(42);
first<number>([]);
```

Calls with a `<...>` list continue to require its exact full arity. M2b must retain the existing `COPE-GENERIC-0007` behavior for a partial list; it must not add partial explicit inference.

## Corrected M1b implementation inventory

The following is the current implementation, not the earlier M0a/M1a design baseline.

| Concern | Current evidence | M2b consequence |
| --- | --- | --- |
| Generic declaration syntax | [`FunctionDeclarationSyntax` and `TypeParameterSyntax`](../../../src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs), [`Parser.ParseFunctionDeclaration`](../../../src/Copeland/Copeland.TS/Syntax/Parser.cs), and [`BinderImpl.PredeclareFunctions`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L1235) | Named functions own ordered parameters; the binder creates `TypeParameterSymbol` entries in declaration order. |
| Explicit generic calls and parser disambiguation | [`GenericCallExpressionSyntax`](../../../src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs#L669), [`Parser.ParsePostfixExpression`](../../../src/Copeland/Copeland.TS/Syntax/Parser.cs#L790), [`Parser.IsGenericCallAhead`](../../../src/Copeland/Copeland.TS/Syntax/Parser.cs#L910), [`Parser.ParseGenericCallExpression`](../../../src/Copeland/Copeland.TS/Syntax/Parser.cs#L928), and [`ParserTests`](../../../tests/Copeland/Copeland.TS.Tests/ParserTests.cs#L29) | `name<Type>(...)` is recognized only after a name and only when the matching `>` is followed by `(`. Ordinary `<`, `>`, and `&&` comparisons retain their existing parse. An inferred call adds no syntax and therefore adds no parser ambiguity. |
| Type parameters and requirements | [`TypeParameterSymbol`](../../../src/Copeland/Copeland.TS/Semantics/Symbols.cs#L61), [`RequirementSet`](../../../src/Copeland/Copeland.TS/Semantics/Symbols.cs#L55), and [`BinderImpl.BindRequirements`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L1523) | Candidate slots are indexed by existing ordered type parameters. Requirements are post-inference validation facts, never candidate sources. |
| Call and argument binding | [`BinderImpl.BindExpression`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L1651), [`BindCall`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L1871), and [`BindGenericCall`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L1955) | Ordinary calls currently reject generic targets with `COPE-GENERIC-0003`. M2b replaces only that branch with a shared inferred-call path; explicit calls remain on their current path. |
| Closed specialization and cache | [`GetOrCreateClosedInstantiation`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L2101), [`SubstituteType`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs), and [`ClosedInstantiationRewriter`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L2283) | Inference must call this exact existing factory after it has a fully closed ordered list. It must never bind or lower a second generic body. |
| Stable identities and names | [`FunctionSymbol.StableIdentity`](../../../src/Copeland/Copeland.TS/Semantics/Symbols.cs#L25), [`ClosedTypeIdentity`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L2168), and [`CreateSpecializationName`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L2153) | The full identity text remains authoritative: function identity plus ordered canonical closed identities. The display/mangled suffix is not semantic identity. |
| Type algebra and equivalence | [`TypeSymbol` families and `TypeFacts.AreEquivalent`](../../../src/Copeland/Copeland.TS/Semantics/Types.cs#L3) | The initial matcher may traverse only `ArrayTypeSymbol`, `ResultTypeSymbol`, and `ColumnTypeSymbol`; canonical nominal records, enums, tables, and rows are atomic. Equivalence is exact. |
| Aliases and provenance | [`TypeAliasSymbol.CanonicalType`](../../../src/Copeland/Copeland.TS/Semantics/Symbols.cs#L67), [`BindType`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L3538), and [`ReportTypeMismatch`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L3805) | Alias arguments infer their canonical type, while diagnostics may retain direct authored alias text such as `UserId (alias of number)`. |
| Contextual arrays, Results, and records | [`BindArray`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L3195), [`BindResultConstructor`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L3175), and [`BindObject`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L3209) | Nonempty arrays are independently typable; empty arrays require an expected array type. Bare `ok`/`err` require an expected Result type. Object literals require one expected nominal record type and rows cannot be constructed this way. |
| Tables and columns | [`TableRowTypeSymbol` and `ColumnTypeSymbol`](../../../src/Copeland/Copeland.TS/Semantics/Types.cs#L93), [`BindIndex`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L3468), and table field access in [`BindMember`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L3428) | A row is an atomic known nominal type and can later satisfy requirements from its existing fields. `column T` is the one additional structural wrapper currently present. |
| Expected types and returns | [`BindReturn`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L1610), variable initialization in [`BindVariableDeclaration`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs), and `IsAssignable`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L3732) | Existing expected-type propagation remains available only after inference has closed the callee. It must not be read as inference evidence. |
| Current diagnostics and bounds | [`GenericDiagnosticInventoryTests`](../../../tests/Copeland/Copeland.TS.Tests/GenericDiagnosticInventoryTests.cs), and constants in [`BinderImpl`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L47) | The current ordinary-call diagnostic is deliberately `COPE-GENERIC-0003`; M2b needs a new bounded inference inventory and must preserve nonempty spans. |
| Closed TSON | [`TsonEncodeFeatureTests`](../../../tests/Copeland/Copeland.TS.Tests/TsonEncodeFeatureTests.cs#L228) | TSON receives only an already concrete result. It is never an inference evidence source. |
| CLI, backends, and topology | [`Copeland CLI`](../../../src/Copeland/Copeland.Cli), [`CSharpBackend`](../../../src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs), [`JavaScriptBackend`](../../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs), [`Validate-CopelandTsTopology.ps1`](../../../tools/Validate-CopelandTsTopology.ps1), and [`Validate-DependencyBoundaries.ps1`](../../../tools/Validate-DependencyBoundaries.ps1) | Both backends consume validated closed MIR and cannot reference frontend symbols. The CLI has no inference mode; no CLI or backend change is authorized. |
| Existing fixtures and parity | [`Language/Valid/generics`](../../../tests/Copeland/Copeland.TS.Tests/Language/Valid/generics), [`Language/Invalid/generics`](../../../tests/Copeland/Copeland.TS.Tests/Language/Invalid/generics), [`GenericBackendParityTests`](../../../tests/Copeland/Copeland.TS.Backend.CSharp.Tests/Runtime/GenericBackendParityTests.cs), and [`CliIntegrationTests`](../../../tests/Copeland/Copeland.Cli.Tests/CliIntegrationTests.cs) | M1b proves explicit closed calls. M2b must extend, rather than replace, this inventory with explicit/inferred reuse evidence. |

## Evidence boundary and matching algebra

For an inferred call, the only evidence is a corresponding declared parameter type and independently bound value-argument type. Process pairs in authored order. Canonicalize aliases before matching and compare candidates with `TypeFacts.AreEquivalent`.

```text
Pattern T            Actual A              -> candidate T = A
Pattern Array<P>     Actual Array<A>       -> match P, A
Pattern Result<P,E>  Actual Result<A,B>    -> match P, A; match E, B
Pattern Column<P>    Actual Column<A>      -> match P, A
Pattern Concrete C   Actual C              -> compatible
Pattern Concrete C   Actual D              -> structural mismatch
```

The current type algebra does not justify any other initial structural family. Records, enums, tables, and table rows are atomic nominal closed types; their fields are not decomposed. This permits `sum(row)` to infer `Samples.Row`, followed by normal requirement checking, without converting or serializing the row. It also means `consume({ x: 1, y: 2 })` cannot infer `Point`: a contextual record has no independently knowable nominal identity.

Repeated evidence must agree exactly and in encounter order:

```ts
same(1, 2);       // T = number
same(1, "two");   // conflict; no common type is searched
```

No union, common base, numeric promotion, structural join, `unknown`, `any`, existential interface, variance, or general equation solving is permitted. A concrete-pattern mismatch is distinct from a repeated-candidate conflict and must say which parameter/argument pair failed.

## Context-dependent argument law

M2b must use a two-phase, non-speculative call binder.

1. Bind only independently typable arguments with no generic expected type, collecting evidence.
2. Require one candidate for each type parameter, canonicalize it, apply existing closed-type and instantiation bounds, and validate requirements.
3. Obtain the existing closed instantiation.
4. Bind deferred contextual arguments once against its closed parameter types, then perform the ordinary count/assignability checks.

The first phase must not retain a provisional bound node for later execution. Binding is compile-time analysis, but a second bind can create duplicate diagnostics or incompatible bound trees. The implementation should classify syntax before binding, retain a single bound result per independent argument, and retain only syntax plus parameter index for deferred arguments. Once a decisive conflict, missing candidate, or limit breach occurs, do not bind deferred arguments; emit the primary diagnostic and recover the whole call as one error expression.

Consequently:

```ts
identity(42);              // infer number
first([42]);               // infer number[]
first([]);                 // no independent element evidence: use first<number>([])
make();                    // no argument evidence: use make<number>()
handle(ok(42));            // bare Result constructor has incomplete evidence: use explicit arguments
consume({ x: 1, y: 2 });   // contextual record lacks nominal evidence: use consume<Point>(...)
```

Existing `BindArray` already independently types nonempty homogeneous literals and rejects an empty literal without context. Existing `BindCall` already rejects bare `ok` and `err` without a Result context. M2b should convert those facts into focused generic-inference diagnostics at the call site rather than first emitting their generic contextual-binding diagnostics. A non-contextual argument that happens to bind to `error` contributes no candidate and should not trigger secondary type mismatches.

## Constraint, identity, TSON, and backend laws

After exact candidate collection, M2b performs, in order: canonicalization; existing closed-depth validation; `Satisfies` for each `RequirementSet`; and `GetOrCreateClosedInstantiation`. A candidate that is primitive or otherwise ineligible must produce the existing requirement-ineligibility diagnostic (`COPE-REQUIREMENT-0005`); missing and mismatched fields must use existing `COPE-REQUIREMENT-0006`/`0007`. These are not generic inference failures.

Thus `sum(point)` infers `Point`, not `Positioned`; an alias to `Point` also becomes canonical `Point`; and `sum(row)` infers the known row type before its fields satisfy the requirement. Extra fields remain irrelevant to satisfaction.

`identity<number>(42)` and `identity(42)` must pass the same ordered canonical argument list to the same cache. They must therefore have one semantic identity, one specialized bound function/MIR function, one generated name, one runtime carrier choice, and one concrete TSON identity. This is an M2b proof requirement, including a concrete record passed to `tsonEncode` through both spellings. Inference adds no TSON plan or variant, and no inference variable, candidate set, requirement, interface, or open call crosses MIR. C# and JavaScript receive only ordinary closed MIR exactly as they do for M1b explicit calls.

## Resource and termination policy

Reuse the M1b limits of 8 type parameters, closed nesting depth 16, 16 instantiations per generic definition, and 128 per compilation. Add these M2b limits, measured per call before specialization:

- at most the declared parameter count (therefore bounded by normal source size) participates; process only `min(argumentCount, parameterCount)` pairs;
- type-pattern traversal depth: 16, identical to closed-type depth;
- structural matching steps: 128 (eight parameters times a conservative 16-node pattern budget);
- candidate evidence entries: 16 per type parameter, retaining the first source and at most 15 additional equal checks;
- conflict details: first conflict only, with first and conflicting argument indices; and
- no global inferred-call cache or cross-call candidate state.

Use an explicit LIFO or FIFO worklist of `(pattern, actual, parameterIndex, argumentIndex)` frames. Push Result components in reverse authored order so processing remains success then error, and stop at the first decisive conflict, mismatch, or limit. This avoids recursion proportional to hostile nesting and makes artifact/diagnostic order deterministic.

## Implementation-ready sketch

```text
BindInferredGenericCall(call, generic):
    reject if current function is generic (retain M1b generic-to-generic/recursion policy)
    require ordinary call arity before inference
    slots = unresolved entries indexed by generic.TypeParameters
    deferred = []
    boundArguments = []

    for each corresponding parameter/argument in authored order:
        if RequiresGenericExpectedType(argument):
            deferred.add(parameterIndex, argument)
            continue
        actual = BindExpression(argument, contextualType: null)
        boundArguments[parameterIndex] = actual
        MatchIteratively(parameter.Type, actual.Type, slots, parameterIndex, argumentIndex)

    report one missing-evidence diagnostic for all unresolved slots, if any
    canonicalArguments = CanonicalizeSlots(slots)
    ValidateClosedTypeDepth(canonicalArguments)
    ValidateRequirements(generic.TypeParameters, canonicalArguments)
    specialization = GetOrCreateClosedInstantiation(generic, canonicalArguments, call.OpenParenToken)

    for each deferred item in authored order:
        boundArguments[item.index] = BindExpression(item.argument,
            specialization.Symbol.Parameters[item.index].Type)

    Apply ordinary argument assignability once
    return BoundCallExpression(specialization.Symbol, boundArguments)
```

`RequiresGenericExpectedType` is a narrow syntactic/semantic classifier for the existing contextual forms, initially empty arrays, object literals, and bare `ok`/`err`. It must not guess from expected returns or try a bind-and-rebind probe. Its error diagnostic must anchor `AnchorToken(argument)` or the call parenthesis, both nonempty source spans.

## Diagnostics and M2b fixtures

M2b should allocate stable frontend diagnostics for: one/multiple missing candidates (with suggested complete `<...>` text when all other names are known); repeated-candidate conflict; concrete structural mismatch; context-required argument; empty-array evidence absence; contextual-record nominal absence; incomplete Result evidence; inference nesting/step/evidence limits; and partial explicit lists. Messages show the type parameter, parameter and argument ordinal, canonical types, and useful direct alias provenance. They process in source order, cap detail, and avoid cascades after the first decisive reason. Requirement failures reuse their current diagnostics.

The future valid matrix adds inferred primitive, alias, multiple-parameter, repeated-consistent, nonempty/nested array, complete independently known Result, column when a source value exists, constrained record/alias/row, multiple requirements, ordinary downstream use, and TSON record cases. Every representative call appears once explicitly and once inferred in the same program, asserting one specialization and identical C#/Node behavior.

The invalid matrix adds missing and return-context-only evidence, constraint-only evidence, repeated conflict, empty array, contextual record, partially known Result, primitive requirement failure, missing/mismatched requirements, concrete mismatch, partial explicit list, each inference limit, generic-to-generic inference, recursive generic inference, and open/illegal candidates. Extend [`GenericDiagnosticInventoryTests`](../../../tests/Copeland/Copeland.TS.Tests/GenericDiagnosticInventoryTests.cs), the `Language/*/generics` fixture trees, [`GenericBackendParityTests`](../../../tests/Copeland/Copeland.TS.Backend.CSharp.Tests/Runtime/GenericBackendParityTests.cs), and CLI artifact checks. The parity assertion is not only equal output: it must prove explicit/inferred reuse, no duplicate emitted function, and identical closed result/TSON identity.

## Specialization-name hash audit

`CreateSpecializationName` forms `displaySuffix + SHA-256(identity)[..16]`; the full canonical identity is the `_closedInstantiations` cache key. `_closedInstantiationNames` detects a reused short name with a different full identity and throws `InvalidOperationException`. Therefore silent name merging is prevented, but the policy is not yet a compiler-grade deterministic collision outcome: it has no source-anchored diagnostic, no collision-safe allocation, and no forced-collision test.

M2b must stabilize this boundary before claiming inferred/explicit artifact equivalence. Preferred rule: preserve the full identity as authority and deterministically extend the hash suffix (then, if necessary, append a deterministic full-hash-derived disambiguator) until unique; test it with an injectable hash seam. A source-anchored deterministic frontend diagnostic is acceptable only if allocation is not chosen. M2a changes no code here.

## Scope, exclusions, and owner decisions

M2b should implement only inferred calls in closed nongeneric bodies/contexts. Keep generic-to-generic calls and generic recursion rejected; inference does not justify solving open equations. Keep separate CTS-UNION, CTS-CALLABLE, CTS-TSXML, CTS-STATIC, CLR interop, generic nominal declarations, overloads, function values/lambdas/capture, `|`, intersections, conditional/mapped/indexed/template types, `keyof`, type-query `typeof`, `infer`, TSON/JSON, CTS-JS-EMIT, and package/version work.

Remaining owner decisions are: whether a Result constructor with both payload component types independently known can be represented without an expected Result under current syntax; the exact diagnostic IDs/text; and whether collision stabilization allocates names or reports a bounded frontend diagnostic. The recommended M2b scope is the shared inferred-call binder, iterative matcher, diagnostics, fixture/parity proof, and specialization-hash stabilization only.
