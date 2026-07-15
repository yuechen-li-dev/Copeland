# CTS-UNION-M0b nominal union sugar

**Status:** closed. CTS-UNION-M0b is nominal payload-enum sugar with parser, binder, shared MIR validation, backend parity, corpus retention, and TSON/runtime closeout evidence ratified.

CTS-UNION-M0b accepts a declaration-only TypeScript-shaped pipe spelling and canonicalizes it before MIR:

```ts
type Shape = Circle | Rectangle;
```

It creates the same semantic payload enum as:

```ts
enum Shape {
    Circle(value: Circle),
    Rectangle(value: Rectangle),
}
```

`type UserId = number;` remains a transparent erased alias. A pipe declaration is instead a nominal, value-visible enum family. `Shape` and `OtherShape` are distinct even when their alternatives are identical.

## Implemented law

- The lexer preserves longest-match `||` and emits a distinct `PipeToken` for `|`; pipe has no expression precedence.
- `NominalUnionDeclarationSyntax` preserves the `type` name, equals sign, optional leading pipe, ordered alternatives, pipe tokens, and semicolon. No `UnionTypeSyntax` exists.
- A union may have two through eight direct, same-unit nominal record alternatives. Aliases, enums/unions, interfaces, tables, duplicates, unknown names, and out-of-range declarations are rejected with `COPE-UNION-*` diagnostics.
- The binder predeclares union names as `EnumTypeSymbol` values, binds generated ordered cases with a fixed `value` payload field, and records source provenance in `NominalUnionProvenance`.
- Exact expected-type contextual injection wraps a direct alternative record in its generated enum case. Existing expected-type paths therefore cover variable initializers, assignments, arguments, returns, record fields, enum payloads, arrays, Results constructed through `ok`/`err`, and contextual conditional or match arms.
- Existing enum construction (`Shape.Circle(circle)`), exhaustive matching, containment traversal, MIR lowering, shared malformed-MIR validation, C# emission, JavaScript emission, and TSON planning are reused without a union-specific semantic, MIR, or backend node.
- `COPE-UNION-0002` currently has no independent source path: `type Name = T;` remains a transparent alias by accepted law, and explicit malformed one-arm pipe spellings are stopped earlier by `COPE-UNION-0001`. The slot is therefore an unreachable inventory entry, not current user-visible behavior.

No structural dispatch, narrowing, union inference, union widening, implicit extraction, or union equality is introduced. Equality remains rejected under the existing payload-enum law.

## Canonical boundary

The bound injection is an ordinary `BoundEnumValueExpression`; lowering emits an ordinary `MirEnumValueExpression`. Consequently canonical MIR contains `MirEnum`, `MirEnumValueExpression`, and `MirMatchExpression`, never `MirUnion*`. Backends consume their existing enum carrier/tag/provenance machinery and receive no source-union branch.

Schema-bearing unions use the existing enum identities:

```text
schema#Shape
schema#Shape.Circle
schema#Shape.Circle.value
```

Union-authored malformed canonical enum, match, and TSON states are now rejected before either backend emits an artifact. The shared MIR validator owns the enum/match closeout slice, and both backends continue to surface the same `COPE-*-0002 Invalid MIR` family instead of drifting into backend-specific partial artifacts.

TSON uses the existing payload-enum contract. Union-root `tsonEncode` round-trips through canonical TSON as `schema#Shape` / `schema#Shape.Case` / `schema#Shape.Case.value` with exact C#/Node parity. Union-root `tsonAsset(...)` ingestion is intentionally **not** a second accepted boundary today: the TSON document reader still rejects `NominalUnionDeclaration` inside assets, and M0b documents that exclusion instead of widening the asset language.

## Requirement ledger

| M0b area | Status | Evidence |
| --- | --- | --- |
| Pipe lexing and declaration parser | Satisfied | `Lexer`, `Parser`, malformed-pipe tests, invalid fixtures |
| Alias-versus-union split and nominal identity | Satisfied | binder predeclaration, same-shaped-union rejection, same-shaped-record parity |
| Supported alternatives and explicit exclusions | Satisfied | focused diagnostic inventory plus named invalid fixtures; non-source-expressible array/Result/row/column/open-generic forms are documented as grammar exclusions |
| 2–8 direct-alternative resource bound | Satisfied | focused diagnostics and invalid fixtures for one-arm malformed pipe, 9-arm rejection, and duplicate alternatives |
| Canonical payload-enum mapping and exhaustive match | Satisfied | MIR-structure assertions, equivalent-enum MIR comparison, valid fixtures, backend parity |
| Expected-type direct injection | Satisfied | valid fixture matrix and cross-backend trace proving variable, assignment, argument, field, enum-payload, array, Result, and explicit-generic contexts |
| Generic inference remains record-directed | Satisfied | `NominalUnionTests` and backend parity trace show inferred `Circle` remains a record while explicit `identity<Shape>` injects only under the known target |
| Equality status remains unchanged | Satisfied | no new equality path; unions continue to inherit payload-enum non-equality |
| No union MIR/backend family | Satisfied | MIR assertions plus C#/Diagnostic-JS/Symbolic-JS searches and parity; canonical MIR contains ordinary enum nodes only |
| TSON enum identity/encoding reuse | Satisfied | union MIR/TSON plan identities, canonical runtime round-trips, and exact C#/Node parity are pinned; union-root asset ingestion is documented as an accepted-scope TSON document exclusion |
| Focused C#/Diagnostic-JS/Symbolic-JS runtime parity | Satisfied | repeated Diagnostic Node runtime, Symbolic corpus hash, and cross-backend trace parity |
| CLI stale-output/determinism proof | Satisfied | existing CLI test repeats MIR/C#/Diagnostic/Symbolic output and preserves stale artifacts on frontend failure |
| Focused canonical corpus | Satisfied | `cts-union-m0b` pins source, MIR, C#, and Diagnostic JS byte lengths and SHA-256 hashes; retained Symbolic JS is pinned under the JavaScript backend corpus root required by topology validation |
| Complete reachable `COPE-UNION-*` inventory | Satisfied | reachable union diagnostics now have focused tests and fixtures; `COPE-UNION-0002` is documented as unreachable under the accepted alias law |
| Malformed canonical/MIR boundary reuse | Satisfied | union-authored malformed canonical enum/match/TSON mutations now fail through the shared MIR validator before either backend emits |

The ledger now has zero missing rows. CTS-UNION-M0b is closed as nominal payload-enum sugar, not as a broader TypeScript union system.

## Explicit exclusions

General `TypeSyntax` unions, inline/literal/primitive/null/undefined/structural unions, alias/enum/nested alternatives, generic union declarations, transitive injection, union inference/widening, structural narrowing, runtime shape dispatch, JSON tagging, and a new backend representation remain excluded.
