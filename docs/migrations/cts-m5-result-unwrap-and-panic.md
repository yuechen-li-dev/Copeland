# CTS-M5 migration: Result unwrap and panic

Use `value!` when an `err` must terminate execution rather than propagate. The operation accepts only `T ! E`, yields `T`, and has no fallibility target requirement. Use `value?` when an error should be propagated to a compatible fallible function return.

The stable terminal classification is `COPE-PANIC-UNWRAP`. Payload formatting, exception type names, and stack traces are backend-private. Existing Result failures remain value-based and exception-free. This change does not add `try`/`except`, recoverable panic, Result equality, or error conversion. The later typed lexical Result-handler design is recorded by [CTS-M6a](../Copeland/language/copeland-ts-try-except-design-cts-m6a.md) and implemented by CTS-M6b/M6c; it preserves this panic boundary. CTS-M6d ratifies the final distinction.
