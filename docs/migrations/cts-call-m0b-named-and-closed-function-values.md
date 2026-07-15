# CTS-CALL-M0b migration record

## Scope

This migration adds exact callable type syntax, references to existing named functions and existing closed generic specializations, and invocation of callable-valued expressions. It deliberately excludes anonymous callable definitions and captures.

## Requirement ledger

| Status | Requirement | Evidence |
| --- | --- | --- |
| satisfied | Exact callable types and transparent aliases | `CallableTypeSyntax`, `CallableTypeSymbol`, and alias resolution |
| satisfied | Named and closed generic references | `BoundFunctionReferenceExpression` reuses `GetOrCreateClosedInstantiation` |
| satisfied | Direct call versus value invocation | Separate Bound/MIR nodes and focused regression tests |
| satisfied | C# and JavaScript realization | delegates; frozen null-prototype WeakMap/WeakSet carrier |
| satisfied | Callable storage/equality exclusions | binding and shared MIR validation |
| stronger evidence | Shared malformed-MIR rejection | `MirValidator` validates callable references and invokes before backends |
| missing | Checked-in corpus byte/hash records and exhaustive CLI/runtime adversarial lanes | deferred validation work |

The remaining missing ledger row means this migration is not declared closed until corpus and full runtime evidence are checked in.
