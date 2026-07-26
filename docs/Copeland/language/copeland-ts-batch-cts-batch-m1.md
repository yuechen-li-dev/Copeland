# CTS-BATCH-M1: deterministic structured data-parallel mapping

`batch` is Copeland TS's synchronous, structured map expression:

```ts
const squares = batch values as value {
    return value * value;
};
```

The input is evaluated once and must be a one-dimensional Copeland array. The
item binding has the input element type and every item body has one final,
value-producing `return`. The result is an array with exactly the input length;
result `i` always belongs to input `i`. A batch expression joins before it
returns, so a caller never receives a partially populated result array.

CTS-BATCH-M1 accepts primitive values, strings, immutable records, and arrays
of those shapes. Captures must be read-only and portable by the same rule.
Item-local `let` and `const` declarations are allowed. Mutable outer captures,
CLR interop, npm calls, inline C#, async operations, callable invocation, and
nested batch expressions are diagnosed.

The CLR backend preserves `MirBatchExpression` to selection and emits bounded
`Parallel.For` work over input indices. Each worker writes only its own private
result slot. Failures are retained privately; after joining, the runtime throws
one failure selected by the lowest failed input index, including that index in
its message. This provides stable ordering and failure identity without
exposing partial output.

The CLR runtime test proves genuine overlap without measuring speed: an
emitted-module private test seam can set a controlled maximum degree and a
per-item entry hook. The test gates two item bodies, tracks the active count
atomically, and requires a peak above one. It exits early on single-processor
environments. The fields are private, emitted only for batch-containing
modules, and remain null/zero in production, so they introduce no authored
worker or scheduler control.

The JavaScript backend deliberately uses a synchronous, sequential index loop.
It preserves the same mapping, ordering, join, and failure semantics but does
not claim CPU parallelism. `Promise.all` and npm transport are not batch
realizations.

Worker counts, tasks, locks, scheduling, cancellation, and partitioning are
runtime-owned implementation details. Reductions, filtering, flattening,
async batch, arbitrary iterables, inline C# or npm work in a batch, and nested
scheduling remain deferred. A later capability system may replace the bounded
portable-value predicate with explicit `Immutable`/`BatchSafe` facts.
