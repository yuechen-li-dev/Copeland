# Copeland TS TypeScript-shaped union syntax design (CTS-UNION-M0a)

**Status:** documentation and repository-audit milestone. This is a proposed independent compatibility-surface ladder, not an implemented language feature. [CTS-TYPE-M3](../architecture/copeland-ts-foundational-type-system-closeout-cts-type-m3.md) remains closed and unchanged.

## Product decision

Copeland should accept a deliberately narrow TypeScript-shaped declaration:

```ts
type Shape = Circle | Rectangle;
```

when it can mean exactly one new nominal tagged value, canonically equivalent to this existing payload-enum model:

```ts
enum Shape {
    Circle(value: Circle),
    Rectangle(value: Rectangle),
}
```

The direction is:

```text
familiar TypeScript syntax
-> safer Copeland canonical semantics
-> existing closed MIR
-> backend-private realization
```

This is compatibility sugar, not TypeScript structural-union compatibility. Syntax redundancy is acceptable; a second union algebra, MIR family, or backend path is not.

## Status vocabulary and current audit

| Classification | Finding and exact evidence |
| --- | --- |
| Implemented law | `type Name = ExistingType;` is a transparent, erased `TypeAliasSymbol`, resolved before MIR. The parser has `TypeAliasDeclarationSyntax` and `ParseTypeAliasDeclaration` in [`Parser.cs`](../../../src/Copeland/Copeland.TS/Syntax/Parser.cs); the binder predeclares, topologically resolves, and cycle-checks aliases in [`Binder.cs`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs); `TypeAliasSymbol.CanonicalType` is in [`Symbols.cs`](../../../src/Copeland/Copeland.TS/Semantics/Symbols.cs). |
| Implemented law | A compilation unit has one case-sensitive type-name space across aliases, records, tables, enums, and compiler-owned enum names. `AnalyzeAliasTypeNameCollisions`, `PredeclareAliases`, `PredeclareRecords`, `PredeclareTables`, and `PredeclareEnums` establish the order in [`Binder.cs`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs). Aliases have no value symbol; enum types are also declared as values for `Shape.Case(...)` construction. |
| Current rejection | The lexer recognizes `||` as `PipePipeToken` (`Lexer.cs`, `SyntaxKind.cs`, and `SyntaxFacts.cs`), but a lone `|` falls through as `COPE-LEX-0003 Invalid character`; no lone-pipe `SyntaxKind` exists. `&&` and constraint-only `&` already have distinct tokens. |
| Implemented law | Payload enums predeclare `EnumTypeSymbol`, bind ordered `EnumCaseSymbol` and `EnumPayloadFieldSymbol`, construct `BoundEnumValueExpression`, and bind exhaustive `BoundMatchExpression`. See [`Types.cs`](../../../src/Copeland/Copeland.TS/Semantics/Types.cs), [`Symbols.cs`](../../../src/Copeland/Copeland.TS/Semantics/Symbols.cs), [`BoundNodes.cs`](../../../src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs), and [`Binder.cs`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs). |
| Implemented law | `MirLowerer.LowerEnum`, `LowerExpression`, and `MirValidator` carry only `MirEnum`, `MirEnumValueExpression`, and `MirMatchExpression`; the validator also checks enum membership, ordered payloads, TSON-plan correspondence, and record/enum containment cycles. See [`MirLowerer.cs`](../../../src/Copeland/Copeland.TS/Lowering/MirLowerer.cs), [`MirNodes.cs`](../../../src/Copeland/Copeland.TS.Mir/MirNodes.cs), and [`MirValidator.cs`](../../../src/Copeland/Copeland.TS.Mir/MirValidator.cs). |
| Proof-era implementation | The C# backend emits each `MirEnum` as an abstract record with sealed case carriers in `CSharpBackend.EmitEnum` and uses `EmitEnumMatch`; the JavaScript backend validates and emits enum values/matches, private type tokens, provenance sets, frozen payloads, and validators in [`CSharpBackend.cs`](../../../src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs) and [`JavaScriptBackend.cs`](../../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs). These are realizations of canonical MIR, not source-union semantics. |
| Implemented law | TSON already has nominal enum schema/value/plan forms. A schema-bearing enum receives `schema#Name`; lowering derives `schema#Name.Case` and `schema#Name.Case.field`; `MirValidator` requires the same declaration and payload order. See [`TsonSchema.cs`](../../../src/Copeland/Copeland.TS/Tson/TsonSchema.cs), [`TsonValues.cs`](../../../src/Copeland/Copeland.TS/Tson/TsonValues.cs), [`MirLowerer.cs`](../../../src/Copeland/Copeland.TS/Lowering/MirLowerer.cs), and [`MirValidator.cs`](../../../src/Copeland/Copeland.TS.Mir/MirValidator.cs). |
| Implemented law | Expected types are supplied to variable initializers, assignments, named-call arguments, returns, record fields, enum payloads, arrays, Results, conditional/match arms, `tsonAsset`, and deferred generic contextual arguments. Current compatibility is exact `TypeFacts.AreEquivalent` through `Binder.IsAssignable`; no conversion relation exists. |
| Implemented law | Generic inference treats nominal records/enums atomically and decomposes only Array, Result, and column; it has no union synthesis. `ClosedTypeIdentity` and `CollectInferenceEvidence` are in [`Binder.cs`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs). Primitive equality only is admitted by `IsPrimitiveEqualityType`; enum equality is not implemented. |
| Historical proposal | [`copeland-typescript-support.md`](../architecture/copeland-typescript-support.md), the historical M1 profile, and the original CTS-TYPE-M0a audit describe payload enums as the replacement for arbitrary unions. They do not establish a pipe spelling or structural narrowing law. |
| M0a recommendation | Add one declaration-only pipe sugar later, canonicalized to ordinary nominal payload-enum semantics before MIR. Do not add a general `TypeSyntax` union operator. |
| Explicitly unresolved | A future canonical `union` keyword or enum-pipe spelling, acceptance of enum/nested-union alternatives, union declaration resource limits, and cross-schema authoring policy require owner approval before implementation. |

Existing diagnostic and fixture evidence is deliberately reused: `TypeAliasDiagnosticInventoryTests`, `GenericDiagnosticInventoryTests`, `TypeAliasTests`, `BinderTests`, enum/match language fixtures under `Language/Valid/tagged-data` and `Language/Invalid/tagged-data`, C#/Node enum runtime and corpus tests, TSON asset/encoding tests, and invalid fixtures for records, Results, `null`, coercion, and unsupported type syntax. No M0a fixture is added.

## `type` has two semantic families

The apparent irregularity is accepted deliberate TypeScript compatibility:

```text
type Name = SingleType;
    transparent erased alias

type Name = Alternative | Alternative ...;
    new nominal union declaration, canonically a payload enum
```

The pipe makes the family syntactically unambiguous. A union declaration is not an alias merely because it begins with `type`; it has a nominal type identity, generated cases and fields, a runtime carrier, and (when permitted) TSON identities. A single alternative remains an alias and is not a degenerate union.

`union Shape = Circle | Rectangle;` and `enum Shape = Circle(value: Circle) | Rectangle(value: Rectangle);` are not introduced or authorized here. A future canonical spelling could improve explicitness, but `type Shape = A | B` must remain accepted compatibility sugar if the ladder proceeds. Existing brace-delimited enum syntax remains the explicit escape hatch.

## Proposed first-slice grammar and recovery

The only admitted location is the right side of one compilation-unit `type` declaration. The first implementation should add a dedicated `PipeToken` only after preserving the maximal `||` token, and should parse a separate union-declaration syntax/provenance wrapper rather than extending `TypeSyntax`.

```ebnf
type-declaration ::= "type" Identifier "=" union-alternatives ";"
union-alternatives ::= [ "|" ] nominal-record ( "|" nominal-record )+
```

Leading-pipe formatting is accepted; whitespace and line breaks have no meaning:

```ts
type Shape =
    | Circle
    | Rectangle;
```

All other pipe locations remain rejected, including `Circle | Rectangle` annotations, parameters, arrays, Results, parentheses, generic arguments, constraints, expressions, and bitwise OR. The parser must recover at the declaration semicolon, give every diagnostic a nonempty authored-token span, reject empty/leading-only/trailing alternatives without cascades, and preserve source alternative order. `!`, `<...>`, `&`, arrays, and `||` retain their present grammars.

## Initial semantic family

**Recommended M0b minimum: two through eight directly named nominal records from the same compilation unit.** This is deliberately narrower than existing enums. It yields deterministic names and payload types without nested injection paths, aliases acquiring tags, or JavaScript's current recursive-payload-enum limitation.

| Candidate | M0b decision | Reason |
| --- | --- | --- |
| Direct nominal record | Allowed | `Circle` maps deterministically to `Circle(value: Circle)`. Same-shaped records remain distinct. Forward references already exist. |
| Transparent alias | Rejected | It is erased; it must not accidentally acquire a case/tag. `Round` aliasing `Circle` reports: `Union alternatives must name nominal declarations directly; 'Round' is an alias of 'Circle'. Use 'Circle'.` |
| Existing payload enum | Deferred | It would create a nested carrier and raises explicit-versus-contextual wrapping questions. Do not infer a benefit from existing enum support. |
| Named union declaration | Rejected in M0b | It creates transitive paths and recursive/nesting concerns. Reconsider only after a direct construction law is proven. |
| Primitive, array, Result, table, row, column, interface, type parameter, open generic, anonymous object, literal, `null`, `undefined`, `void`, `any`, `unknown` | Rejected | None supplies the required direct nominal record case/payload law. |

The compiler-defined one-field payload name is **`value`**, matching established enum/TSON convention. Cases appear in authored declaration order. Duplicate direct alternatives and duplicate derived case names are errors, never collapsed. Aliases that canonicalize to the same record are rejected as aliases before duplicate analysis. Records with the same fields remain separate alternatives. The existing record and MIR containment validation remains authoritative; M0b should reject any graph it cannot prove non-recursive, rather than adding a union-specific recursive representation.

The proposed bound of eight alternatives is an owner decision, selected to match the existing generic parameter cap and keep diagnostics bounded; it is not a current implemented limit. Maximum nesting is zero in M0b because union alternatives are records only. Diagnostics must report in authored order and show at most the existing bounded number of alternatives where a list is needed.

## Identity, construction, injection, and matching

Every declaration creates a distinct nominal identity: `Shape` and `OtherShape` are different even with the same ordered alternatives, and neither is an authored enum with matching cases. Compiler-local IDs must not be durable identities. For a schema-bearing declaration, reuse enum identity law:

```text
schema#Shape
schema#Shape.Circle
schema#Shape.Circle.value
```

Direct construction uses the existing spelling and machinery:

```ts
Shape.Circle(circle)
```

Contextual injection is additionally proposed only when an expected target union is already known and a source has exactly one direct alternative type:

```text
Circle -> Shape.Circle(Circle)
```

It is legal in variable initialization, assignment, named-function arguments, returns, record-field initialization, enum payloads, known-union array elements, known Result success/error components, and contextual conditional/match arms. It may be used by `tsonAsset` projection only if that projection already has a permitted exact nominal expected type. The source evaluates once in authored order; there is no runtime shape test, widening, extraction, best-fit search, or backend conversion.

The M0b law forbids nested and transitive injection. If a later slice admits `type Value = Shape | Text`, only `Shape -> Value` could inject; `Circle -> Value` could not. M0b avoids that question by rejecting union alternatives entirely. No injection occurs between independently declared unions.

Match remains byte-for-byte payload-enum matching:

```ts
const area: number = match shape {
    Circle(value) => value.radius * value.radius,
    Rectangle(value) => value.width * value.height,
};
```

The generated enum's case order, payload arity/binding, duplicate and missing-case diagnostics, scrutinee-once rule, nested-match behavior, and Result/`try`/`except` interaction all apply. The tag is authoritative: no `typeof`, property-existence, truthiness, control-flow structural narrowing, or host shape inspection is added. Equality remains unsupported because payload-enum equality is currently unsupported.

## Generics, Result, and TSON

A completed union is one atomic nominal type. Inference does not decompose it or synthesize it: `identity(circle)` infers `Circle`; `identity<Shape>(circle)` may inject because explicit substitution supplies the expected `Shape`. Generic union declarations, union inference, distributive behavior, and generic-to-generic synthesis are excluded.

The categories remain separate:

```text
union declaration: one of several nominal application alternatives
Result<T, E>: typed success/failure control flow
future Option<T>: explicit optionality without null
```

`T | E` is never Result and `T | null` is never optionality. Pipe syntax does not replace `!`, `ok`, `err`, `?`, unwrap, or `try`/`except`.

For TSON-capable unions, no new schema/value/plan variant is permitted. Reuse `TsonEnum`, `TsonEnumDefinition`, `MirTsonEnumPlan`, and existing fixed-point encoding in declaration order. The selected case remains tagged; its record payload retains its own nominal identity. Same-unit `$schema` ownership, exact reachable schema checking, `tsonAsset`, `tsonEncode`, and canonical fixed-point rules remain those of ordinary enums. Cross-schema alternatives are rejected in the first slice; no untagged structural-union object and no JSON tagging policy is introduced.

## Bound, MIR, and backend strategy

The recommended route is:

```text
UnionDeclarationSyntax / source provenance
-> frontend UnionSymbol or equivalent diagnostic wrapper
-> canonical EnumTypeSymbol, EnumCaseSymbol, EnumPayloadFieldSymbol
-> BoundEnumDeclaration / BoundEnumValueExpression / BoundMatchExpression
-> MirEnum / MirEnumValueExpression / MirMatchExpression
-> current C# and JavaScript enum realization
```

A frontend provenance wrapper is useful for source diagnostics, injection eligibility, and tooling. It must disappear before MIR. `MirUnionType`, `MirUnionConstruction`, `MirUnionMatch`, backend union branches, host `object`, JavaScript `typeof`, property dispatch, untagged objects, nullable values, and exceptions are prohibited. Existing topology rules—frontend creates validated backend-neutral MIR; backends consume MIR and do not reference frontend/TSON compiler-host state—remain the guardrail.

## Diagnostics and future fixture plan

M0b should allocate a bounded `COPE-UNION-*` inventory (the numeric slots are not allocated by M0a) for malformed/fewer-than-two/too-many alternatives; unsupported family and primitive; alias with canonical replacement; duplicate/case collision; unknown/interface/open-generic; recursive or nested restriction; illegal inline pipe; unavailable/unrelated injection; cross-schema restriction; and resource limits. Existing enum diagnostics own constructor, wrong-payload, duplicate/missing-match, and exhaustiveness cases. Messages must name canonical records, retain authored order, recommend direct nominal spelling or explicit enum syntax, and never mention a backend.

Future valid fixtures: two/three records, leading pipe, forward references, same-shaped records, explicit construction, every contextual injection position, exhaustive/nested match, generic identity and explicit generic injection, TSON asset/encode fixed point, and C#/Node parity. Future invalid fixtures: one/duplicate/primitive/alias/unknown/interface/array-Result-table-row-column alternatives; inline/literal/null/undefined/structural unions; nested/transitive injection; missing case/wrong payload/equality; general union inference; cross-schema and resource failures. The fixture owner must add malformed parser and recovery cases for `||`, leading/trailing pipes, semicolons, arrays, Results, generic arguments, and constraints.

## Recommended ladder and owner decisions

1. **CTS-UNION-M0b:** declaration grammar, direct-record nominal identity, direct construction, contextual injection, canonical enum lowering, parser/diagnostic/limit proof. Deliberately reject unless the existing full enum path is reached.
2. **CTS-UNION-M1:** C#/JavaScript/TSON end-to-end parity only if M0b did not already prove it atomically through reused enum MIR. Do not create a milestone merely for numbering symmetry.
3. **CTS-UNION-M2:** adversarial closeout, compatibility diagnostics, corpus, and doctrine ratification.

Owners must approve: the eight-alternative bound and diagnostic truncation; whether M0b requires a distinct `UnionSymbol` versus source provenance; future enum/nested-union alternatives and explicit nested construction; a future canonical spelling; and cross-schema policy after same-unit proof. CTS-CALLABLE, CTS-TSXML, CTS-STATIC, CLR/.NET interop, CTS-JS-EMIT Hangul-radix identifiers, generic nominal declarations, and Option design remain independent.

## Explicit exclusions

M0a does not implement or authorize production syntax, general/inline/primitive/literal/null/undefined/structural/untagged unions, union narrowing/inference/distributive conditionals/intersections/generic union declarations, runtime structural dispatch, JSON tags, callable/lambda/capture/TSX/static/CLR work, CTS-JS-EMIT/Hangul changes, or package/version changes.
