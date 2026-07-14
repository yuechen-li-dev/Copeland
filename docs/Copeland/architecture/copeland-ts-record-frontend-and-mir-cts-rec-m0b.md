# Copeland TS record frontend and MIR (CTS-REC-M0b)

**Status:** implemented frontend-to-MIR contract. CTS-REC-M1 realizes this MIR in C# and [CTS-REC-M2](copeland-ts-javascript-records-cts-rec-m2.md) realizes it in JavaScript.

## Source contract

The top-level declaration grammar is:

```text
record-declaration := `record` identifier `{` record-field* `}`
record-field       := identifier `:` type `;`
record-literal     := `{` (identifier `:` expression) (`,` ...)* `}`
record-with        := expression `with` record-literal
record-access      := expression `.` identifier
```

There is no `const record`, constructor call, `new`, structural object type, shorthand, spread, computed field, default, method, or mutable field form. A brace literal binds as a record construction only when one expected `RecordTypeSymbol` is already known. Context flows from typed local initialization, assignment targets, function returns, known arguments, nested record fields, Result payloads, payload-enum arguments, and the existing context-propagating expression forms.

Record declarations are predeclared in source order. `RecordTypeId` values are `rN`; field IDs are `rN.fM`, where `N` is the one-based record declaration allocation and `M` is the zero-based field declaration ordinal. These compilation-local IDs are deterministic for identical input but intentionally change when declarations are edited. Type equivalence compares record identity, never field shape or display name.

Containment through records, arrays, either Result component, and payload-enum fields participates in the first-slice cycle check. This is the deliberately conservative rule: any such path back to a record is rejected with `COPE-REC-0004`, even when a future backend representation might add indirection.

## Ordering and immutability

The record definition preserves field declaration order. Construction and `with` nodes independently preserve authored initializer/replacement order. Therefore `{ y: second(), x: first() }` retains `second()` then `first()` in bound nodes and MIR even though canonical field order is `x`, `y`. A future backend must capture source and replacement values once before assembling declaration-order storage.

Fields are intrinsically immutable. `let` changes only whether a binding may be replaced:

| Operation | Valid? | Reason |
| --- | ---: | --- |
| `const p: Point = {...}` | Yes | Immutable binding to immutable value |
| `let p: Point = {...}` | Yes | Reassignable binding to immutable values |
| `p = p with {...}` when `p` is `let` | Yes | Replaces the binding value |
| `p = p with {...}` when `p` is `const` | No | Reassigns a constant binding |
| `p.x = value` | No | Mutates an immutable record field |

`with` evaluates its source once, evaluates replacements once in authored left-to-right order, preserves unspecified fields, and produces exactly the source nominal type. It is not a staged mutation; every replacement observes the original source value.

## Bound and MIR contract

The semantic layer owns `RecordTypeSymbol`, `RecordFieldSymbol`, `BoundRecordDeclaration`, `BoundRecordConstructionExpression`, `BoundRecordFieldAccessExpression`, and `BoundRecordWithExpression`. Field initializer objects carry resolved field symbols rather than names.

Cope MIR owns `MirRecordDefinition`, `MirRecordFieldDefinition`, `MirRecordType`, `MirRecordConstructionExpression`, `MirRecordFieldAccessExpression`, and `MirRecordWithExpression`. Definitions carry nominal and field IDs; operation lists remain in authored order. `.cope` prints definitions in source/declaration order and prints operation entries in authored order with IDs visible.

Shared MIR validation indexes definitions and rejects blank/duplicate IDs and names, recursive definitions, nonexistent record-type references, unknown record/field operations, incomplete or duplicate construction, type mismatches, mismatched access identity, and wrong/unknown/duplicate/empty `with` replacements.

## Diagnostics and backend boundary

Frontend diagnostics are the bounded `COPE-REC-0001` through `COPE-REC-0016` family: invalid declaration, duplicate declaration/field, recursion, missing context, incomplete construction, unknown field, duplicate initializer/replacement, initializer mismatch, invalid access, immutable mutation, invalid/empty `with`, replacement mismatch, nominal mismatch, and equality rejection.

Valid record source emits canonical MIR. [CTS-REC-M1](copeland-ts-csharp-records-cts-rec-m1.md) maps it to deterministic sealed C# classes, and [CTS-REC-M2](copeland-ts-javascript-records-cts-rec-m2.md) maps it to private token-branded frozen null-prototype values. Shared validation rejects malformed MIR before either backend emits output. Equality remains unsupported.

## Evidence

The filesystem language contract is under `Language/Valid/records` and `Language/Invalid/records`. Focused tests cover syntax shape, IDs, nominal binding, authored order, MIR text stability and malformed MIR. M1 adds C# corpus and runtime proofs; M2 adds JavaScript corpus, Node representation/order proofs, CLI success, and C#/Node parity. Existing non-record corpus snapshots remain unchanged.
