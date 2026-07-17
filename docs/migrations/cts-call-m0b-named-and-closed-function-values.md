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
| stronger evidence | Shared malformed-MIR rejection | one shared nine-case malformed-MIR matrix is rejected before both backends write an artifact |
| stronger evidence | Exact resource limits | source and MIR tests pin 32 parameters / depth 16 as accepted and 33 / 17 as rejected |
| stronger evidence | Adversarial JavaScript carrier boundary | Node tests reject host functions and copied/null-prototype counterfeits, wrong signatures, and mutation attempts |
| satisfied | Checked-in corpus and CLI artifact policy | `TestData/Corpus/cts-call-m0b`; byte/hash pins and repeat/fresh/stale CLI checks cover MIR, C#, Diagnostic JS, and Symbolic JS |
| missing | Cross-backend evaluation-order and Result-flow trace | no checked-in C#/Diagnostic-JS/Symbolic-JS trace yet pins callee/argument ordering, typed recovery, and terminal unwrap behavior |
| missing | Complete source diagnostic inventory | the focused fixtures cover the authored callable failure families, but missing-type/name and every container/TSON diagnostic still need an explicit diagnostic-to-fixture ledger |

## Corpus pins

All corpus artifacts are UTF-8 without BOM and end in one LF. `main.cope` is 865 bytes
(`677CDF3157BAB9B1FD310D33727BBC5094901BF99505A69C35C25BF42E8F0C93`),
`main.g.cs` is 1480 bytes
(`8DD27E8377923BC74A81EB2662D98D98169DE13041F35A8D63BCE5103404A945`),
`main.g.js` is 1546 bytes
(`E2DF6970403EDB9A74E758655DCEA5ECAFE76C286B25F3658AD916177DE0E77E`),
and `main.sym.js` is 1508 bytes
(`B6AD9D99353FBBA8FCFFD6F546581DA688E4DE49A0FC7AD40AB99AAF43712E1A`).

CTS-CALL-M0b remains open until the two missing rows are discharged. CTS-CALL-M0c remains
the boundary for authored arrow bodies and noncapturing definitions, while explicit capture
and environments remain CTS-CALL-M1 work.
