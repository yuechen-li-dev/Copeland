# CTS-REC-M2 JavaScript record backend migration

CTS-REC-M2 retires the valid-record `COPE-JS-REC-0001` gate and realizes canonical immutable nominal record MIR in deterministic strict JavaScript.

## Delivered

- Private per-record and per-field `Symbol` identities.
- Frozen null-prototype values with complete fixed symbol-keyed storage.
- Expected-brand assertions for access, `with`, and internal copying.
- Authored-order construction/replacement staging and declaration-order assembly.
- Exactly-once field receivers and `with` sources.
- `let` rebinding for immutable record values.
- Record composition with Results, propagation, unwrap, typed handlers, payload enums, matches, conditionals, calls, and returns.
- Hardened operand staging when a later argument or operand has a statementful prelude.
- Backend corpus, stable hashes, repeated Node execution, representation isolation, immutability proofs, malformed-MIR no-artifact proof, CLI coverage, and C#/Node parity.
- Architecture and language-profile ratification in [CTS-REC-M2](../Copeland/architecture/copeland-ts-javascript-records-cts-rec-m2.md).

## Unchanged boundaries

Record equality, hashing, ordering, patterns, destructuring, methods, classes, interfaces, inheritance, optional/default fields, mutable variants, spread, dictionaries, JSON, reflection, public JavaScript ABI, generic or recursive records, runtime packages, C# representation, and compiler-wide representation unification remain excluded. A record freezes its own shape and slots; contained values keep their own mutability law.

CTS-REC-M3 completed the cross-backend stress, diagnostics/doctrine ratification, and closeout milestone. Record tables are not part of this ladder; CTS-TABLE-M0a is the recommended separate design/audit milestone.
