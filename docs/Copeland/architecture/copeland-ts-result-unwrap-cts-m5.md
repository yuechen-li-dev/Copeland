# CTS-M5: Result unwrap and terminal panic

CTS-M5 adds postfix `!` for first-class `Result` values. `result!` evaluates `result` once, returns its success payload on `ok`, and terminates with the backend-neutral classification `COPE-PANIC-UNWRAP` on `err`.

`!` is a postfix operation and binds more tightly than binary operators. It is distinct from prefix Boolean negation and from the `!` in a Result type. Postfix operations chain left to right: `outer!!` unwraps two nested Results, while `outer!?` unwraps then propagates. Each operation is type-checked against the type produced by the previous one.

The bound tree has `BoundUnwrapExpression` and Cope MIR has `MirUnwrapExpression`; it is written as `unwrap <expression>`. Unlike `MirPropagateExpression`, unwrap has no propagation target and works in nonfallible functions.

Both emitters inspect their private Result representation after assigning the operand to a temporary. JavaScript throws a private `Error` carrying the original payload; C# throws a private generated exception carrying the original payload. Those host throws are terminal panic machinery only. Ordinary `err`, Result match, and `?` remain explicit Result control flow and do not use host exceptions.

There is no Copeland `try`/`except` in this milestone. Future Result-based handlers will not catch unwrap panic, and test-host interception of the private throw is not source-language recovery.
