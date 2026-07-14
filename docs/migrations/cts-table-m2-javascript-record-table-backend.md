# CTS-TABLE-M2 JavaScript record-table backend migration

CTS-TABLE-M2 removes the JavaScript valid-table rejection boundary. Valid canonical table MIR now emits deterministic strict-mode JavaScript; malformed table MIR still fails shared validation as `COPE-JS-0002` with no artifact.

The JavaScript backend uses private symbols and frozen null-prototype carriers. Tables are singleton carriers, rows are table-and-index views, and columns are immutable non-array carriers over private frozen declaration-ordered arrays. The backing arrays are closure-private and are not a language-visible or JSON representation.

Table and column indexing returns ordinary existing Result values. Non-finite or non-integral indexes return `InvalidIndex`; negative finite integrals and indexes at or above row count return `OutOfBounds`; `-0` is row/element zero. Existing Result matching, propagation, unwrap, and structured handlers compose without JavaScript `throw`/`catch` for ordinary bounds flow.

No shared table validation was duplicated in JavaScript. No C# representation change, JSON codec, public plain-object/array ABI, mutable API, query/dataframe feature, or package-version change is part of this migration. CTS-TABLE-M3 remains the parity and serialization closeout milestone.
