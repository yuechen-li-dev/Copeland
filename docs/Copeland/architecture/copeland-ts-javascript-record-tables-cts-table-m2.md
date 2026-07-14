# Copeland TS JavaScript record tables (CTS-TABLE-M2)

CTS-TABLE-M2 realizes validated canonical record-table MIR in the JavaScript backend. It preserves the CTS-TABLE-M0a language law and the M0b shared-MIR boundary; malformed table MIR remains rejected by `MirValidator` before JavaScript realization.

## Runtime representation

Each table definition emits a private table `Symbol`, a distinct row `Symbol`, and private symbols for its resolved columns. The authored singleton is a frozen null-prototype carrier with fixed, non-enumerable symbol descriptors. Its declaration-ordered column symbols point to frozen null-prototype column carriers; a column carrier has a private brand and a private read closure, so it is not an array (`Array.isArray` is false) and exposes no storage array.

Each column is initialized once, in declaration order, into a private frozen dense array. The array is captured only by its column read closure and is never a table, row, or column property. The table carrier is frozen only after every column, column view, and row-read closure is complete. Repeated table references return the same singleton.

Rows are private frozen null-prototype views carrying the table-specific row token, table carrier, and checked index. A row projects fields from the authoritative column closure; it does not contain a copied record. Table and row validators require the exact private token, null prototype, frozen state, expected symbol slots, and fixed descriptor shape. Column validation requires the private column carrier token. Counterfeit, cross-table, and impossible malformed states take the existing terminal invariant panic path, which is intentionally outside Copeland `except` flow.

## Access and flow

Table and column access stage the receiver and index once in source order. `Number.isFinite` and `Number.isInteger` classify `NaN`, infinities, and fractional values as `TableBoundsError.InvalidIndex`; finite integral values below zero or at/above the fixed row count produce `OutOfBounds`. JavaScript `-0` passes the range check and indexes zero, matching the C# binary64 behavior. Successful and failed accesses use existing frozen Result values and existing Result validation, matching, propagation, unwrap, and structured `try`/`except` flow. Bounds flow uses no generated `catch` or host exception; `throw` remains reserved for terminal invariant and unwrap panic paths.

Closed `MirTableConstant` trees emit literals (including `-0`), records in declared field order, payload enums in payload order, and non-void Result success/error values recursively. The runtime object shape is backend-private. It is not the deferred canonical table JSON contract.

## Evidence and boundaries

Focused JavaScript backend tests prove valid emission, deterministic repeated emission, and shared malformed-MIR rejection. Node 26.2.0 runtime tests execute the table representation twice and cover singleton identity, frozen/null-prototype carriers, non-array columns, descriptor immutability, `-0`, index classification, and same-shaped row isolation. The pinned `m2-table-basic.g.js` SHA-256 is `B9AEA6132233229C4F594E9AB34F89F9D4E8F906B160CC1485CE2706436E3C26`; `m2-table-nested.g.js` is `7D72CC23337D65B4F1841D01B5E7E7ED04BD65794109F3D43FB54EEDF3856145`. CLI coverage compiles valid table source to MIR, C#, and JavaScript and executes the JavaScript artifact. The C# representation is unchanged and remains the parity authority for this milestone.

Closure validation used `dotnet build/test Copeland.TS.slnx` (282 frontend, 77 JavaScript, and 101 C# tests; each backend lane completed in about 3 seconds), `Copeland.slnx` (including 20 CLI tests), and `JointTaskForce.slnx`; all passed. Both topology and dependency-boundary checks passed. The Machina slow lane and NativeAOT publish were intentionally omitted because no shared/Machina infrastructure changed and no publish lane was requested.

M3 remains responsible for table-ladder parity closeout and serialization work. No JSON, public host ABI, builders, queries, table equality, row construction, or mutable table operation is introduced here.
