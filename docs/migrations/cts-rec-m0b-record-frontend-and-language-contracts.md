# CTS-REC-M0b record frontend and language contracts

> Historical milestone boundary: CTS-REC-M1 and M2 subsequently implemented both backend realizations and retired the valid-record rejection diagnostics. CTS-REC-M3 now closes the ladder.

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

`COPE-REC-0001`–`0016` cover invalid declaration, duplicate declaration/field, recursion, required context, incomplete construction, unknown field, duplicate entry, initializer type, access, mutation, `with` receiver/empty/type, nominal mismatch, and equality. At the M0b boundary backend ownership was explicit through `COPE-CS-REC-0001` and `COPE-JS-REC-0001`; M1 retired the former and M2 retired the latter for valid canonical MIR.

## Backend and ladder boundary

No C# or JavaScript record representation existed at the M0b checkpoint. CTS-REC-M1 subsequently provided C# representation and execution, and CTS-REC-M2 provided deterministic JavaScript representation and Node execution.

The remaining ladder is now:

1. **CTS-REC-M0a:** accepted design/audit.
2. **CTS-REC-M0b:** complete source-to-bound-to-MIR contract, fixtures, diagnostics, `with`, and backend rejection (this milestone).
3. **CTS-REC-M1:** C# backend realization and runtime/order evidence — implemented.
4. **CTS-REC-M2:** JavaScript backend realization and Node/privacy evidence.
5. **CTS-REC-M3:** cross-backend parity and closeout — implemented and closed.

There is no future redundant frontend/MIR or `with`-frontend milestone. Equality, hashing, ordering, patterns, serialization, interop, recursion, defaults, methods, structural objects, and general object semantics remain excluded.
