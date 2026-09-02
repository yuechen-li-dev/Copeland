# Precompiled table runtime — CTS-OPT-M1

CTS-OPT-M1 replaces Production JavaScript's bespoke per-column realization with
one module-local trusted scaffold. It changes representation only. TableScript,
TSON, MIR table meaning, query behavior, and the public validation boundary are
unchanged.

## Old runtime shape

`EmitTableRuntime` previously emitted, for every column, a storage constant, a
null-prototype carrier, three symbol properties, a column-specific closure with
finite/integral/range branches, concrete Result construction, and freeze calls.
The Tables burn-in carried 1,062 bytes of literal payload inside 6,340 bytes of
repeated column wrapper code. Row construction was also repeated per table.

## Scaffold and payload split

Production modules containing tables now emit three private helpers:

- `__cope_table_row_view(rowToken, table, index)` creates the immutable row
  projection.
- `__cope_table_trusted_read(...)` implements the one shared bounds and Result
  path.
- `__cope_table_trusted_column(...)` freezes direct literal storage and creates
  one branded semantic column view.

Each table definition still emits its compiler-selected table token, row token,
column slots, column tokens, fixed row count, concrete Result tokens, and direct
column payload. A column is realized by one call using those known values. No
helper examines payload contents, lengths, or first cells to infer schema.

The helpers are emitted only when `catalog.Tables.Count > 0`, remain lexical to
the generated module, and introduce no deployed runtime dependency.

## Trusted construction law

`MirTableDefinition` reaches the backend only after parser, binder, semantic,
and MIR validation. Its authored constants and compiler-generated replacement
expressions therefore supply known column count/order/types, row count,
rectangularity, identity, and type-correct values. Production construction may
freeze and publish those values without rechecking compiler-proven facts.

Trust is not inferred from a runtime value and has no new source syntax. No new
MIR flag was needed: the trusted factory is called only while realizing a
validated `MirTableDefinition`. A future runtime-origin payload path must add
explicit provenance rather than reuse this path implicitly.

## Validated boundary law

Copeland exposes no arbitrary JavaScript table constructor. Generated function
parameter validators, table/row/column validators, TSON carrier checks, and
nominal Result validators remain for values that can arrive at a runtime
boundary. Diagnostic emission retains the old explicit validation shape.

Production removes revalidation only at typed compiler-internal access sites.
Function ingress still rejects a row from a different nominal table. TSON
encoding retains WeakSet-backed provenance and its backing-storage slot.

## Identity and storage

- A table keeps one private table token and compiler-selected symbol slots.
- A row keeps the table-specific row token, owning table, and checked index. It
  never copies fields.
- A column keeps the shared carrier token and table-specific column token. It
  is frozen, null-prototype, and not an array.
- Column arrays remain authoritative. Static field access uses compiler-known
  symbol slots; there is no string dictionary or reflection.
- Semantic Symbol count is unchanged: 20 in the Tables corpus.

## Freeze and publication

The trusted column helper freezes each supplied array before freezing and
returning its carrier. Table construction builds all columns, defines the table
carrier, freezes it, and only then assigns the authored singleton. Rows are
fully defined and frozen before return. No partially initialized value escapes.

## Bounds and Result path

The shared reader preserves `Number.isFinite`, `Number.isInteger`, and the exact
`index < 0 || index >= rowCount` range test. This retains `-0`, NaN, infinities,
fractions, negative values, large finite integrals, and `>= count` behavior. It
constructs the same concrete Result identity and Production enum shapes for
`InvalidIndex` and `OutOfBounds`.

Row-field reads retain the success-tag guard. This is load-bearing because an
adversarial boundary value must not turn an out-of-bounds Result payload into a
cell.

## Security and counterfeit handling

Table, row, and column tokens remain module-private Symbols. TSON-visible
carriers retain WeakSet provenance. The trusted factory is not exported.
Focused Node coverage passes a row from a different same-shape table through a
typed boundary and observes invariant rejection.

## C# implications

The C# backend already emits typed table-specific carriers and constructs its
static singleton before publication. Roslyn and the CLR preserve type and
bounds enforcement without JavaScript descriptor scaffolding. CTS-OPT-M1 adds
no C# path or shared MIR metadata; cross-backend output remains qualification.

## Non-goals

This milestone adds no runtime schema inference, row-oriented storage, public
factory, external runtime package, TableScript/TSON law, query redesign,
general DCE, SSA, or optimizer framework. Diagnostic readability is preserved.
