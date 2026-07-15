# Copeland TS user-authored type-system design (CTS-TYPE-M0a)

**Status:** accepted architecture recommendation. [CTS-TYPE-M0b](../architecture/copeland-ts-transparent-type-aliases-cts-type-m0b.md) implements transparent non-generic compilation-unit aliases. [CTS-TYPE-M1a](copeland-ts-interface-requirements-design-cts-type-m1a.md) supplies requirement architecture and [CTS-TYPE-M1b](../architecture/copeland-ts-interface-and-explicit-generics-cts-type-m1b.md) implements its bounded interface plus explicit closed-generic slice. Static evaluation remains unimplemented.

## Decision

Copeland will grow a bounded, TypeScript-shaped user-authored type system around this separation:

```text
type       = erased compile-time description or transparent name
record     = nominal immutable runtime data
interface  = erased structural requirement set
generic    = parameterized definition checked against requirements
static ... = bounded compile-time execution in a later, separate ladder
```

Conceptually:

```text
interface Positioned
    ~= Requires(
        readable field x: number,
        readable field y: number
      )

T extends Positioned
    ~= Requires(T, Positioned)
```

This notation explains the model; it is not source syntax or a request for a runtime `Requires` value. Ordinary aliases, field-only interfaces, and bounded generic functions should look familiar to a TypeScript programmer. Type-level programs, ambient merging, runtime interface carriers, and C++-template-style execution are not part of the model.

M0a changed documentation only. M0b implements the non-generic `type` alias examples; M1b implements field-only interfaces, explicit generic function declarations, and explicit closed calls. General type-level programming remains proposed syntax and is rejected.

## Evidence and status vocabulary

This record uses six classifications:

| Classification | Meaning |
| --- | --- |
| Implemented law | Current production behavior supported by the canonical profile and executable evidence. |
| Implemented proof-era behavior | Current mechanism or recovery behavior that exists, but is not automatically product doctrine. |
| Historical proposal | Earlier planning evidence that has no present authority by itself. |
| Current rejection | A form rejected by current parsing, binding, validation, or profile law. |
| M0a recommendation | The bounded direction selected here for a later milestone. |
| Explicitly unresolved | A decision that still requires owner evidence or approval before implementation. |

Backend output is evidence, not source-language authority. In particular, a C# declaration or JavaScript carrier does not decide Copeland identity, assignability, reflection, layout, or generic representation.

## M0a baseline implementation inventory

This inventory records the repository audited by M0a. CTS-TYPE-M0b supersedes only its alias-specific findings; the linked M0b architecture record is current implementation law.

### Syntax and tokens

| Surface | Exact production evidence | Finding and classification |
| --- | --- | --- |
| Lexer | [`Lexer.NextToken`](../../../src/Copeland/Copeland.TS/Syntax/Lexer.cs) and `LexIdentifierOrKeyword` | Identifiers are classified through `SyntaxFacts`. `<`, `<=`, `>`, and `>=` are implemented operator tokens. There is no angle-bracket/type-argument lexical mode. **Implemented law.** |
| Keyword inventory | [`SyntaxKind`](../../../src/Copeland/Copeland.TS/Syntax/SyntaxKind.cs), [`SyntaxFacts.KeywordKinds`](../../../src/Copeland/Copeland.TS/Syntax/SyntaxFacts.cs) | `type`, `interface`, `extends`, and `implements` have no keyword kinds and currently lex as `IdentifierToken`. `column` is reserved; table asset `from` is deliberately contextual. **Implemented law/current rejection.** |
| Declaration parsing | [`Parser.ParseMember`](../../../src/Copeland/Copeland.TS/Syntax/Parser.cs) | At the M0a audit baseline, top-level declarations were functions, enums, records, and `record table`. M0b adds contextual compilation-unit aliases; interface, class, and generic declaration productions remain absent. **M0a baseline, superseded for aliases by M0b.** |
| Type grammar | `Parser.ParseTypeSyntax`, `ParsePostfixTypeSyntax`, `ParseIdentifierOrQualifiedRowType` | Result `!` is right-recursive; postfix `[]` is supported; parentheses group types; `Table.Row` and prefix `column T` are dedicated forms. `<` and `>` are never consumed in a type. **Implemented law.** |
| Type syntax hierarchy | [`TypeSyntax`](../../../src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs) | The complete current set is `PredefinedTypeSyntax`, `IdentifierTypeSyntax`, `ArrayTypeSyntax`, `ParenthesizedTypeSyntax`, `ResultTypeSyntax`, `QualifiedRowTypeSyntax`, and `ColumnTypeSyntax`. There is no function-type, alias-reference-specific, interface, type-parameter, union, intersection, indexed-access, conditional, mapped, or type-query node. **Implemented law.** |
| Type positions | `FunctionDeclarationSyntax`, `ParameterSyntax`, `RecordFieldSyntax`, `EnumPayloadFieldSyntax`, `VariableDeclarationStatementSyntax`, `TableColumnSyntax` | Types occur in named function signatures, variables, record fields, payload fields, and explicit table columns. Functions themselves are not first-class type expressions. **Implemented law.** |

The existing comparison tokens can be reused by a future generic parser, but their existence does not establish generic syntax. CTS-TYPE-M2a must specify lookahead and recovery for declarations and calls such as `f<T>(x)` without weakening comparison parsing.

### Semantic types, symbols, and identity

[`Types.cs`](../../../src/Copeland/Copeland.TS/Semantics/Types.cs) contains the complete current `TypeSymbol` family:

| Semantic form | Identity/equality today | Status |
| --- | --- | --- |
| `PrimitiveTypeSymbol` | Interned singletons for `number`, `string`, `boolean`, `void`, and compiler recovery `error`. | Implemented; `error` is not authorable. |
| `ArrayTypeSymbol` | Structural, recursively equivalent by element type. | Implemented law. |
| `ResultTypeSymbol` | Structural, recursively equivalent by success and error type. | Implemented law. |
| `ErrorNominalTypeSymbol` | Same runtime symbol class and same name compare equivalent. It allows an otherwise undeclared identifier in the error side of `T ! E`. | Implemented proof-era behavior, not a general named-type declaration mechanism. |
| `EnumTypeSymbol` | Nominal declaration object in binding, with authored cases/payloads and optional TSON stable identity; fallback equality is symbol class plus name. | Implemented nominal law, with name-based implementation debt isolated by unique declarations. |
| `RecordTypeSymbol` | Explicit `RecordTypeId`; two nonidentical symbols are never equivalent. Fields have `RecordFieldId`. | Implemented nominal law. |
| `TableTypeSymbol` | Explicit `TableTypeId`; owns ordered columns, one nominal `TableRowTypeSymbol`, and the authored singleton. | Implemented nominal law. |
| `TableRowTypeSymbol` | Nominal by owning table/symbol; fields derive from columns. | Implemented nominal law. |
| `ColumnTypeSymbol` | Structural by element type. | Implemented law. |

[`Symbols.cs`](../../../src/Copeland/Copeland.TS/Semantics/Symbols.cs) separately defines variables, parameters, named function signatures, enum cases/payload fields, record fields, table columns, and table-row fields. `FunctionSymbol` carries ordered parameters and a return type but is not a `TypeSymbol`; there are no function values, overload sets, type parameters, aliases, or interface symbols.

`TypeFacts.AreEquivalent` is the sole general equivalence relation. [`Binder.IsAssignable`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs) currently adds only error recovery to exact equivalence. There is no subtyping, widening, variance, interface satisfaction, conversion, overload resolution, or assertion rule. This exactness is the compatibility baseline that aliases must preserve and interface requirements must extend deliberately.

### Binder scopes, lookup, and compatibility

`Binder.Scope` is one case-sensitive `Dictionary<string, Symbol>` with lexical parent lookup. The global scope contains named functions and compiler-created `VariableSymbol` entries for enums, records, tables, and table singletons. Local block scopes contain parameters and variables. There is no separate type namespace, import scope, declaration merging, or member scope.

The binding order in `BinderImpl.Bind` is schema metadata, compiler-owned errors, record/table/enum predeclaration, function predeclaration, declaration bodies, cycle validation, and executable bodies. Consequently, current named records, tables, enums, and functions support same-unit forward references after predeclaration. Duplicate declarations are rejected by the shared global scope plus family-specific dictionaries.

Expected types already flow through `BindExpression` into:

- variable initializers and assignments;
- return expressions;
- call arguments and enum payload arguments;
- record fields and `with` replacements;
- array elements;
- conditional and match arms;
- Result `ok`/`err` payloads; and
- TSON asset/encoding sites.

This is reusable evidence for aliases and generic calls. It is not evidence for structural object inference: `Binder.BindObject` requires exactly one expected `RecordTypeSymbol`, rejects `TableRowTypeSymbol`, rejects extra or missing record fields, and produces a dedicated nominal construction.

Assignment, argument, field-initializer, and ordinary return compatibility use `IsAssignable`, which is exact equivalence after recovery. Result-return binding additionally wraps a compatible success value in `ok`; a bare return from `void ! E` similarly produces `ok(unit)`. Wrong record and table-row identities receive family-specific diagnostics. Named calls require exact arity and exact parameter compatibility.

Primitive `==`/`!=` is supported only for same-type number, string, or Boolean operands. Strict spellings are source-profile rejections. Record, table, row, and column equality is rejected; Result, array, enum, interface, and generic equality is not established. Interfaces therefore must not silently manufacture equality.

`null` is tokenized and parsed as both a literal and predefined type, then rejected by `COPE-PROFILE-0005`. `void` is a real return/Result-success type but is not allowed as an ordinary JavaScript backend value position. `unknown` has no token or symbol and resolves as an unknown named type. `any` and `undefined` are likewise rejected through current profile/unknown-name paths. `PrimitiveTypeSymbol.Error` and `BoundErrorExpression` are diagnostic recovery only.

### Records, enums, tables, arrays, and Results

- Records use `RecordTypeSymbol`/`RecordFieldSymbol`, complete contextual construction, resolved field reads, and immutable `with`. Recursive record containment is rejected. Identical fields do not imply assignability.
- Payload enums use `EnumTypeSymbol`, ordered `EnumCaseSymbol` entries, ordered `EnumPayloadFieldSymbol` entries, nominal construction, and exhaustive match.
- Tables use `TableTypeSymbol`, `TableColumnSymbol`, nominal `TableRowTypeSymbol`, structural `ColumnTypeSymbol`, and closed `BoundTableConstant` data. Rows are immutable table-owned views, not record values or anonymous objects.
- Arrays are homogeneous structural values. Empty arrays require context; mixed element types are rejected.
- Results are structural `ResultTypeSymbol` values with explicit construction, matching, propagation, unwrap, and lexical handling. They are not arbitrary union types.

These families are the concrete candidates that can satisfy field requirements. Satisfaction never changes their identity or representation.

### Bound model, canonical MIR, and validation

[`BoundNodes.cs`](../../../src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs) carries semantic `TypeSymbol` instances on expressions, variables, functions, records, enums, tables, constants, TSON plans, and resolved operations. There are no alias, interface, requirement, type-parameter, generic-definition, generic-call, or open-instantiation bound nodes.

The exact aggregate-definition side is `BoundRecordDeclaration`, `BoundTableDefinition`, `BoundTableColumnDefinition`, and closed `BoundTableConstant` variants `BoundTableLiteralConstant`, `BoundTableArrayConstant`, `BoundTableRecordConstant`, `BoundTableEnumConstant`, and `BoundTableResultConstant`. The MIR parallels are `MirRecordDefinition`, `MirTableDefinition`, `MirTableColumnDefinition`, and literal/array/record/enum/Result `MirTableConstant` variants. Table rows are projected runtime views described by `TableRowTypeSymbol`/`MirTableRowType`, not separately authored constants or record definitions.

[`MirNodes.cs`](../../../src/Copeland/Copeland.TS.Mir/MirNodes.cs) defines:

```text
MirNamedType
MirRecordType
MirTableType
MirTableRowType
MirColumnType
MirArrayType
MirResultType
```

Payload enums and proof-era named error types currently use `MirNamedType`; functions are `MirFunction` definitions with typed parameters and return types, not `MirType` values. `MirLowerer.ToMirType` maps semantic structured/nominal forms directly and otherwise creates a named type. Deterministic numeric record/table IDs are compiler-local identities; optional TSON stable identities are a separate schema concern.

[`MirValidator`](../../../src/Copeland/Copeland.TS.Mir/MirValidator.cs) validates arrays, nominal record definitions/references/construction/access, table definitions/constants/identities/access, TSON plans, structured control flow, and Result propagation. Both backends invoke it before their own program/carrier validation. It does not know aliases, interfaces, type parameters, or generics.

The topology validator requires `Copeland.TS.Mir` to be BCL-only with no project/package dependency; frontend and each backend may depend only on MIR, while backends may not reference the frontend. Compiler-host TSON nodes and asset abstractions are prohibited from MIR/backends. These are **implemented topology laws** relevant to M2: generic semantics reaching a backend must cross validated backend-neutral MIR, and backend representation policy must not leak into frontend law.

### Backend realization

[`CSharpBackend.MapType`](../../../src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs) maps primitives, arrays, structural Result, nominal records/tables/rows, structural columns, and other validated named types. It emits ordinary sealed record carrier classes with get-only members, abstract/sealed payload-enum records, private table/row/column classes and arrays, and a generated Result carrier. C# records, classes, arrays, and generic helper types are private proof-backend choices.

[`JavaScriptBackend`](../../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs) erases ordinary source annotations but does not erase runtime identity required by current law. Its validation accepts the closed MIR value-type family and rejects unsupported named types. It emits private tokens, provenance sets, constructors, validators, frozen record/enum/Result carriers, and table/row/column carriers. `ValidateValueType`, call validation, and carrier validators prevent JavaScript erasure from weakening MIR types.

An erased alias or interface must emit none of those carriers merely by being declared. A satisfying record, enum, table, row, array, or Result keeps its existing carrier law. Generic implementation may erase, share, specialize, or reify private evidence only when observably equivalent.

### TSON semantic and runtime boundary

[`TsonSchema.cs`](../../../src/Copeland/Copeland.TS/Tson/TsonSchema.cs) defines `TsonTypeKind` Boolean, Number, String, Object, Record, Enum, Array, and Table; `TsonTypeReference`; array schema; nominal record/enum/table definitions; table/column identities; and `TsonCatalog`. [`TsonValues.cs`](../../../src/Copeland/Copeland.TS/Tson/TsonValues.cs) defines Boolean, number, string, array, structural document object, nominal record, nominal enum, and nominal table values. [`TsonDocument.cs`](../../../src/Copeland/Copeland.TS/Tson/TsonDocument.cs) owns bounded profiles, limits, diagnostics, and documents.

Compiler binding permits concrete same-schema nominal record, payload-enum, and authored table roots for the applicable asset/encoding paths; arrays can occur where the closed TSON algebra permits them. MIR encoding plans contain primitive/record/enum/array/table plan nodes, never compiler-host `TsonValue` nodes.

Aliases, interfaces, open type parameters, and open generics are not new TSON value variants. TSON rules for the future type system are:

- a record referenced through an alias serializes with the record's existing nominal identity;
- an interface alone cannot establish canonical TSON nominal identity or field order;
- assets continue to target concrete nominal schemas;
- a generic value must be closed to one concrete eligible type before encoding; and
- no alias, interface, requirement, type parameter, or open generic node may appear in canonical TSON or a canonical TSON encoding plan.

### Fixtures, corpus, and historical evidence

The current language-law tree contains 31 valid and 83 invalid fixtures: M0b adds valid and invalid alias coverage alongside arrays, conditions, control flow, declarations, equality, fallibility, functions, records, tables, and tagged data. No fixture accepts interfaces or generics. `TestData/Corpus`, `MirCorpusTests`, focused record/table/TSON suites, C# corpus/runtime tests, JavaScript Diagnostic/Symbolic corpora, and Node runtime tests separately cover syntax-to-artifact and runtime behavior.

Historical evidence is deliberately subordinate:

| Source | Historical claim | M0a reconciliation |
| --- | --- | --- |
| [`architecture/copeland-typescript-support.md`](../architecture/copeland-typescript-support.md) | Planned aliases, nominal/direct-C# interfaces, classes, restricted generics, modules, async, and CLR interop; proposed `interface -> C# interface`. | **Historical proposal.** Direct C# interface/generic lowering cannot define backend-independent Copeland semantics. |
| [`architecture/language-profile.md`](../architecture/language-profile.md) | Deferred classes, interfaces, generics, unions, and JavaScript object/prototype semantics. | **Historical boundary evidence**, not a design for the deferred forms. |
| [CTS-REC-M0a](copeland-ts-immutable-records-design-cts-rec-m0a.md) | Distinguished runtime nominal records from erased interface/type-alias descriptions. | **Accepted compatible doctrine.** |
| [CTS-TSON-M2a](copeland-ts-runtime-tson-encoding-design-cts-tson-m2a.md) | Deferred aliases/interfaces to schema algebra and rejected runtime structural objects. | **Accepted compatible boundary**, superseded here only where M0a now gives type-authoring recommendations. |
| Earlier generated C# records/interfaces/classes discussion | Used target-language forms as convenient proof representations. | **Implemented proof-era behavior or historical proposal**, never source identity law. |

No current or historical authoritative document supplies implemented traits, concepts, higher-kinded types, arbitrary compile-time templates, declaration merging, or CLR metadata import law.

## Canonical semantic algebra

The current and recommended bounded algebra is:

```text
CanonicalValueType =
    Primitive(number | string | boolean | void)
    | NominalEnum(EnumId)
    | NominalRecord(RecordTypeId)
    | NominalTable(TableTypeId)
    | NominalTableRow(TableTypeId)
    | Column<CanonicalValueType>
    | Array<CanonicalValueType>
    | Result<CanonicalValueType, CanonicalValueType>
    | TypeParameter<RequirementSet>       // bound checking only until closed

ErasedDescription =
    Alias<Name, CanonicalOrRequirementDescription>
    | Interface<Name, RequirementSet>

CallableSignature =
    Function<Parameters, Return>          // declaration/signature, not a value type today
```

`Primitive(error)` and name-only undeclared Result error symbols are recovery/proof mechanisms and are excluded from the authored algebra. Table columns are included because they are current value types; table columns are not arrays. Payload fields, record fields, table columns, and row fields are members carrying types, not independent type forms. TSON `Object` is a document semantic form, not a Copeland runtime value type.

Aliases disappear by canonicalization. Interfaces and requirement sets disappear after satisfaction checking. Open type parameters disappear only when a generic use is closed. The initial M2 recommendation lowers closed generic instantiations to existing canonical MIR types and functions, so canonical MIR receives no alias/interface/open-generic type. If M2a finds that a shared open generic MIR is materially required, that is an owner-approved MIR extension, not an assumption in M0a.

## Transparent alias laws

Recommended source:

```ts
type UserId = number;
type Users = User[];
type ParseResult = User ! ParseError;
```

Aliases:

- exist only at compile time and introduce no runtime identity;
- are transitively transparent for assignability, equality eligibility, requirement satisfaction, construction context, and TSON eligibility;
- may name any otherwise approved value-type expression;
- cannot be constructed, matched, reflected, or emitted merely by existing;
- do not emit C# or JavaScript declarations merely by existing;
- cannot contain direct or transitive expansion cycles; and
- cannot introduce conditional, mapped, indexed-access, template-literal, type-query, or other evaluation forms absent from the bounded algebra.

Contrast:

```ts
type UserId = number;

record StoredUserId {
    value: number;
}
```

`UserId` is interchangeable with `number`. `StoredUserId` is one nominal runtime record with construction, identity, field, carrier, and TSON laws.

### Alias decisions

| Question | M0a recommendation |
| --- | --- |
| Scope | M0b permits compilation-unit aliases only. Copeland has no module system; block aliases wait for a demonstrated use and a separate shadowing law. |
| Forward references | Permit same-unit forward references by predeclaring all alias names, matching current named-type declarations. |
| Namespace and duplicates | Use one declaration type-name namespace for aliases, records, enums, tables, interfaces, and later type parameters in their lexical owner. Reject duplicate or colliding type declarations; do not merge. The implementation may stop installing type declarations as value-shaped `VariableSymbol` entries, but that refactor is not required by M0a. |
| Cycles | Reject every direct/transitive alias expansion cycle with a stable diagnostic naming the alias and a deterministic declaration-order cycle path. No lazy recursive alias. |
| Generic aliases | Deferred from M0b and M2 generic-function slices. They require their own termination and inference evidence. |
| Aliases naming interfaces | Once interfaces exist, permit a transparent alias of a requirement set only in positions where that interface is legal. It does not make the interface a storage type. M0b cannot exercise this rule. |
| Diagnostics | Preserve the authored alias at the primary source site and show the canonical target when useful, for example `UserId (alias of number)`. Avoid printing arbitrarily long expansion chains; cycle diagnostics show one bounded path. |
| MIR text | Emit only the expanded canonical type. `.cope` remains deterministic and does not gain alias declarations. |
| Provenance | Bound/compiler diagnostics may retain alias-use provenance as nonsemantic metadata. Canonical MIR receives no alias node in M0b. |

## Interfaces as requirement sets

Recommended declaration:

```ts
interface Positioned {
    x: number;
    y: number;
}
```

An interface is an erased structural set of readable-member requirements. It has no constructor, storage, runtime identity, carrier, mutation authority, declaration merging, or inheritance graph. Satisfaction is implicit and structural at a checked use. It does not change a record/table-row representation and does not imply a C# interface, JavaScript brand, vtable, wrapper, adapter, or runtime cast.

### First-slice requirement law

- Each requirement is one uniquely named readable field with one canonical type.
- A candidate satisfies the field when it exposes a statically resolved immutable/readable field of the same canonical type after alias expansion.
- Extra fields are allowed and ignored for satisfaction.
- A nominal record may satisfy an interface without an `implements` clause.
- A nominal table row may satisfy an interface from its declaration-derived readable row fields. The row remains nominal and table-owned.
- Arrays, Results, enums, tables, columns, primitives, and functions do not acquire magic fields.
- General structural object values cannot satisfy an interface because Copeland has no structural runtime object type. Contextual record literals satisfy only through their selected nominal record.
- Field types are invariant/exact in the first slice. No readable covariance, numeric widening, optional field, mutable field, or implicit conversion is inferred.
- Satisfaction never grants field mutation; current records and rows remain immutable.

Method requirements are deferred. The current compiler has named free functions but no methods, function values, member-callable type, receiver law, overload sets, or variance law precise enough to specify method satisfaction.

### Interface decisions

| Question | M0a recommendation |
| --- | --- |
| Interface composition | Do not use `interface A extends B` in M1b and do not form an inheritance graph. A generic parameter may list multiple explicit requirements. A later milestone may add named requirement composition only as deterministic flattening with cycle/conflict diagnostics. |
| Multiple constraints | Use an explicit ordered list, proposed as `T extends Positioned, Named`; do not introduce arbitrary `A & B`. Parser spelling remains an M1a confirmation item because comma also separates type parameters. |
| `implements` | Deferred. It is unnecessary for implicit satisfaction. If later admitted, it is checked documentation only and cannot change representation or satisfaction. |
| Storage types | In M1b, interfaces are legal only as generic constraints/requirement operands, not variable, field, parameter, return, array-element, Result-component, TSON, or runtime cast types. Existential interface values require separate representation and dispatch law. |
| Interface requirements on interfaces | Deferred with composition. Do not treat `extends` on an interface as inheritance in the first slice. |
| Diagnostics | Report the candidate and requirement name, then missing fields and incompatible fields in interface declaration order. For mismatch show required and actual canonical types and the candidate declaration/member site when available. Bound the list and report a remaining count. |

## Generics as checked parameterized definitions

Recommended starting surface:

```ts
function distanceSquared<T extends Positioned>(value: T): number {
    return value.x * value.x + value.y * value.y;
}

const answer: number = distanceSquared<Point>(point);
```

Type parameters are compile-time parameters. A generic body is checked once against declared requirements; each use proves that supplied closed type arguments satisfy them. Runtime reflection over `T`, type-name branching, backend tests, and undeclared operations are unavailable. Generic behavior is parametric and backend-independent.

### Initial generic decisions

| Area | M0a recommendation |
| --- | --- |
| First declaration family | Generic named functions only. Generic records, enums, tables, interfaces, and aliases are deferred. |
| Constraints | `T extends Requirement` is the familiar one-requirement spelling. Multiple constraints use an explicit bounded list, subject to M1a grammar confirmation; no intersection type. |
| Explicit arguments | M2b requires explicit closed type arguments at calls. |
| Inference | M2c may infer from direct value-argument/parameter positions using one deterministic, non-backtracking pass. Conflicting or absent evidence requests explicit arguments. Return-context-only inference, overload search, and constraint solving are excluded initially. |
| Variance | No variance annotations or inferred declaration-site variance. Parameters are substituted exactly. |
| Defaults | Deferred. |
| Higher-kinded types | Rejected by initial doctrine; no type constructor parameters. |
| Generic aliases/records | Deferred pending useful generic-function evidence and bounded expansion design. |
| Generic recursion | Deferred in M2b. M2a must distinguish ordinary runtime recursion of one closed instantiation from compile-time creation of new instantiations. Expansive instantiation recursion is rejected. |
| Reflection | Unavailable without a future explicit type-evidence feature. `typeof` as a type query does not provide it. |

### Bound, MIR, and backend strategy

M2a should design, without promising a backend representation, this first route:

1. Bind a generic function definition with ordered `TypeParameterSymbol` entries and normalized requirement sets.
2. Check the body using only operations proven by those requirements.
3. At each explicit/inferred use, canonicalize closed type arguments, verify requirements, and intern a stable closed-instantiation identity.
4. Lower each reachable closed instantiation to an ordinary canonical `MirFunction` using existing concrete MIR types and resolved operations.
5. Validate the resulting closed program through shared MIR before either backend runs.

This closed-MIR strategy fits current MIR and JavaScript erasure, and avoids inventing runtime interface values. It is a recommendation for the first slice, not a permanent monomorphization promise. A C# backend may later preserve/share CLR generics, JavaScript may erase/share code, and NativeAOT may specialize when those transformations are proven observationally equivalent to the closed program.

Stable identities must derive from the generic definition identity plus the ordered canonical identities of closed arguments. They must not depend only on traversal order, C# type spelling, JavaScript names, CLR handles, object hashes, or nondeterministic dictionaries. MIR text should display a deterministic escaped identity and readable canonical arguments.

The compiler must impose deterministic limits on instantiation depth, total distinct closed instantiations, canonical type nesting, and generated-code budget. Exceeding a limit is a stable compile diagnostic, never partial output or backend-dependent behavior. Exact numeric defaults and whether identical instantiations can share emitted code are **M2a owner decisions** after corpus measurements.

Backend implications:

- C# generic preservation is an optimization/representation choice, not source law. CLR constraints may be emitted only when exactly equivalent; otherwise generated checks/specialization remain private.
- JavaScript erases annotations but must retain any existing nominal carrier validation. It cannot accept arbitrary objects merely because a constraint was erased.
- NativeAOT requires closed reachability, trimming-safe generated code, controlled specialization, and code-size measurement. Reflection-based generic discovery is excluded.
- Cross-backend tests must compare behavior and diagnostics, not demand identical sharing or code size.

## Explicit TypeScript exclusions and alternatives

| TypeScript family | Initial disposition | Copeland route |
| --- | --- | --- |
| `any`, unchecked assertions, bivariant function parameters | Rejected by doctrine | Exact checking; explicit controlled host boundary if later approved. |
| `object`, `Object`, anonymous mutable DTO shapes | Replaced for ordinary data; existential top-object type rejected | Nominal immutable records; no general runtime structural object. |
| Arbitrary `A | B` and literal unions | Replaced for tagged alternatives | Nominal payload enums plus exhaustive `match`. Result for fallibility; an eventual ordinary Option payload enum for optionality. |
| Arbitrary `A & B` | Replaced for constraints | Multiple explicit requirement entries. Runtime intersections remain rejected. |
| `keyof`, `T[K]`, indexed access | Deferred pending concrete evidence | Explicit fields/requirements and nominal schemas. No type-level property enumeration. |
| `typeof` as a type query | Deferred pending evidence | Explicit declared types. Runtime `typeof` is not thereby accepted. |
| Conditional types and `infer T` | Rejected by initial doctrine | Bounded generic requirements and ordinary payload-enum matching at runtime. |
| Mapped types | Rejected by initial doctrine | Explicit record/interface declarations; future bounded static generation belongs to CTS-STATIC, not type evaluation. |
| Template-literal types | Deferred pending evidence | Nominal enums/records or runtime strings; no initial type-language string computation. |
| Declaration/namespace merging | Rejected by doctrine | One declaration, deterministic duplicate diagnostic. |
| Ambient declarations | Deferred to an explicit interop/import model | Verified metadata/import declarations, not unchecked ambient promises. |
| Higher-kinded types and default type arguments | Higher-kinded types rejected initially; defaults deferred | First-order explicit generic functions. |
| Classes/inheritance | Separate deferred ladder | Records for immutable data; interfaces here are requirements, not a class hierarchy. |
| `unknown` | Deferred, with many initial uses replaced | Generics for parametric code, TSON for typed transport, explicit host boundaries for dynamic input. |

“Rejected by initial doctrine” is stronger than a scheduling deferral but is not a claim that no future evidence could reopen the product decision. “Replaced” means the initial use case has a deliberate Copeland construct, not that every theoretical TypeScript use is expressible.

## Diagnostics strategy

New diagnostics should be family-owned and stable:

- alias declaration, unknown target, duplicate, and bounded cycle path;
- interface declaration, duplicate requirement, unsupported member, and illegal storage position;
- requirement failure with missing/mismatched fields in declaration order;
- generic declaration/constraint errors, explicit argument count, inference conflict/absence, and closed-instantiation limit/cycle;
- parser-specific diagnostics for incomplete `<...>` lists and ambiguous recovery rather than cascaded comparison errors.

Diagnostics use authored names at source sites and canonical types for compatibility facts. Alias expansion must not cause two backends to report different source errors. Requirement checking belongs in binding; open alias/interface/type-parameter forms must not reach ordinary MIR validators. Malformed future generic MIR, if M2a authorizes any, must be rejected by shared validation before artifact emission.

## .NET and TypeScript.NET interop implications

M0a adds no import, package, reflection, metadata, or CLR code. It establishes these future constraints:

- A transparent Copeland alias may name an imported CLR type only after that CLR type has one explicit Copeland semantic type. The alias adds no CLR type or conversion.
- A structural Copeland interface is not a nominal CLR interface. A CLR type may satisfy a Copeland requirement through statically imported readable members; a Copeland record does not automatically implement or emit a CLR interface.
- Closed CLR generic types can be imported as closed Copeland types when their semantics and constraints are representable. Open CLR generic types require an approved arity, substitution, constraint, variance, nullability, and accessibility model.
- Generic method calls may participate in the same bounded explicit/inferred type-argument rules; CLR overload resolution cannot silently become the Copeland language's inference engine.
- Imported CLR constraints should be translated only when they map exactly to approved Copeland requirements. Nominal base-class, constructor, unmanaged, reference/value-type, static-abstract, variance, and runtime-interface constraints remain unsupported until each has a Copeland law. Otherwise the member is unavailable with a precise import diagnostic.
- Targeting C# does not authorize exposing `System.Type`, reflection, CLR layout, C# interface identity, reified generics, exceptions, nullable references, or C# overload behavior as Copeland semantics.
- Copeland can feel like a real .NET language by having verified metadata imports, predictable calls, debuggable symbols, NativeAOT-safe closure, and idiomatic generated artifacts. It need not define itself as C# source generation; a future direct IL/metadata backend must preserve the same law.

Whether CLR nominal interfaces can be explicitly imported as nominal Copeland constraints in addition to structural requirements is **explicitly unresolved** and belongs to the interop ladder.

## Future bounded static execution

CTS-STATIC is a separate, later approval sequence:

```text
source
-> bind types and static values
-> execute bounded static constructs
-> produce a closed ordinary program
-> MIR
-> backend
```

Candidate constructs are `static if`, `static match`, and `static for`. They must be pure, deterministic, resource-bounded, finite-input operations with no network/filesystem access, arbitrary runtime reflection, open-ended loops, or initial language-level recursion. No static construct survives into backend-visible MIR.

Aliases, requirement satisfaction, substitution, and bounded inference are compiler checking, not an excuse to implement an ad hoc evaluator. Mapped/conditional/template-literal type evaluation is not smuggled into CTS-TYPE. CTS-STATIC-M0a must separately define values, staging, termination, diagnostics, caching, and reproducibility.

## Recommended implementation ladder

| Milestone | Bounded result |
| --- | --- |
| CTS-TYPE-M0b | Compilation-unit transparent non-generic aliases; forward references; duplicates/cycles; canonical expansion; diagnostics; no MIR alias nodes or emitted declarations. **Implemented and closed.** |
| CTS-TYPE-M1a | Confirm requirement grammar, field compatibility, table-row evidence, multiple-constraint spelling, diagnostics, and illegal interface positions against parser/member evidence. Documentation/tests may be designed, but behavior changes wait for M1b. |
| CTS-TYPE-M1b | Field-only erased interfaces/requirement sets and implicit satisfaction for nominal records and table rows; constraint positions only; no methods, interface values, composition, `implements`, or runtime carrier. |
| CTS-TYPE-M2a | Generic-function bound/MIR/backend design, stable closed identities, resource limits, recursive-instantiation policy, C#/JS/NativeAOT/code-size measurements. |
| CTS-TYPE-M2b | Bounded generic named functions with explicit type arguments and closed instantiations; no generic records/aliases/defaults/variance/recursion. |
| CTS-TYPE-M2c | Predictable direct-argument inference, adversarial diagnostics, deterministic artifacts, runtime parity, and cross-backend representation closeout. |
| CTS-TYPE-M3 | Consolidated type-system doctrine, excluded-feature audit, malformed-MIR/adversarial parity, corpus/profile closeout, and decision whether any deferred family has earned a next ladder. |
| CTS-STATIC-M0a | Separate documentation-only bounded static-execution audit after CTS-TYPE foundations close. |

M1a is retained because current member access is nominally resolved and generic structural member access has no bound/MIR operation. It should combine with M1b only if the audit produces one unambiguous representation and diagnostic contract without expanding scope.

## Open questions and owner decisions

The following require explicit owner approval before their named implementation milestone:

1. The single compilation-unit type-name namespace and compilation-unit alias scope are implemented by M0b.
2. Approve aliases of interfaces as requirement-position-only transparency once M1b exists.
3. Confirm `T extends A, B` or select another explicit multiple-requirement spelling in M1a.
4. Confirm table rows as satisfying candidates and exact/invariant field compatibility in M1b.
5. Confirm that `implements`, interface composition, method requirements, and interface storage values remain deferred.
6. Approve closed-instantiation MIR as the M2b baseline, including stable identity inputs and measured numeric limits.
7. Decide whether same-instantiation runtime recursion is needed in the first generic implementation; expansive generic recursion remains rejected.
8. Set the M2c inference boundary, especially whether nested `Array<T>`/`Result<T,E>` matching is admitted after direct parameter inference.
9. Define the future CLR import identity and constraint-translation policy; do not infer it from the C# backend.
10. Approve CTS-STATIC-M0a separately; no CTS-TYPE milestone authorizes compile-time execution.

Until those approvals, this document is an architecture recommendation and audit boundary, not implemented syntax.
