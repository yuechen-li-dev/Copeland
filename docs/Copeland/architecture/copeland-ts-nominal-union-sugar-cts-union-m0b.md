# CTS-UNION-M0b nominal union sugar

**Status:** implemented frontend and canonical-MIR slice; closeout evidence remains incomplete.

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
- Existing enum construction (`Shape.Circle(circle)`), exhaustive matching, containment traversal, MIR lowering, C# emission, JavaScript emission, and TSON planning are reused without a union-specific semantic, MIR, or backend node.

No structural dispatch, narrowing, union inference, union widening, implicit extraction, or union equality is introduced. Equality remains rejected under the existing payload-enum law.

## Canonical boundary

The bound injection is an ordinary `BoundEnumValueExpression`; lowering emits an ordinary `MirEnumValueExpression`. Consequently canonical MIR contains `MirEnum`, `MirEnumValueExpression`, and `MirMatchExpression`, never `MirUnion*`. Backends consume their existing enum carrier/tag/provenance machinery and receive no source-union branch.

Schema-bearing unions use the existing enum identities:

```text
schema#Shape
schema#Shape.Circle
schema#Shape.Circle.value
```

## Requirement ledger

| M0b area | Status | Evidence |
| --- | --- | --- |
| Pipe lexing and declaration parser | Satisfied | `Lexer`, `Parser`, `NominalUnionTests` |
| Alias-versus-union split and nominal identity | Satisfied | binder predeclaration and semantic tests |
| Direct-record alternatives, duplicates, 2–8 bound | Satisfied | binder validation and fixtures |
| Canonical payload-enum mapping and exhaustive match | Satisfied | existing enum binding/lowering plus union test |
| Expected-type direct injection | Satisfied | shared binder contextual path |
| No union MIR/backend family | Satisfied | canonical lowering uses existing enum nodes only |
| TSON enum identity/encoding reuse | Stronger evidence needed | enum path is shared, but M0b-specific fixed-point test is not yet pinned |
| Focused C#/Diagnostic-JS/Symbolic-JS runtime parity | Satisfied | C# runtime, repeated Diagnostic Node runtime, and CLI Symbolic Node execution all observe `16` |
| CLI stale-output/determinism proof | Satisfied | union CLI test repeats MIR/C#/Diagnostic/Symbolic output and preserves stale output on frontend failure |
| Focused canonical corpus | Satisfied | `TestData/Corpus/cts-union-m0b` pins source, MIR, C#, and Diagnostic JavaScript; the JavaScript artifact is hash-checked |
| Complete diagnostic inventory and exhaustive filesystem matrix | Missing | focused tests cover core acceptance/rejection laws; the requested exhaustive inventory is not complete |

The remaining diagnostic-inventory row means CTS-UNION-M0b is **not declared closed**. A follow-up evidence milestone should add only fixture inventory coverage unless it reveals a shared enum defect.

## Explicit exclusions

General `TypeSyntax` unions, inline/literal/primitive/null/undefined/structural unions, alias/enum/nested alternatives, generic union declarations, transitive injection, union inference/widening, structural narrowing, runtime shape dispatch, JSON tagging, and a new backend representation remain excluded.
