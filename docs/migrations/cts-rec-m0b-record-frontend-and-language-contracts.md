# CTS-REC-M0b record frontend and language contracts

CTS-REC-M0b consolidates the originally proposed syntax-only M0b, frontend/MIR M1, and source/bound/MIR portion of `with` from M4. The filesystem valid-fixture contract made a syntax-only stopping point untruthful: accepted fixtures must lower to canonical MIR, while contextual construction, field access, immutability, and `with` all require binding and type identity.

## Delivered

- top-level `record Name { field: Type; }` declarations and deliberate `const record` rejection;
- source-order nominal type IDs and declaration-order field IDs;
- contextual complete construction in all existing expected-type paths;
- resolved immutable field access and focused mutation rejection;
- same-type `with` with authored replacement order and empty-update rejection;
- composition through functions, nested records, Results, arrays, and payload enums;
- dedicated bound nodes and Cope MIR definitions/types/expressions;
- indexed shared MIR record validation and deterministic `.cope` text;
- one deterministic no-artifact diagnostic from each executable backend;
- 4 valid and 25 invalid filesystem record fixtures plus focused compiler/backend/CLI tests.

The conservative recursive-type boundary follows record containment through arrays, both Result components, and payload-enum fields. No recursive solver or payload-enum recursion redesign was added.

## Diagnostic inventory

`COPE-REC-0001`–`0016` cover invalid declaration, duplicate declaration/field, recursion, required context, incomplete construction, unknown field, duplicate entry, initializer type, access, mutation, `with` receiver/empty/type, nominal mismatch, and equality. Backend ownership is explicit through `COPE-CS-REC-0001` and `COPE-JS-REC-0001`.

## Backend and ladder boundary

No C# or JavaScript record representation exists in M0b. Both backends validate MIR, detect record definitions, return their one record-specific unsupported diagnostic, and expose an empty/null artifact. The CLI can write `.cope`; executable targets fail before an output path is created.

The remaining ladder is now:

1. **CTS-REC-M0a:** accepted design/audit.
2. **CTS-REC-M0b:** complete source-to-bound-to-MIR contract, fixtures, diagnostics, `with`, and backend rejection (this milestone).
3. **CTS-REC-M1:** C# backend realization and runtime/order evidence.
4. **CTS-REC-M2:** JavaScript backend realization and Node/privacy evidence.
5. **CTS-REC-M3:** cross-backend parity and closeout.

There is no future redundant frontend/MIR or `with`-frontend milestone. Equality, hashing, ordering, patterns, serialization, interop, recursion, defaults, methods, structural objects, and general object semantics remain excluded.
