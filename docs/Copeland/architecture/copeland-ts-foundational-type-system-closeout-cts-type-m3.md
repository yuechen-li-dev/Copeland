# Copeland TS foundational type system closeout (CTS-TYPE-M3)

**Status:** implemented and closed. This is the concise canonical authority for the implemented foundational type system. It consolidates [M0a](../language/copeland-ts-type-system-design-cts-type-m0a.md), [M0b](copeland-ts-transparent-type-aliases-cts-type-m0b.md), [M1a](../language/copeland-ts-interface-requirements-design-cts-type-m1a.md), [M1b](copeland-ts-interface-and-explicit-generics-cts-type-m1b.md), [M2a](../language/copeland-ts-generic-inference-design-cts-type-m2a.md), and [M2b](copeland-ts-bounded-generic-inference-cts-type-m2b.md). Their historical discussion remains useful, but this record resolves any ambiguity about current product law.

## Canonical algebra and lifetime

| Form | Source syntax | Semantic/bound representation | Canonical MIR and runtime |
| --- | --- | --- | --- |
| Primitive value | `number`, `string`, `boolean`, restricted `void` | `PrimitiveTypeSymbol` | Concrete primitive where legal. `void` is only a function return or Result success type. |
| Nominal value | record, payload enum, record table, table row | nominal symbols with declaration/stable identity | Concrete nominal MIR and backend carrier. Record and row identities remain distinct even if their fields match. |
| Structural value | `T[]`, `T ! E`, `column T` | recursive closed type symbols | Concrete structural MIR and backend representation. |
| Alias | `type Name = ExistingType;` | `TypeAliasSymbol` with canonical target and diagnostic provenance | Erased before MIR; no carrier, declaration, or TSON identity. |
| Requirement | `interface I { field: Type; }` | `InterfaceSymbol`, normalized `RequirementSet` | Constraint-only; erased before MIR and never a storage/value type. |
| Type parameter | named function `<T>` | `TypeParameterSymbol` and open-body type | Exists only while the generic body is bound once; never ordinary MIR. |
| Generic definition/candidate state | named generic function and omitted-call evidence | open `FunctionSymbol`, open bound body, local worklist/evidence slots | Frontend-only. A failed inference produces no specialization. |
| Callable | named ordinary function or closed specialization | `FunctionSymbol`, `BoundFunctionDeclaration` | Existing concrete MIR function and backend function. Function signatures are not first-class values or function types. |

`PrimitiveTypeSymbol.Error` and `ErrorNominalTypeSymbol` are diagnostic recovery sentinels, not authored value types, legal generic arguments, TSON identities, or MIR success cases. `null`, `any`, and `unknown` are not product types: `null` and `any` are rejected and `unknown` has no type symbol or syntax law. The proof-era allowance of an undeclared Result error name is recovery-compatible only; it does not create a general authored nominal-type mechanism. Alias provenance exists solely on selected source symbols for diagnostics. Interface declaration identities, type-parameter ordinals, and closed-specialization identities are semantic bookkeeping; inference evidence is local transient state.

## Implemented source law

### Aliases

`type Name = ExistingType;` is a contextual, compilation-unit declaration in one case-sensitive namespace shared with records, payload enums, tables, and aliases. It has no value symbol. Forward references are valid; direct and indirect cycles use iterative dependency/cycle processing and bounded, deterministic diagnostics. Aliases are transparent for assignment, equality eligibility, expected-type propagation, arrays, Results, records, table rows, and TSON identity. Canonical expansion occurs before MIR. Generic aliases, block aliases, and interface aliases are excluded.

The focused alias suite covers direct and indirect cycles, independent cycles, diamond dependencies, long valid forward chains, aliases to records in contextual construction, arrays/Results, namespace collisions, runtime-value misuse, and absence of alias spelling from MIR, C#, Diagnostic JavaScript, Symbolic JavaScript, and TSON identity. An alias program cannot select different semantics by backend.

### Interfaces and requirement sets

`interface I { x: Type; ... }` is contextual, compilation-unit-only, and contains one or more declaration-ordered readable fields. Requirements compare exact canonical field types; candidate records and table rows may have extra fields. Missing or mismatched fields fail in interface/field declaration order. Repeated interfaces fail, alias-equivalent same-name fields merge, and conflicting fields fail deterministically. Requirement lists in diagnostics show at most four fields.

Interfaces are constraint-only (`T extends A & B`). They are not storage values, runtime interfaces, C# interfaces, JavaScript brands, TSON schemas/values, equality/mutation/adapters, or interface composition. A generic body can access only normalized requirement fields: a candidate's undeclared member cannot make the body valid. Record and table-row shape satisfaction never converts, serializes, or unifies their distinct nominal identities. Unused interfaces emit nothing.

### Generic functions

Only named functions may be generic. They support up to eight unconstrained or constrained parameters, multiple parameters, multiple requirement interfaces, and either complete explicit closed arguments or M2b direct inference. The generic body is bound once in its own type-parameter/requirement environment. Requirement access is represented as a dedicated open bound operation and is rewritten during closed substitution to ordinary record/table-row access. Specialization copies and rewrites that bound body; it never reparses or rebinds generic source.

Each successful call has a stable full canonical identity and reuses one cache entry, bound specialized function, MIR function, C# function, Diagnostic JavaScript function, Symbolic JavaScript function, resource accounting entry, and concrete TSON identity. Generic records, aliases, enums, interfaces, tables, defaults, variance, higher-kinded parameters, overloads, generic-to-generic calls, and generic recursion are excluded.

### Inference

An omitted type-argument list invokes local direct-argument inference. Explicit calls remain the M1b path. Matching uses exact canonical candidates and an iterative worklist that decomposes only `Array`, `Result`, and `column`; nominal types are atomic. Authored argument order controls evidence and the first conflict wins. Missing evidence asks for explicit arguments; constraints validate inferred candidates after inference and never create candidates. There is no return/assignment context inference, overload inference, best-common-type search, backtracking, union synthesis, or inference in generic bodies.

Contextual forms use exactly two phases:

```text
bind independently typable arguments once
-> collect candidates and close every type parameter
-> validate requirements
-> bind each deferred contextual argument once with its closed expected type
```

Thus a sole empty array, record literal, or incomplete bare `ok`/`err` is not evidence; the same form after a witness is bound once contextually. Closed Results provide both components. Equivalent repeated evidence succeeds, conflicting evidence fails, and failure creates no partial specialization.

## Limits and deterministic failure

All limits are frontend checks before MIR/backends; diagnostics are backend-independent and use bounded lists. Alias cycle and inference traversal are iterative. Closed-type validation is bounded; the implementation does not accept hostile unbounded type nesting.

| Limit | Value |
| --- | ---: |
| Type parameters per generic function | 8 |
| Requirement interfaces per type parameter | 8 |
| Normalized requirement fields per parameter | 32 |
| Interface fields per compilation | 128 |
| Closed-type nesting | 16 |
| Closed instantiations per generic definition | 16 |
| Closed instantiations per compilation | 128 |
| Requirement fields shown in a diagnostic | 4 |
| Inference structural depth per call | 16 |
| Inference structural matching steps per call | 128 |
| Evidence entries per type parameter | 16 |

The binder and diagnostic inventory test the generic/interface/inference limits and the inference depth/evidence boundaries; M2b supplies the step boundary and first-conflict evidence. No limit failure yields a `MirCompilation`.

## Identity, rendering, and erasure

The specialization cache key is the full canonical semantic identity: function stable identity plus recursively canonical closed argument identity. It is authoritative and independent of source location, declaration insertion, call order, backend, and JavaScript profile. Explicit and inferred `identity<number>(42)` / `identity(42)` therefore share the exact specialization. Focused tests exercise either discovery order and unrelated declaration insertion.

Rendered specialization names are deliberately secondary:

```text
full canonical semantic identity
-> SHA-256 digest
-> 16, 24, 32, or full hexadecimal suffix
-> escaped-identity fallback
```

Short suffixes are presentation only. Names never merge semantic identities; valid source never fails because a shortened digest collides. Allocation sorts identities and the forced-collision suite covers 16-to-24, 24-to-32, full-digest, escaped-identity fallback, and discovery-order independence. Diagnostic rendering remains readable ASCII plus hexadecimal suffix. A future Symbolic Chinese-prefix/Hangul-radix or Release opaque rendering is only a seam, not current CTS-JS-EMIT behavior.

Canonical MIR contains closed ordinary functions, types, and operations only. It contains no alias, interface, requirement-set, inference-variable/candidate, open type parameter/function/call, or requirement-field-access operation. Backend projects consume validated MIR rather than frontend source interfaces; topology and dependency-boundary validation enforce that boundary. Unused aliases, interfaces, and generic definitions have no runtime declaration or helper.

## Parser, TSON, and parity evidence

The parser distinguishes generic declaration lists and explicit generic calls from `<`, `>`, `<=`, `>=`, comparisons, and `&&`; inferred calls have no angle brackets. Constraint `&` is accepted only in the constraint grammar. Focused parser/alias/generic fixtures cover nested Array/Result types and malformed aliases/generic forms without comparison cascades or whitespace-sensitive grammar.

TSON receives concrete canonical nominal types only. An alias of a record, a constrained generic record, and explicit/inferred calls retain the same concrete record identity. Interface/type-parameter/generic-instantiation names cannot enter canonical TSON, and open types cannot reach an encoding plan. Table rows remain invalid standalone roots while columnar table serialization is unchanged. M3 adds no TSON type, value, plan, or helper.

`GenericBackendParityTests.Closed_generic_matrix_has_csharp_node_parity_and_reuses_specializations` pins the repeated exact trace `42|value|7|42|11|named|alias|row|105|42|3` for C#, Diagnostic JavaScript, and Symbolic JavaScript. It covers aliases, explicit/inferred and multiple-parameter generics, records/extra fields, multiple requirements, rows, requirement access, arrays, Results, contextual staging, control flow, and reuse. Node is recorded by the validation environment; the test executes the Diagnostic profile twice and verifies the Symbolic profile against the same trace.

## Diagnostic inventory and fixtures

The production foundational inventory is owned by `TypeAliasDiagnosticInventoryTests` and `GenericDiagnosticInventoryTests`, supplemented by parser and language-fixture theories. It covers `COPE-ALIAS-*`, `COPE-INTERFACE-*`, `COPE-REQUIREMENT-*`, `COPE-GENERIC-*`, and `COPE-INFER-*` production diagnostics, plus `COPE-TYPE-0020` for illegal erased/`void` storage. Every listed case asserts a nonempty source span. Numeric slots intentionally absent from the inventory are reserved/non-production slots, not silently filled for sequence aesthetics. Declaration order, first conflict, and four-field truncation define stable precedence and bounded output.

At closeout, the filesystem language corpus has 43 valid and 101 invalid fixtures; 19 are under the `types` subsets and 30 under `generics`. The retained inferred-reuse corpus pins canonical MIR, C#, Diagnostic JavaScript, and Symbolic JavaScript by exact bytes and SHA-256 in their owning corpus tests. No M3 production change regenerated an artifact.

## Exclusions and routing

The following remain outside this foundation: partial explicit inference; return-context/overload/best-common-type inference; backtracking/global solving; generic recursion and generic-to-generic calls; generic aliases/nominal types/interfaces; interface values/runtime/interfaces methods/composition/`implements`; variance/defaults/higher-kinded parameters; `any`, unchecked assertions, general structural runtime objects; conditional/mapped/indexed/template-literal types, `keyof`, type-query `typeof`, and `infer`; declaration merging/ambient unchecked declarations; classes/inheritance; first-class function types/lambdas/implicit closures/explicit capture; static evaluation; and CLR metadata import.

Independent union work is now tracked by [CTS-UNION-M0b](copeland-ts-nominal-union-sugar-cts-union-m0b.md): declaration-only `type Name = Record | Record` canonicalizes to payload enums without broadening `TypeSyntax`. [CTS-CALL-M0a](../language/copeland-ts-callables-and-explicit-capture-design-cts-call-m0a.md) now owns function types, noncapturing lambdas, first-class callables, and explicit capture. [CTS-CLASS-M0a](../language/copeland-ts-pure-classes-design-cts-class-m0a.md) proposes an atomic nominal class-owned record type, erased field-only interface satisfaction through public fields, and existing closed generic-function integration; generic classes remain separately deferred. `CTS-TSXML` owns TS-XML; `CTS-STATIC` owns bounded compile-time execution; CLR/.NET interop owns metadata import. Generic nominal types require separate evidence and approval.

## M3 requirement ledger

| Status | Material requirement | Evidence |
| --- | --- | --- |
| Satisfied | Canonical algebra and recovery-type distinction | `Types.cs`, `Symbols.cs`, M3 algebra table, binder diagnostics. |
| Satisfied | Transparent alias law and iterative cycles | `TypeAliasTests`, `TypeAliasDiagnosticInventoryTests`, language fixtures. |
| Satisfied | Erased interface/requirement law | generic diagnostic inventory, open-body and closed-rewrite binder tests, language fixtures. |
| Satisfied | Closed generic specialization law | `BinderTests`, corpus artifacts, C#/Node parity matrix. |
| Satisfied | Bounded direct inference and contextual staging | M2b fixtures, binder boundaries, generic diagnostic inventory. |
| Stronger evidence | Explicit/inferred reuse and collision naming | M3 call-order and all-stage collision tests plus retained corpus hashes. |
| Satisfied | MIR/backend erasure and dependency boundary | concrete MIR assertions, backend assertions, topology/dependency scripts. |
| Satisfied | TSON concrete identity | alias and generic record TSON tests and corpus tests. |
| Satisfied | Parser/resource/diagnostic closeout | parser tests, inventories, fixture theories, exact bound table. |
| Accepted-scope exclusion | Deferred TypeScript and callable/static/interop families | exclusions and routing above. |

Missing rows: **0**. CTS-TYPE is honestly closed. The recommended next independent ladder is `CTS-UNION`; M3 does not begin it.
