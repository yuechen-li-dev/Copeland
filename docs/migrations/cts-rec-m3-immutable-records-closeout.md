# CTS-REC-M3 immutable records closeout

CTS-REC-M3 closes the immutable nominal record ladder. The ratified authority is [Copeland TS immutable nominal records closeout](../Copeland/architecture/copeland-ts-immutable-records-closeout-cts-rec-m3.md).

## Delivered

- Ratified source, MIR, C#, and JavaScript laws and representation boundaries.
- Collected shared malformed-record-MIR rejection and no-partial-artifact proofs across both backends.
- Added a repeated C#/Node adversarial matrix for nested construction, argument order, access through branches/matches/Result/unwrap/handlers, nested update, source preservation, and logical short-circuiting.
- Added demand-driven record-helper isolation proofs.
- Fixed JavaScript logical short-circuit lowering for statementful right operands.
- Fixed C# statementful payload-enum matching when the scrutinee is a direct case constructor.
- Retained all existing record and pre-record corpus artifacts byte-for-byte.
- Closed the `COPE-REC-0001` through `0016` inventory and confirmed obsolete valid-record backend diagnostics remain absent.

## Compatibility

No syntax, record feature, MIR shape, diagnostic code, representation contract, fixture harness, CLI syntax, or default backend changed. The two production changes repair general expression lowering and do not alter existing corpus text. Record equality and all other deferred boundaries remain rejected or undefined.

## Ladder

1. CTS-REC-M0a: immutable nominal record design — accepted.
2. CTS-REC-M0b: frontend, binding, MIR, validation, diagnostics, and fixtures — implemented.
3. CTS-REC-M1: deterministic C# sealed-class realization — implemented.
4. CTS-REC-M2: deterministic JavaScript private-token realization — implemented.
5. CTS-REC-M3: adversarial parity, doctrine, diagnostics, and artifact closeout — implemented and closed.

The recommended next independent milestone is **CTS-TABLE-M0a**, a design/audit milestone. It must not inherit an implementation mandate from CTS-REC.
