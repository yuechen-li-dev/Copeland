# Copeland TS field-only interface requirements (CTS-TYPE-M1a)

**Status:** accepted M1a architecture and historical baseline audit. Its recommended bounded vertical slice is implemented by [CTS-TYPE-M1b](../architecture/copeland-ts-interface-and-explicit-generics-cts-type-m1b.md); [CTS-TYPE-M3](../architecture/copeland-ts-foundational-type-system-closeout-cts-type-m3.md) is the final canonical authority. [CTS-TYPE-M2b](../architecture/copeland-ts-bounded-generic-inference-cts-type-m2b.md) validates these requirements after direct candidates are inferred; constraints never invent a candidate.

## Decision

Copeland will use familiar declarations for erased field requirements:

```ts
interface Positioned {
    x: number;
    y: number;
}
```

Semantically, `Positioned = Requires(readable field x: number, readable field y: number)`. An interface is neither runtime data nor a nominal object type, C# interface, JavaScript brand, existential value, inheritance hierarchy, or construction target. It is a compile-time requirement set.

The only initial consumer is a type-parameter constraint:

```ts
function inspect<T extends Positioned & Named>(value: T): string
```

Here, and only in a generic constraint, `&` conjoins named requirement sets. It does not introduce general intersections, stored intersection values, or runtime intersections.

## Historical M1a baseline inventory

The following records the repository evidence available at the M1a audit baseline. It corrects M0a's then-superseded alias absence: transparent non-generic aliases were implemented and erased before MIR; interfaces and generics were still absent. It is not a statement of current M1b behavior; see the M1b and M2a records above.

| Concern | Exact current path | Finding |
| --- | --- | --- |
| Contextual declaration keywords | [`Parser.ParseMember`](../../../src/Copeland/Copeland.TS/Syntax/Parser.cs#L61), [`Parser.ParseTypeAliasDeclaration`](../../../src/Copeland/Copeland.TS/Syntax/Parser.cs#L94), [`SyntaxFacts`](../../../src/Copeland/Copeland.TS/Syntax/SyntaxFacts.cs#L7) | `type` is an `IdentifierToken` recognized only at compilation-unit member start. `interface`, `extends`, `implements`, and `readonly` are identifiers today. |
| Compilation-unit and type grammar | [`SyntaxNodes`](../../../src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs#L74), [`Parser.ParseTypeSyntax`](../../../src/Copeland/Copeland.TS/Syntax/Parser.cs#L379) | Alias, record, enum, table, and function declarations exist. Approved type syntax is canonical primitives/names, arrays, Results, parenthesized types, `Table.Row`, and `column T`; no interface/type-parameter/intersection node exists. |
| Type names and aliases | [`BinderImpl.Bind`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L78), [`AnalyzeAliasTypeNameCollisions`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L120), [`PredeclareAliases`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L169) | Aliases, records, payload enums, and tables are predeclared case-sensitively and checked in one compilation-unit type-name collision pass. Alias targets resolve before bodies, supporting forward names. Aliases do not enter the value scope. |
| Fields and rows | [`Parser.ParseRecordDeclaration`](../../../src/Copeland/Copeland.TS/Syntax/Parser.cs#L392), [`Symbols`](../../../src/Copeland/Copeland.TS/Semantics/Symbols.cs#L55), [`Types`](../../../src/Copeland/Copeland.TS/Semantics/Types.cs#L54) | Record fields and declaration-derived table-row fields have stable IDs and canonical `TypeSymbol` types. Their current source syntax requires field separators; record fields use semicolons. |
| Member access and binding | [`MemberAccessExpressionSyntax`](../../../src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs#L549), [`Binder.BindMemberAccess`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L2848) | `.` parses generally, but binding permits table columns, table-row fields, and record fields only. It resolves rows and records to separate bound nodes and rejects every other receiver. |
| Bound/MIR access | [`BoundNodes`](../../../src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs#L295), [`MirLowerer.LowerExpression`](../../../src/Copeland/Copeland.TS/Lowering/MirLowerer.cs#L237), [`MirNodes`](../../../src/Copeland/Copeland.TS.Mir/MirNodes.cs#L339) | `BoundRecordFieldAccessExpression` and `BoundTableRowFieldAccessExpression` lower directly to ordinary concrete MIR accesses. There is no interface/type parameter/generic node. |
| Backend field access | [`CSharpBackend.EmitExpression`](../../../src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs#L577), [`JavaScriptBackend.EmitExpression`](../../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs#L1417), [`JavaScriptBackend.ValidateValueType`](../../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs#L503) | C# emits ordinary get-only carrier member access. JavaScript emits concrete carrier access and validates accepted MIR/value carriers; neither has a source-interface carrier. |
| Expected types, equivalence, diagnostics | [`Binder.BindExpression`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L1474), [`Binder.IsAssignable`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L3135), [`TypeFacts.AreEquivalent`](../../../src/Copeland/Copeland.TS/Semantics/Types.cs#L109), [`Diagnostic`](../../../src/Copeland/Copeland.TS/Diagnostics/Diagnostic.cs) | Expected types reach initializers, returns, arguments, record construction, arrays, Results, and TSON sites. Assignability is error recovery plus exact equivalence. Diagnostics are source-token anchored; alias work establishes parser, declaration/collision, resolution, then executable-binding precedence. |
| TSON identity and order | [`TsonSchema`](../../../src/Copeland/Copeland.TS/Tson/TsonSchema.cs#L249), [`Binder` schema matching](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L2130), [`MirValidator`](../../../src/Copeland/Copeland.TS.Mir/MirValidator.cs#L370) | Concrete nominal schema identity and declaration field/column order are checked. There is no interface schema/value kind. |
| Fixtures and historical proposals | [`Language`](../../../tests/Copeland/Copeland.TS.Tests/Language), [`Copeland TypeScript Support`](../architecture/copeland-typescript-support.md), [historical language profile](../architecture/language-profile.md) | Current fixtures cover records, rows/tables, aliases, and invalid object/member access; no fixture accepts interfaces/generics. Historical class/interface/concept-like proposals are not current doctrine. |
| Topology | [`Validate-CopelandTsTopology.ps1`](../../../tools/Validate-CopelandTsTopology.ps1), [`Validate-DependencyBoundaries.ps1`](../../../tools/Validate-DependencyBoundaries.ps1) | Frontend depends on MIR; backends consume validated MIR and cannot reference the frontend. MIR is BCL-only. This prevents frontend requirement symbols leaking into either backend. |

`<`/`>` already lex as comparison tokens and alias parsing has bounded generic-rejection recovery. A lone `&` does **not** exist: [`Lexer.NextToken`](../../../src/Copeland/Copeland.TS/Syntax/Lexer.cs#L109) recognizes only `&&` and otherwise reports an invalid character; [`SyntaxKind`](../../../src/Copeland/Copeland.TS/Syntax/SyntaxKind.cs#L49) has only `AmpersandAmpersandToken`. This is an implementation absence, not a contradiction of this design.

## Interface declaration grammar

M1b should add a contextual compilation-unit declaration keyword, consistent with `type`:

```text
InterfaceDeclaration ::= `interface` Identifier `{` InterfaceField* `}`
InterfaceField       ::= Identifier `:` Type `;`
```

`interface` remains an `IdentifierToken`, recognized only by `ParseMember` at compilation-unit scope. This preserves ordinary local/value uses and follows established `type` behavior. Semicolons are required: record field grammar is the nearest production evidence and provides no contrary reason to invent automatic semicolon insertion.

Names occupy the existing case-sensitive compilation-unit type-name namespace, colliding with aliases, records, payload enums, tables, and interfaces. They never enter the runtime/value namespace. Interface names and all field types participate in the same predeclaration and forward-reference model used by aliases/nominal declarations. Fields are unique and declaration-ordered; their types use the existing approved canonical grammar and transparent aliases. Fields have no initializer, construction, assignment, or mutation operation. Runtime-storage recursion is irrelevant, but a field's canonical type and each finite requirement set must be well-defined; no interface composition graph exists in this slice.

The first parser/binder must reject, rather than accept as latent syntax: `readonly` (redundant because every approved field is readable/immutable), `?` optional members, methods, getters/setters, call/index signatures, computed members, generic interfaces, composition/inheritance, and `implements`. `readonly` is rejected initially, not accepted as sugar, so the first accepted grammar has one spelling and no unused modifier law.

## Requirement-only positions and satisfaction

An interface may occur only as a named operand of a generic `extends` constraint. It is illegal as a variable, field, payload, function parameter/return storage type, array element, Result component, table column, TSON schema/value, cast, or construction target. Thus `const value: Positioned = point;` is invalid even when `point` satisfies `Positioned`.

This is intentional: interface storage would need an existential representation, dispatch law, identity, carrier, and interop policy. “Erasure” does not make that harmless. The M1a design grants implicit, static structural satisfaction only to immutable nominal records, nominal table-row views, transparent aliases whose canonical target is one of those candidates, and future closed parameters instantiated with one of those candidates.

A candidate satisfies every required readable field when it has the field name and exactly equivalent canonical type after alias expansion. Extra fields are allowed; missing fields and type mismatches fail; candidate order does not matter; requirements report in interface declaration order. Satisfaction changes neither nominal identity nor backend representation, provides no mutation/equality/adapter/wrapper, and is statically checked. Compatibility is invariant and exact: `required number == actual number`; a required alias compares as its canonical target. There is no widening, covariance, optionality, implicit conversion, mutable-field variance, `any`, `unknown`, or host-language assignability.

Table rows qualify because their fields are statically known, declaration-derived, immutable readable views. Their storage remains table-owned and columnar: a row is never converted to record-shaped storage or serialized as a row.

## Constraint conjunction and requirement model

Future constraint grammar may use:

```text
Constraint ::= Identifier `extends` RequirementOperand (`&` RequirementOperand)*
RequirementOperand ::= InterfaceName
```

M1b/M2 must add `AmpersandToken` after the lexer checks `&&`, then admit it only in this grammar. It must not be added to expression precedence or general `TypeSyntax` parsing.

Conjunction flattens named requirement sets into deterministic normalized fields. Same-named equivalent canonical fields merge; incompatible fields are diagnosed at the constraint site. Preserve authored interface then field order for diagnostics. Repeated identical named interfaces receive a stable redundancy diagnostic (rather than silent deduplication), because they are likely author error. No inheritance graph, record intersection type, or runtime conjunction is created.

The internal model is frontend-only:

```text
InterfaceSymbol(Name, DeclarationIdentity, OrderedFields)
RequirementFieldSymbol(Name, CanonicalType, DeclarationLocation, StableIdentity)
RequirementSet(OrderedSourceInterfaces, NormalizedFields)
```

Identities derive from declaration order plus the interface/field declaration identity, never object hashes or dictionary iteration. Normalize iteratively over ordered operands and fields; cache/intern canonical requirement sets keyed by ordered symbols/canonical field identities. Candidate lookup may use an ordinal dictionary for lookup only while retaining ordered lists for output. Bound diagnostics, preserve alias provenance beside canonical type text, and avoid recursive interface traversal because composition is deferred.

## Generic member access and closed-instantiation erasure

Within a future generic body, `value.x` must bind against `T`'s normalized requirement set, not masquerade as a concrete record or row. Add a frontend bound form conceptually equivalent to:

```text
BoundRequirementFieldAccess(receiver, typeParameter, requirementFieldIdentity, canonicalFieldType)
```

At each closed instantiation, validate satisfaction then rewrite the operation to `BoundRecordFieldAccessExpression` or `BoundTableRowFieldAccessExpression`. The existing lowerer already turns those concrete nodes into [`MirRecordFieldAccessExpression`](../../../src/Copeland/Copeland.TS.Mir/MirNodes.cs#L339) and [`MirTableRowFieldAccessExpression`](../../../src/Copeland/Copeland.TS.Mir/MirNodes.cs#L344), so this path is mechanically compatible without a new open MIR node.

Under the recommended closed-instantiation strategy, no interface, requirement set, open type parameter, generic requirement access, or generic definition reaches canonical MIR. Every emitted instantiation has ordinary validated concrete access and both backends remain unaware of interfaces. An open MIR operation is not justified unless a later implementation proves closed specialization cannot preserve the existing concrete access contract; it is not needed for theoretical symmetry.

## M1b/M2 staging decision

Do **not** ship declaration-only interfaces as an inert ordinary source feature (option A), and do not split constraint syntax away from a usable body/lowering path (option C). Recommend a single narrow vertical slice: **M1b combines field-only interface declarations with explicit generic functions, one or more constrained parameters, and closed call-site instantiation; M2 closes specialization/lowering breadth and diagnostics/performance only if it remains necessary.**

This is the smallest usable behavior: an interface has its sole legal consumer, satisfaction is exercised, and requirement-member access is proven through existing concrete MIR nodes. The slice must exclude generic records/aliases/enums/tables, inference, overloads, runtime interfaces, and open MIR. If a prototype cannot atomically bind, satisfy, specialize, and lower one closed generic function through both backends, stop and rescope rather than releasing inert declarations.

## Diagnostics

Reserve stable `COPE-INTERFACE-*` / `COPE-REQUIREMENT-*` families for malformed declarations, duplicate/colliding names, duplicate fields, unsupported members, illegal storage positions, unknown field types, repeated requirements, conflicting conjunction fields, missing candidate fields, incompatible candidate fields, ineligible candidates, and constraint-undeclared member access. Exact numeric allocations remain an owner decision before implementation.

Parser diagnostics precede declaration/collision analysis, then interface field/type resolution, constraint normalization, satisfaction, and generic-body binding. Every primary span is nonempty: offending keyword/member token, later colliding name, duplicate field name, constraint operand/`&`, candidate type, or accessed member. Missing/mismatched fields are ordered by interface declaration; output must show required and actual canonical types plus useful alias provenance, cap long lists, and state the remaining count. A malformed interface suppresses its dependent cascades; no backend-dependent error participates.

## TSON and backend/.NET boundaries

Interfaces are not TSON schemas or values. A satisfying record retains nominal record identity; a satisfying row remains a table-owned row view. Interfaces supply neither nominal schema identity nor serialization order. TSON assets target concrete nominal schemas; `tsonEncode` needs a closed concrete eligible value; canonical TSON/encoding plans contain no interface, requirement set, or conjunction.

No backend emits an interface merely because source declares one: no C# `interface`, JavaScript token/brand/provenance/validator/wrapper/vtable, or new carrier. Closed concrete operations emit after instantiation. A later optimizer may retain CLR generic/interface machinery only if observationally equivalent; Copeland structural satisfaction is not nominal CLR-interface implementation. CLR metadata import and constraint translation stay in the interop ladder.

## Explicit exclusions, owner decisions, and next milestone

M1a does not implement or authorize production interface/generic syntax, type parameters, constraint binding, satisfaction, interface MIR/runtime/storage/construction, methods/composition/`implements`/classes, optional/index/call/computed/accessor members, general intersections/unions, generic records/aliases/enums/tables, static evaluation, CLR metadata import, CTS-JS-EMIT, TSON/JSON, package, or version changes.

Owner decisions adopted here: contextual `interface`; required semicolon field-only declarations; initial rejection of `readonly`; exact invariant field compatibility; repeated named requirements are errors; constraint-only `&`; and closed-instantiation erasure to existing concrete access nodes. Remaining implementation-time owner decisions are diagnostic numbers/text, the bounded list cap, and whether the combined M1b vertical slice can remain atomic after a small design spike.

**Recommended next milestone:** CTS-TYPE-M1b, the narrowly bounded field-only-interface plus closed explicit generic-function vertical slice described above; do not begin a separate declaration-only interface implementation.
