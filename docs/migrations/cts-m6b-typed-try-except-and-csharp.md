# CTS-M6b: Typed `try`/`except` and C# lowering

CTS-M6b implemented the CTS-M6a Result-handler design without JavaScript handler lowering; CTS-M6c now supplies that backend-local lowering.

Implemented production shapes:

- `TryExceptExpressionSyntax` and `TryValueBlockSyntax` with reserved `try`/`except` keywords.
- `BoundTryExceptExpression`, `BoundValueBlock`, `BoundHandlerId`, and discriminated bound propagation targets.
- `MirTryExpression`, `MirValueBlock`, `MirTryBinding`, `MirHandlerId`, discriminated MIR targets, and `MirValidator`.
- Deterministic Cope MIR text for handler identity, result/error types, blocks, and lexical propagation.
- C# local/label/branch lowering, including nested outer-handler transfers.

Diagnostics reserved by CTS-M6a are now implemented: `COPE-TRY-0001` malformed shape, `0002` value mismatch, `0003` error mismatch, `0004` empty protected target, `0005` unsupported value-block control flow, and `0006` invalid handler binding. Existing propagation diagnostics `COPE-TYPE-0014` through `0016` and unwrap diagnostic `COPE-TYPE-0019` retain their meanings.

The historical JavaScript rejection boundary is retired for valid CTS-M6b handler MIR by [CTS-M6c JavaScript typed `try`/`except`](../Copeland/architecture/copeland-ts-javascript-try-except-cts-m6c.md). Invalid MIR remains artifact-free, and ordinary Result transfer still must not use JavaScript `throw`/`catch`.

Validation covers parser/binder semantics, language fixtures, MIR validation, C# generated-source compilation/runtime recovery, nested outer transfer, JavaScript rejection, and existing Result/unwrap suites. Unwrap remains terminal and bypasses every handler.
