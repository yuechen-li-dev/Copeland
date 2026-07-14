# Copeland TS immutable nominal records design (CTS-REC-M0a)

**Status:** accepted product direction and implementation-ready design; documentation only. Immutable nominal records are not implemented by CTS-REC-M0a. The current compiler continues to reject ordinary object literals and non-enum member access.

## Decision

Copeland TS will add a first-class immutable product type:

```text
record = product type
payload enum = sum type
Result = specialized fallible sum type
```

The first declaration form is:

```ts
record Point {
    x: number;
    y: number;
}
```

The one canonical first construction form is a **contextual record literal**:

```ts
const origin: Point = {
    x: 0,
    y: 0,
};
```

This is not a JavaScript object literal with a structural type. The expected `Point` type selects one declared nominal record, and binding produces a dedicated record-construction node. A brace literal without an expected record type is rejected and never creates an anonymous type.

The design deliberately excludes production code, fixtures, and compiler behavior changes from M0a. Until a later CTS-REC implementation milestone lands, the examples in this document are design examples rather than accepted programs.

## Product boundary

| Construct | Identity | Mutability | Runtime status |
| --- | --- | --- | --- |
| Copeland record | Nominal | Immutable | Real generated value |
| Payload enum | Nominal | Immutable | Real generated value |
| Result | Structural `T ! E` | Immutable | Real generated value |
| TS interface/type alias | Structural | Descriptive | Erased |
| TS `Record<K,V>` | Structural dictionary description | Not inherently immutable | Erased |
| JS object | Dynamic runtime object | Mutable by default | Runtime value |

A Copeland record is not sugar for TypeScript `Record<K, V>`, an interface, a type alias, JSON, or a JavaScript property bag. JavaScript objects and generated C# types are private backend realizations. They do not define source identity, construction, mutation, equality, serialization, or interop law.

## Current compiler audit

### Evidence and classification

The audit covered the canonical profile; lexer, parser, syntax nodes, binder, symbols, bound nodes, type equality, lowering, Cope MIR model/validator/writer, both backends, enum/Result runtime representations, evaluation-order tests, language fixtures, corpus harnesses, and topology/dependency validators. Historical searches used current historical documents plus `git log -S`, `git log --follow`, `git blame`, tree listings, and blob reads without rewriting the worktree.

| Finding | Current evidence | Classification | Consequence for records |
| --- | --- | --- | --- |
| Object literal syntax | `ObjectLiteralExpressionSyntax` parses identifier or string keys and `key: expression` entries; `Binder.BindObject` always reports `COPE-TYPE-0011`. | Implemented contract; reusable compiler mechanism | Reuse the bounded property-list mechanics, but bind only against an expected record type. Do not graduate general objects. |
| Member syntax | Postfix `receiver.name` parses. Binding recognizes `Enum.Case`; every other member access reports `COPE-TYPE-0012`. | Implemented contract; reusable compiler mechanism | Add record-instance field resolution without changing enum-case lookup or admitting general properties. |
| Assignment targets | The parser admits names and member accesses on the left of `=`; the binder accepts names only and reports `COPE-BIND-0007` otherwise. | Implemented contract; reusable compiler mechanism | Recognize a bound record field target specifically enough to issue the stable immutable-field diagnostic; never lower a field store. |
| Named declarations | Functions and payload enums have declarations. Enums are predeclared before bodies and duplicate names are rejected. No record/class/interface/type-alias declaration exists. | Reusable compiler mechanism | Add a top-level `record` declaration and predeclare record types before binding field types. |
| Type identity | `EnumTypeSymbol` is a distinct symbol object, but fallback `TypeFacts.AreEquivalent` compares runtime symbol class and `Name`. `MirNamedType` and enum expressions use textual names. | Implemented contract; incompatible with the new direction if reused unchanged | Introduce explicit record type and field identities; do not make name strings the semantic identity. |
| Field declarations | Enum payload fields preserve authored order and reject duplicates. Object properties preserve authored order but have no semantic field model. | Reusable compiler mechanism | Reuse ordered lists and duplicate checks, with dedicated record fields and semicolon grammar. |
| Field initializers | Object property expressions parse; arrays, call arguments, and enum payload arguments retain source order. No aggregate field initializer binds. | Reusable compiler mechanism | Bind record initializers in authored order, then retain a separate declaration-order mapping. |
| Aggregate construction | Arrays, payload-enum cases, and contextual `ok`/`err` construction exist. Calls propagate expected parameter types; variables, returns, arrays, Results, conditionals, matches, and enum payloads propagate contextual types. | Implemented contract and reusable compiler mechanism | Context is sufficient to select nominal record construction in all required first-slice positions. |
| Copy/update | No syntax, bound node, MIR node, or backend operation exists. `with` has been lexed as a keyword since the original `Copeland.Script` lexer, but no parser production consumes it. | Proof-era experiment; unresolved until this design | Add a deliberate postfix/infix `with` production later. The reserved token alone established no language law. |
| Runtime immutability | JavaScript payload enums and Results use frozen null-prototype values, private tokens, frozen payload arrays, and validators. C# payload enums currently lower to synthesized C# records; Result lowers to a dedicated readonly struct. | Implemented backend mechanisms, not universal source law | Reuse private tokens, validation, ordered preludes, and generated-name ownership. Do not inherit C# record equality or JS object identity. |
| Helper ownership | The JavaScript backend emits the shared value runtime only when enums, Results, or typed control flow require it, then owns deterministic private names and validators. The C# backend emits Result/unit/panic helpers only when used. | Reusable compiler mechanism | Record tokens, factories, validators, and copy helpers are compiler-owned, deterministic, private, and emitted only when record use requires them. |
| Evaluation order | `CL-FLOW-002`, MIR tree-order tests, JavaScript `EmittedExpression` preludes, and backend tests establish left-to-right and exactly-once behavior for existing constructs. | Implemented/normative contract and reusable mechanism | Record construction and `with` extend the same law; temporaries are required when canonical field order differs from authored order. |
| Language fixtures | `Language/Valid/**/*.cl-valid.ts` must lower to MIR; `Language/Invalid/**/*.cl-invalid.ts` must fail validation rather than through lexical/parser accident. Corpus directories separately own syntax/bound/MIR/backend snapshots. | Implemented test contract | M0b and later record fixtures follow the same split. M0a adds none. |
| Topology | Cope MIR is the Copeland TS frontend/backend boundary. Validators enforce project ownership and dependency direction; `.cope` is a deterministic projection, not a source or interchange parser. | Implemented contract | Record semantics require dedicated Cope MIR and remain within existing projects; no shared IR or infrastructure extraction is justified. |

### Historical findings

| Historical source | Finding | Classification under this design |
| --- | --- | --- |
| Historical M1 language profile (`4575d9e`) | JavaScript object/prototype semantics were banned and object literals/member access were deferred; classes, interfaces, and generics were deferred. | Implemented contract for current rejection; unresolved record model |
| Historical support matrix (`be8466d`) | Proposed a later Copeland-native typed object model, preferred nominal records/classes for typed object construction, listed immutable “record-like data,” planned `readonly`, interfaces, classes, index signatures, and dictionary-like types, and left structural typing open. | Historical proposal only |
| Cope Test v0 (`668f57a`) | Defined a separate test-dialect syntax target and no aggregate type law. | Proof-era experiment; irrelevant to product record law |
| Early lexer history (`20b857b`, via blame) | Reserved `with` but supplied no parse, bind, MIR, backend, or documentation contract. | Proof-era experiment, not evidence that copy/update was designed |
| CTS-M0c and later enum/Result records | Warn that generated C# records and frozen JavaScript records are representations, not language authority. | Reusable doctrine and backend mechanism |

No historical specification defined an implemented record, struct, object type, interface, class, readonly-field model, structural aggregate, dictionary, or anonymous aggregate for current Copeland TS. The old C#-oriented roadmap is incompatible where it would make CLR record equality, nominal C# interfaces, or C# `readonly` spelling determine Copeland law. General structural object types, mutable property bags, index signatures, and dictionary semantics are incompatible with this first direction. The remaining equality, patterns, serialization, and host-boundary questions are explicitly unresolved rather than inherited.

### Grammar and ambiguity audit

- `record` is currently an ordinary identifier, so M0b must introduce `RecordKeyword` and a top-level declaration production deliberately. A record declaration is unambiguous with function, enum, and global statement starts.
- `{` begins a statement block in statement position and an object-literal syntax node in primary-expression position. Contextual record construction reuses only expression-position braces. It does not make a bare block an aggregate.
- `Point { ... }` has no current postfix production. Adding it would require a new adjacency rule after a name and would diverge from TypeScript-shaped object-literal authoring.
- `Point({ ... })` already parses as a call whose argument is an object-literal syntax node. Treating a type declaration as a callable value would manufacture constructor semantics and collide with ordinary function lookup.
- `with` is already tokenized as `WithKeyword`, but currently cannot start a primary expression or continue a postfix expression. M0b should recognize the intended `source with { ... }` boundary deliberately and issue a stable feature-status diagnostic until implementation. It must not fall through parser recovery.

## Source model

### Construction spelling decision

| Alternative | Assessment | Decision |
| --- | --- | --- |
| `const p: Point = { x: 0, y: 0 };` | Matches TypeScript-shaped authoring and existing expected-type flow. Nominality is selected by context, not shape. | **Canonical first form** |
| `const p = Point { x: 0, y: 0 };` | Makes the nominal type explicit but needs a new adjacency grammar and a non-TypeScript construction form. | Rejected for the first model |
| `const p = Point({ x: 0, y: 0 });` | Looks like a function/constructor call, treats a type as a value, and still needs contextual binding for the brace argument. | Rejected |

A record literal is legal only when its expected type resolves to exactly one `RecordTypeSymbol`. It is never inferred from its field names, never overload-resolved by comparing shapes, and never creates an anonymous structural type. Identical declared fields do not permit conversion:

```ts
record ScreenPoint {
    x: number;
    y: number;
}

record WorldPoint {
    x: number;
    y: number;
}
```

`ScreenPoint` and `WorldPoint` have different `RecordTypeId` values. Neither is assignable to the other.

The initial rule is therefore **context required**, not “a type annotation must appear immediately beside every literal.” These positions provide valid context:

```ts
function origin(): Point {
    return { x: 0, y: 0 };
}

function draw(point: Point): void {
}

draw({ x: 0, y: 0 });

const result: Point ! ParseError = ok({ x: 0, y: 0 });
const wrapped: Envelope = { point: { x: 0, y: 0 } };
const event: Event = Event.Moved({ x: 0, y: 0 });
```

Variables without annotations remain rejected by the existing declaration law, and this is rejected specifically as unresolvable nominal construction wherever no expected record type exists:

```ts
const origin = { x: 0, y: 0 };
```

Expected types already flow through returns, parameters, assignment targets, contextual array elements, Result `ok`/`err` payloads, enum payload arguments, record field initializers, and context-propagating branches. M1 must extend that flow to record literals without general object inference. Record-containing Result types, function signatures, enum payload fields, and other record fields use the same named record type identity.

## Language laws

### Declarations and fields

A record declaration introduces one nominal type and a closed ordered field set.

- Every field has one identifier name and one explicit type.
- Every field is required and has no default.
- Declaration order is preserved as canonical compiler, MIR, and artifact order. It is not source-inspectable in the first slice.
- Duplicate record declarations and duplicate fields are rejected.
- Missing, blank, keyword-only, string-literal, computed, or otherwise invalid field names are rejected through deliberate declaration diagnostics.
- Fields have no setters. `readonly` is neither required nor accepted because immutability is intrinsic.
- Methods, executable constructors, getters, setters, visibility modifiers, index signatures, computed properties, and field initializers in declarations are excluded.
- Forward references to other record declarations may bind after predeclaration, but the complete record-containment graph must be acyclic in the first slice. Direct or indirect recursive record declarations are rejected deliberately. No recursive-type solver is introduced.

All existing type constructors may name a record where their current composition laws permit a named value type: parameters, returns, arrays, Result components, enum payload fields, and other record fields. Containment does not alter the contained type's law.

### Complete construction

A bound record construction identifies one `RecordTypeId` and must initialize every declared field exactly once.

- Missing, extra, and duplicate initializers are rejected independently and stably.
- Each initializer must have the exact declared type under `TypeFacts.AreEquivalent`; there is no structural conversion or coercion.
- String-named properties, computed names, shorthand, methods, and spreads are not record initializers in the first slice.
- Initializer expressions are evaluated exactly once in authored left-to-right order.
- Canonical storage and emission use declaration order.
- If authored order differs from declaration order, lowering first evaluates authored expressions into ordered temporaries, then constructs from those temporaries in declaration order. Backends must not reorder side effects to simplify layout.

For example, `Point { y: second(), x: first() }` is not a supported spelling, but the contextual `{ y: second(), x: first() }` evaluates `second()` before `first()` and stores/emits `x` before `y` only after both values exist.

### Field access

Ordinary access is the only field-read spelling:

```ts
const coordinate: number = point.x;
```

Binding evaluates the receiver expression exactly once, requires a known `RecordTypeSymbol`, resolves the textual field to one stable `RecordFieldId`, and assigns the declared field type to the bound expression. Unknown fields are rejected. Enum type member syntax remains separately resolved as enum-case construction; arbitrary property lookup remains rejected.

`point.x = 10` is always rejected as record-field assignment, even if `point` is held in a `let`. `let` permits rebinding the variable to another complete `Point`; it does not make the value's fields mutable.

### Immutability and containment

Immutability is a source-language law:

- a field slot cannot be reassigned;
- a value cannot gain or lose fields;
- construction creates a complete value;
- `with` creates a distinct value of the same nominal type and never mutates its source;
- no source operation exposes backend object identity or mutation.

This is immutable product structure, not a claim that an outer runtime freeze recursively transforms arbitrary referenced values. Copeland primitives, payload enums, Results, and records retain their own immutable value laws when contained. Existing arrays retain their separately specified current and unresolved laws; placing one in a record prevents replacing the field slot but does not decide future array mutation or require recursive freezing. Unknown host objects, mutable collections, interop, and ownership are deferred. A backend may validate known nested Copeland values, but it must not advertise arbitrary deep immutability or recursively freeze unknown host graphs.

## `with` copy/update

The canonical form is:

```ts
const moved: Point = origin with {
    x: 10,
};
```

The laws are:

- The source expression must have a known record type `R` and is evaluated exactly once before any replacement.
- The result type is exactly the same `R`; `with` cannot change nominal type.
- Unspecified fields are copied from the original value.
- Every specified field must resolve to a declared `RecordFieldId` of `R` and appear at most once.
- Replacement expressions have exactly the declared field types and are evaluated exactly once in authored left-to-right order.
- All replacement expressions observe normal lexical state after the source evaluation. If they refer to the source value, they observe the original immutable value; replacements are not staged mutations and cannot observe earlier partial replacements.
- Final storage/emission is in declaration order after source and replacement temporaries have been captured.
- The source is never mutated, even if no field value changes.
- Empty `with {}` is rejected as a likely accidental no-op.
- Nested updates require explicit nesting, such as `outer with { point: outer.point with { x: 10 } }`.
- Field shorthand, computed names, spreads, deletion, and type-changing updates are excluded.

`with` is a same-type record operation, not general object spread.

## Equality, matching, serialization, and interop

Record `==` and `!=` are rejected in the first slice. Backends must not substitute JavaScript reference identity or synthesized C# record equality. A later equality decision must specify recursive records, payload enums, Results, binary64 NaN and signed zero, strings, recursive values, and future collections before any record equality is accepted. Hashing and ordering are likewise out of scope.

The first slice has no record destructuring or record patterns. A later, separately approved field pattern could appear inside a payload-enum case pattern, but this design does not define such a pattern grammar.

Records are not JSON. JSON serialization, property-name policy, host-object conversion, reflection, and a public JavaScript ABI are separate unresolved decisions. A generated object with familiar field names is not interchangeable with a host object of the same shape.

## Bound and Cope MIR architecture

### Source and bound identities

Predeclaration allocates a `RecordTypeSymbol` for every valid top-level record in source declaration order. Each symbol owns a stable compilation-local `RecordTypeId`; each field receives a `RecordFieldId` composed from its owning type identity and zero-based declaration ordinal. IDs are deterministic for identical source input, not promises that numeric IDs survive declaration edits. Textual names remain display and lookup data, not equality keys.

Duplicate declarations do not receive competing usable identities. Deterministic allocation walks syntax declarations in source order and fields in declaration order, independent of dictionary enumeration. The binder resolves all later references to the predeclared symbol. Type equivalence for records compares identity, never name or shape.

Recommended bound shapes are dedicated equivalents of:

```text
BoundRecordDeclaration
  RecordTypeSymbol
    RecordTypeId
    Name
    OrderedFields

RecordFieldSymbol
  RecordFieldId
  Name
  Type

BoundRecordConstructionExpression
  RecordTypeSymbol
  InitializersInAuthoredOrder(field symbol, expression)

BoundRecordFieldAccessExpression
  Receiver
  RecordFieldSymbol

BoundRecordWithExpression
  Source
  RecordTypeSymbol
  ReplacementsInAuthoredOrder(field symbol, expression)
```

### MIR identities and nodes

MIR receives deterministic IDs explicitly rather than reconstructing identity from source-symbol object references or textual names:

```text
RecordTypeDefinition
  RecordTypeId
  Name
  OrderedFields

RecordFieldDefinition
  RecordFieldId
  Name
  Type

RecordConstructionExpression
  RecordTypeId
  InitializersInAuthoredOrder(RecordFieldId, Expression)

RecordFieldAccessExpression
  Receiver
  RecordFieldId

RecordWithExpression
  Source
  RecordTypeId
  ReplacementsInAuthoredOrder(RecordFieldId, Expression)
```

Use a dedicated `MirRecordType(RecordTypeId, displayName)` wherever a record occurs, including function parameters/returns, Result components, enum payload types, arrays, and record fields. Do not encode it as an undifferentiated `MirNamedType` and later recover meaning from a string. Source IDs and MIR IDs may use different implementation types, but lowering must map them one-to-one deterministically for one compilation.

The construction/replacement lists intentionally retain authored order. The definition owns canonical declaration order. A validator or backend can map by stable field ID without losing effects. The `.cope` writer should print record definitions in source order, fields in declaration order, and construction/replacement entries in authored order, with stable IDs visible where needed to disambiguate. Exact text is an implementation decision for M1 snapshots, not a parser contract.

### MIR validation

The shared MIR validator must reject at least:

- duplicate record type IDs, names, field IDs, or field names;
- field IDs whose owner does not match their record;
- missing, extra, duplicate, or unknown construction initializers;
- initializer/replacement types that differ from the field type;
- unknown record types or fields in access/update nodes;
- receiver/source types that do not match the referenced record;
- a `with` result type different from its source type;
- empty replacement sets if the source law rejects them;
- illegal record equality nodes or generic assignment-to-field encodings;
- unsupported recursive record-definition cycles;
- illegal `void` field values and any other existing invalid value-type composition.

Validation must recursively recognize record types inside arrays, Results, enum payloads, functions, and records. Backend validation may add representation constraints, but source meaning belongs in shared MIR validation. MIR contains no JavaScript objects, C# records, property bags, JSON nodes, reflection metadata, or backend layout choices. Record MIR remains distinct from payload-enum MIR: one is a closed product, the other a tagged sum, and their operations and validation differ.

## Backend recommendations

### Backend boundary

| Language law | MIR responsibility | C# realization | JavaScript realization |
| --- | --- | --- | --- |
| Nominal identity | Stable record type ID | Generated nominal type | Private type token |
| Closed fields | Validated field definitions | Fixed members | Null-prototype fixed properties |
| Immutability | No mutation operation | Get-only initialized members | Frozen object |
| `with` | Same-type copy/update node | New generated instance | New frozen value |
| Equality deferred | Reject unsupported operation | Do not expose synthesized semantics | Do not use object identity |

### C#

Generate one ordinary sealed class per Copeland record, with deterministic mangled names, get-only properties in declaration order, and a compiler-owned constructor or factory that requires every field. `with` evaluates the source and authored replacements into temporaries and calls the same complete constructor for a new instance.

An ordinary sealed class is preferable to a C# `record`: C# records synthesize value equality, hashing, printable state, cloning, and `with` conventions that Copeland has not adopted. A `readonly record struct` additionally makes CLR value copying/layout and default values tempting source-law leaks. Neither should be the universal first representation. Inherited `object` identity on the sealed class remains unreachable because Copeland record equality is rejected and no interop ABI is defined.

The representation uses ordinary NativeAOT-compatible C# with no reflection-dependent activation, dynamic code generation, or runtime member discovery. Constructor accessibility and emitted type accessibility are backend/host-boundary choices, not source visibility law. Backend validation must reject malformed MIR rather than emit partially initialized or mutable types.

### JavaScript

Use one private nominal type token per record declaration and construct a null-prototype object with fixed own properties for the token and all declared fields. Freeze the complete object. A compiler-owned deterministic factory accepts declaration-ordered values, defines only the known properties, validates known nested Copeland values as needed, and returns the frozen value. A per-record validator checks non-null object status, null prototype, frozen state, exact private token, exact own-property set, and declared field value categories.

Two same-shaped records receive different token objects. A host object cannot impersonate a record by copying field names because it cannot obtain the private token and must also satisfy the compiler-owned representation invariants. No token, factory, validator, internal property name, or layout is exported as ABI.

Construction lowering evaluates initializer preludes in authored order, stores temporaries, then calls the factory in declaration order. `with` first evaluates and validates the source into one temporary, then evaluates replacement preludes left-to-right, then calls the factory with each declaration-ordered value selected from a replacement temporary or the original. The old object remains frozen and untouched.

Tokens, factories, validators, and any common record-value helper are emitted only when the emitted program contains record declarations/operations requiring them. Their names use the existing deterministic backend-owned allocation discipline. `Object.freeze`, null prototypes, and property validation are initial enforcement choices; the semantic laws are nominality, closed fields, complete construction, immutability, and same-type copy/update.

## Diagnostics plan

Diagnostic numbers should be allocated with the repository's existing stable family convention during M0b/M1. Use a coherent record family (for example `COPE-RECORD-*`) and keep parser recovery diagnostics separate from language rejection. Required diagnostic contracts are:

| Family | Required condition |
| --- | --- |
| declaration | duplicate record declaration; duplicate/blank/invalid field; missing explicit field type; unsupported recursive declaration |
| construction identity | brace construction without a resolvable expected nominal record type |
| construction completeness | missing field; extra field; duplicate initializer |
| construction type | initializer type differs from declared field type |
| access/mutation | unknown field access; assignment to immutable record field |
| `with` receiver | source is not a record; empty replacement set |
| `with` fields | unknown replacement; duplicate replacement; replacement type mismatch |
| unsupported operation | record equality/inequality before equality is defined |
| feature status | in M0b only, recognized record declaration/construction/access/`with` syntax is rejected as not yet implemented without relying on parser accidents |

Diagnostics should identify the record and field where useful, anchor the offending declaration/name/expression, and avoid promising exact backend representation names.

## Fixture and proof plan

M0a adds no fixtures. Later language-law evidence belongs in the existing filesystem-backed topology:

```text
tests/Copeland/Copeland.TS.Tests/Language/
  Valid/records/*.cl-valid.ts
  Invalid/records/*.cl-invalid.ts
```

Minimal valid contracts should cover declaration/contextual construction, same-shape declarations remaining nominally distinct, reads, nested records, records in function positions, records inside Result success/error composition where current Result laws allow it, records as payload-enum fields, declaration-versus-authored field order, and `with` once implemented.

Minimal invalid contracts should cover every missing/extra/duplicate declaration/initializer/replacement case; uncontextualized construction; cross-record assignment/return/call; field type mismatch; unknown read; field assignment through both `const` and `let`; invalid and empty `with`; recursive declaration; and `==`/`!=` rejection.

Runtime/backend proof companions should use counters or trace accumulation to prove:

- construction initializers run exactly once in authored order even when written out of declaration order;
- a field-access receiver runs exactly once;
- a `with` source runs once before replacements;
- replacements run once left-to-right and observe the original value;
- source and result remain distinct and the source is unchanged;
- C# and Node agree;
- JavaScript rejects malformed internal/host values and same-shaped nominal impersonation.

Parser, bound-tree, `.cope`, C#, and JavaScript snapshot corpus files remain under their existing stage-specific `TestData/Corpus` or backend corpus conventions. Language fixtures must not contain generated artifacts.

## Refined CTS-REC ladder

| Milestone | Scope and convergence condition |
| --- | --- |
| CTS-REC-M0a | This documentation-only audit and accepted language/MIR/backend design. No behavior or fixture change. |
| CTS-REC-M0b | Add `record` keyword/declaration recognition, deliberate contextual-literal and `with` syntax recognition, stable rejection diagnostics, and invalid language-law fixtures that prove intended feature-status validation rather than parser accidents. Do not add valid record fixtures or accept programs through MIR. |
| CTS-REC-M1 | Implement declarations, contextual construction, field access, symbols/bound nodes, stable IDs, dedicated MIR, shared validation, deterministic `.cope`, and frontend fixtures. Both backends must explicitly reject record MIR until their milestones; accepted programs must never be silently miscompiled. The current separate backend corpus/runtime topology makes C# proof substantial enough for M2 rather than an artificial tail of M1. |
| CTS-REC-M2 | Implement the ordinary sealed generated C# representation, complete construction/access, runtime proofs, NativeAOT-safe emission, and deterministic artifacts. |
| CTS-REC-M3 | Implement the private nominal frozen JavaScript representation, validators, corruption/impersonation proofs, demand-driven helpers, Node execution, and C#/Node parity for construction/access. |
| CTS-REC-M4 | Implement dedicated source/bound/MIR `with`, empty-update rejection, exactly-once/order temporaries, new-value semantics, both backends, and parity proofs. Keeping this separate is justified because it adds observable sequencing and copy semantics after record values are proven; it must not be emulated with spread or mutation in earlier milestones. |
| CTS-REC-M5 | Close out doctrine, diagnostics, stress coverage, representation privacy, artifact stability, recursive-declaration rejection, and cross-backend evidence. Equality, hashing, ordering, destructuring/patterns, serialization, and interop require separately approved follow-ups. |

M1 and M2 may be combined only if implementation shows that the compiler facade cannot report an explicit unsupported-backend result without accepting a silently wrong artifact. Temporary JavaScript objects, C# anonymous types, structural type equality, or mutation-based `with` are not acceptable milestone bridges.

## Deferred and unresolved

The following are outside this design beyond establishing the boundary: general structural or anonymous object/record types; TypeScript interfaces and type aliases; classes; methods; inheritance; prototypes; executable constructors; getters/setters; visibility; index signatures; computed properties; object spread; mutable structs; dictionaries/maps; generics; equality/hashing/ordering; record destructuring or patterns; reflection; decorators; JSON; JavaScript interop ABI; recursive type solving; compiler-wide IR unification; and shared source infrastructure.

Record equality, record patterns, serialization, and interop remain unresolved. Recursive records are deliberately unsupported in the first slice. Backend optimization may be considered only after the laws and parity evidence are stable.

## M0a completion boundary

CTS-REC-M0a is complete when this design, the [canonical Copeland TS language profile](copeland-ts-language-profile.md), and the [migration audit](../../migrations/cts-rec-m0a-immutable-records-audit.md) agree on contextual nominal construction, immutable closed fields, same-type `with`, dedicated identities/MIR, private backend representations, and the deferred boundaries. No implementation claim follows from this document alone.
