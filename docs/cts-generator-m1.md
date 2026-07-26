# CTS-GENERATOR-M1

Copeland synchronous generators are typed, lazy pull sequences. A call to a
generator creates an iterator session; its body starts only when the consumer
advances it. Each advancement runs through one `yield`, completion, or error.

```ts
export function* values(): Iterable<number> {
    yield 1;
    yield return 2; // accepted C#-friendly alias
}

for (const value of values()) {
    Console.WriteLine(value);
}
```

`yield value` and `yield return value` are represented by the same bound and
MIR yield operation. `return;` and `yield break;` both complete the iterator;
returning a value is rejected. `yield* source` delegates lazily to another
`Iterable<T>` and `for...of` consumes that same typed protocol.

Copeland owns generator syntax, typing, control-flow legality, Bound/MIR
identity, and the observable iterator protocol (`IsGenerator`, `Iterable<T>`,
`MirYieldStatement`, and `MirForOfStatement`). The CLR M1 backend realizes the
validated protocol through `IEnumerable<T>` and native C# iterator machinery;
the JavaScript M1 backend realizes it through a native `function*`. Their state
machines are backend implementation details, not source-language semantics.

Both projections retain lazy start, one-yield-per-advance, local/capture
lifetime, failure-on-advance behavior, stable completion, delegation order,
and early-close disposal supplied by their native iterator protocols. CLR
resource `using` cleanup is exercised when an enumerator is disposed early.
The current JavaScript-authored surface has no resource-using or `finally`
statement construct to normalize beyond native generator closing.

JavaScript generators reject recursive `next()` natively. The CLR backend adds
a small compiler-generated enumerable/enumerator wrapper around the private
native iterator so a public Copeland iterator session also rejects reentrant
`MoveNext` with a deterministic `InvalidOperationException` and treats repeated
disposal as idempotent.

M1 deliberately excludes async generators, `await` in a generator,
`next(value)`, consumer `throw()`, and final generator return values. Inline
C# blocks are rejected in generator bodies. A generator session is mutable and
is intended for one active consumer; backend-native iterator reentrancy rules
apply.

`yield` placement in `catch`/`finally` remains outside the current authored
control-flow surface. An explicit shared automaton execution substrate remains
available for future async generators, durable inspection, persistence,
replay, and explicit `flow`/`state` work; it is intentionally not required for
this synchronous-generator milestone.
