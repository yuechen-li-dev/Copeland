# CTS-REC-M1 C# record backend migration

CTS-REC-M1 retires the C# blanket diagnostic `COPE-CS-REC-0001` for valid canonical record MIR. The C# path is now source → canonical MIR → deterministic generated C# → Roslyn compilation and execution in the existing proof harness.

## Delivered

- one deterministic ordinary sealed class per `MirRecordTypeId`;
- deterministic get-only members per `MirRecordFieldId` and a complete constructor;
- direct nominal type mapping in all existing valid type positions;
- authored-order, exactly-once construction staging followed by declaration-order constructor arguments;
- direct stable-ID field reads with receiver-once precedence preservation;
- source-once and authored replacement-order `with` lowering to a new instance;
- statementful enum-match lowering when record-valued arms require it;
- Result success/error, match, propagation, handler, and unwrap composition;
- payload-enum field/payload/match composition;
- backend corpus and repeated compile/execute proofs, including final result `42`;
- CLI C# success and retained JavaScript `COPE-JS-REC-0001` no-output behavior.

## Compatibility

No frontend or MIR redesign was needed. Shared validation remains the only acceptance boundary and malformed MIR yields no generated artifact. Existing non-record C# corpus artifacts, `.cope` artifacts, and JavaScript artifacts remain unchanged.

The representation deliberately adds no C# `record`, C# `with`, setters, equality, hash, comparison, clone, deconstruction, reflection, `dynamic`, dictionaries, runtime package, or mutable record API. CLR layout, accessibility, and reference identity remain private backend details.

## Production defects found

Complex record field receivers exposed a precedence defect in naive direct formatting: an assignment receiver required parentheses or C# parsed the field read as part of the assignment right-hand side. Field lowering now parenthesizes the single emitted receiver. C# reserved-keyword mangling was also completed so valid source identities such as `event` cannot break generated compilation; existing identifiers and corpus text remain stable.

## Deferred

CTS-REC-M2 owns JavaScript realization. CTS-REC-M3 owns cross-backend parity and closeout. Equality, patterns, serialization, recursive/generic records, interop, tables, and `record table` remain separately deferred.
