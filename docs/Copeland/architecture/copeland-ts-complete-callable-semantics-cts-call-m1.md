# Copeland TS complete callable semantics (CTS-CALL-M1)

**Status:** implemented callable model and closeout contract.

Copeland TS callable values are runtime pairs of a stable code identity and an immutable environment. A named function reference and a noncapturing arrow have an empty environment. A capturing arrow has stable ordered environment slots populated once, in authored `capture { ... }` order, before the callable is published.

## Source law

Callable types use exact canonical signatures: parameter count, ordered parameter types, and return type must match after alias erasure. Parameter names affect diagnostics only. A callable has at most 32 parameters and callable-type and expression nesting are both limited to 16.

Arrows support zero, one, or parenthesized typed parameters, exact contextual parameter typing, optional return annotations, expression bodies, and block bodies. They do not support generic parameters, async, generators, defaults, rest parameters, destructuring, methods, or `this`.

Lexical capture is never implicit. A read of an outer runtime binding must appear in `capture { name, ... }`. The capture list names only outer lexical values, rejects duplicates and non-runtime declarations, has a 16-binding limit, and snapshots each binding at construction. Captured bindings are immutable within lifted code. A later `let` rebind does not affect the already-created callable.

The compilation-unit global-declaration model is deliberately distinct from lexical capture. Declaration-owned global values, including record-table singletons, remain approved global reads under the existing language model; they are not hidden environment cells. Function declarations remain code identities, not capture candidates. Any value introduced by a function, block, pattern, loop, or arrow is lexical and must be captured explicitly before an arrow may read it.

Named functions and explicitly closed generic specializations are callable values. Open generic values remain rejected. Generic inference treats callable values as exact atomic candidates; it does not infer through callable positions.

## Canonical lowering

`BoundCallExpression`/`MirCallExpression` retain direct named calls. Callable-by-value invocation uses `BoundInvokeExpression`/`MirInvokeExpression`. Named references use `BoundFunctionReferenceExpression`/`MirFunctionReferenceExpression`; explicit capture uses `BoundCallableConstructionExpression`/`MirCallableConstructionExpression`.

A callable construction names lifted code, carries its exact public callable type, and carries ordered environment values. Lifted code receives those environment values before its ordinary parameters. Frontend scope objects, capture syntax, source nodes, and inference state do not reach MIR. Shared MIR validation checks callable code identity, slot count/type, tail signature, construction, and invocation before either backend emits.

## Backends

C# keeps direct calls static and signature-specific delegates demand-emitted. Capturing callables use generated sealed environment carriers with private readonly fields and an `Invoke` method that calls generated static lifted code. No reflection, dynamic dispatch, expression tree, or C# closure defines Copeland capture semantics.

Diagnostic and Symbolic JavaScript use frozen null-prototype callable carriers with private WeakSet/WeakMap provenance. Capturing carriers additionally own frozen null-prototype environment carriers whose values are private and passed explicitly to compiler-owned lifted code. Counterfeit objects, wrong signatures, and invalid environments terminate through callable invariants.

Callable values may flow through locals, parameters, returns, arrays, records, enum payloads, Result values, and erased interface field requirements. They remain forbidden in record tables, TSON/TSON assets/encoding, JSON/host serialization, equality, and hashing.

## Limits and evaluator seam

The compiler permits at most 512 lifted definitions, 16 slots per environment, 16 callable-expression nesting levels, and 8,192 total possible capture slots per compilation. The frontend rejects statically visible self/uninitialized capture. The future evaluator receives only:

```text
(code identity, immutable environment, arguments)
-> call frame
-> value, Result transfer, handler transfer, or terminal failure
```

It must not reconstruct frontend lexical scopes.
