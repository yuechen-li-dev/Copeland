# CTS-M6c: JavaScript typed `try`/`except`

**Status:** historical implementation record. CTS-M6d closes and ratifies this backend path with shared MIR validation and expanded parity evidence.

CTS-M6c enables the canonical CTS-M6b `MirTryExpression` and `MirValueBlock` in the MIR-only JavaScript backend. It changes neither source syntax, binding, handler allocation, canonical MIR, nor C# emission.

Each generated program that uses a handler declares a compiler-private frozen null-prototype flow token and records for `value`, `handler`, and `function` completion. These records are distinct from private Result values: Result records retain their result token and `ok`/`err` tag, while flow records retain the flow token and completion kind. They are generated implementation detail, not source ABI.

The affected function body executes in a statementful private IIFE. A protected value block returns a value flow on success; `?` returns a lexical-handler flow with the stable MIR handler number, or a function flow. The owning `try` compares the handler identity, binds the original error exactly once, and evaluates its handler block once. Non-owning and function flows bubble unchanged. The generated function boundary validates the flow and converts only a function flow to the normal source Result `err` return. A dangling lexical flow is an invariant panic.

No generated `catch`, `finally`, promise, or host exception transfers ordinary Result flow. Existing unwrap and invariant panics remain terminal throws and therefore bypass every Copeland handler.

Evidence is owned by `Copeland.TS.Backend.JavaScript.Tests`: repeated Node execution (Node v26.2.0), exact LF corpus comparison, and the `try-except-success` artifact. Existing non-try corpus artifacts remain byte-stable.
