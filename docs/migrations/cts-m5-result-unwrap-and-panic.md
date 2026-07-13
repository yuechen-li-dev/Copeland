# CTS-M5 migration: Result unwrap and panic

Use `value!` when an `err` must terminate execution rather than propagate. The operation accepts only `T ! E`, yields `T`, and has no fallibility target requirement. Use `value?` when an error should be propagated to a compatible fallible function return.

The stable terminal classification is `COPE-PANIC-UNWRAP`. Payload formatting, exception type names, and stack traces are backend-private. Existing Result failures remain value-based and exception-free. This change does not add `try`/`except`, recoverable panic, Result equality, or error conversion.
