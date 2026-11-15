# Native signed 64-bit integers

The C# backend supports `long`, projected directly to `System.Int64`. Importing
`System` also admits `Int64` as the same type in annotations. Values never travel
through `number`/binary64 on this pathway.

```typescript
using System;

record Clock { ticks: long; }

function advance(ticks: long): long {
    return ticks + 1;
}

function exact(): string {
    const clock: Clock = { ticks: 9007199254740993L };
    return String.From(advance(clock.ticks)); // "9007199254740994"
}

function widen(value: int): long { return Convert.ToInt64(value); }
function narrow(value: long): int { return Convert.ToInt32(value); }
```

Use an uppercase `L` suffix for exact Int64 literals, including
`-9223372036854775808L` and `9223372036854775807L`. Unsuffixed integer literals
retain existing Int32 rules. Small Int32 literals adapt at a known `long`
destination or alongside a `long` binary operand. Stored values do not implicitly
widen or narrow. With overloaded CLR methods, use `L` literals to identify the
Int64 overload explicitly.

Addition, subtraction, multiplication, division, remainder and unary negation use
checked native arithmetic. Invalid constant arithmetic has source diagnostics;
runtime overflow throws `OverflowException`. Explicit CLR conversions retain CLR
overflow behavior. `Convert.ToInt32(long)` is checked; converting to floating point
is explicitly lossy and should not be used for exact clocks. `String.From(long)`
uses invariant decimal formatting.

Parameters, results, record fields, arrays, CLR properties, public CLR constants,
closed CLR generic arguments and static evaluation preserve the same type and all
64 bits. General CLR mutable field access remains outside the existing binding
surface. Bitwise operations retain their existing Int32-only contract.

This is a native backend capability. JavaScript emission rejects `long`; it does
not emit unsafe JavaScript numbers. Shader targets are not extended. Existing
TSON numbers use binary64, so TSON encoding rejects reachable `long` fields and
asset decoding does not reinterpret rounded doubles as exact Int64. Project such
fields explicitly to canonical decimal strings for interchange. The `type`
profile's existing string timing contracts remain compatible.

Executable evidence: `tests/Copeland/Copeland.TS.Tests/Int64InteropTests.cs` compiles
through parsing, binding, MIR, the actual C# backend and Roslyn, then invokes the
resulting assemblies. It covers values beyond binary64's exact integer range,
Int64 bounds, native CLR access, checked conversion/overflow, static execution and
explicit transport/backend rejection.
