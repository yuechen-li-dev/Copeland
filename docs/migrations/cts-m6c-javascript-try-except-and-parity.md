# CTS-M6c: JavaScript typed `try`/`except` and parity

CTS-M6c removes the CTS-M6b JavaScript handler boundary. The JavaScript backend now emits private branded structured-flow records for canonical typed handler MIR; it does not use JavaScript exceptions for ordinary Result transfer.

The implementation is local to `Copeland.TS.Backend.JavaScript`. A function containing a handler uses a private completion scope; the function boundary converts `toFunction(error)` into its existing Result `err`. Lexical transfers are selected by stable handler identity, so nested handler flows bubble unless the identity belongs to the current `try`. Result values are ordinary values carried by `value` completion and are never conflated with flow records. Postfix unwrap still throws `COPE-PANIC-UNWRAP` and is not intercepted.

The backend corpus owns `try-except-success.ts` and its exact generated JavaScript artifact. Node execution coverage proves success, payload recovery, nested handler-to-outer transfer, and handler-to-function transfer; repeated execution is deterministic. Node used for this evidence is v26.2.0. CTS-M6d completes the broader nesting, panic, diagnostic, and artifact closeout.
