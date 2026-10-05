# TypeScript instincts in Copeland TS (M26)

If a reasonable TypeScript author would try it, Copeland must do one of three things:

1. Accept it with well-defined Copeland semantics.
2. Reject it with one primary diagnostic that includes the exact preferred Copeland form.
3. State that the capability is not yet expressible and provide the closest supported alternative.

This is a bounded law, backed by the `TypeScriptInstinctCorpus`, rather than a claim to accept every TypeScript program. Diagnostics carry authored `SourcePath`, `Position`, and `Length`; repair diagnostics also carry optional `SuggestedReplacement` and `RepairKind` fields. Dependent errors stop at the first failed receiver or type. Independent errors can still be reported.

## Native strings

These calls are typed native operations in MIR and have matching C# and JavaScript implementations:

| Form | Semantics |
| --- | --- |
| `String.Split(value, separator)` | Ordinal separator, preserves empty fields. Empty separator splits UTF-16 code units; empty input then gives an empty array. |
| `String.IndexOf(value, search)` | First ordinal UTF-16 offset, or `-1`; empty search gives `0`. |
| `String.CodeAt(value, index)` | UTF-16 code unit as `int`. Negative or upper-bound index traps with `String index is out of bounds.` |
| `String.Slice(value, start, end)` | End exclusive. Negative offsets count from the end; both bounds clamp to the string. Reversed bounds give an empty string. |
| `String.Join(parts, separator)` | Joins a `string[]` in order. |

There are no optional search positions, regular-expression separators, locale rules, or arbitrary CLR string methods in this surface. Strings remain immutable. `text.split(",")` receives the exact repair `String.Split(text, ",")`. `text.slice(start)` is repaired to `String.Slice(text, start, text.length)`. `.Length` is repaired to `.length`.

## Numeric and mutation policy

`int` is signed 32-bit. Addition, subtraction, multiplication, unary negation, and increment/decrement wrap modulo 2^32, interpreted as signed two's complement. C# emission explicitly uses `unchecked` even in a checked host; JavaScript emission normalizes arithmetic and uses `Math.imul` for multiplication. Static integer evaluation follows the same arithmetic law.

Integer division truncates toward zero. `int.MinValue / -1` wraps to `int.MinValue`; the corresponding remainder is zero. Remainder has the dividend's sign. Division and remainder by zero trap with `Integer division by zero.` Host exception classes are not a shared language identity.

`&`, `|`, and `^` accept only `int`. `<<` and signed arithmetic `>>` mask the shift count to its low five bits. Their result is `int`; there is no coercion from floating values or strings. Hex literals contain one to eight hexadecimal digits and specify an int32 bit pattern. Decimal `-2147483648` is accepted; a positive decimal magnitude outside int32 remains rejected. Exponent literals retain the existing numeric literal policy.

`Int.Floor`, `Int.Ceil`, `Int.Round`, and `Int.Truncate` retain explicit checked conversion to int32. Ordinary wrapping arithmetic does not change conversion policy or introduce implicit numeric widening. `number`/floating arithmetic retains its existing binary64 policy. Named wrapping arithmetic is unnecessary because ordinary integer arithmetic already wraps.

Named mutable locals support prefix/postfix `++`/`--` and `+=`, `-=`, `*=`, `/=`, `%=`. Postfix returns the previous value; prefix returns the updated value. MIR records that distinction. Invalid operands receive typed errors. Computed array or CLR indexer compound updates require binding the receiver/index once and spelling out the indexed assignment. Immutable record fields require `with` construction of an updated value.

`===` and `!==` are accepted aliases of typed `==` and `!=`. They add no identity comparison, coercion, or new equality domain.

## Collections and CLR interop

`MutableArray<T>(length, initializer)` evaluates the initializer once and fills every slot with that value. Supported immutable elements include strings, records, and enums. Negative length traps. `freeze()` retains its existing copy policy; initializer convenience adds no mutable record identity.

```typescript
record Sample { value: int; }

function samples(): Sample[] {
    const values: MutableArray<Sample> = MutableArray<Sample>(3, { value: 7 });
    values[1] = { value: 9 };
    return values.freeze();
}
```

Native `MutableMap<K, V>` and `MutableList<T>` are not implemented in M26. Do not treat a CLR container as a portable native container. For CLR-only growth:

```typescript
using System.Collections.Generic;

function collect(values: int[]): int[] {
    const result = new List<int>();
    for (const value of values) {
        result.Add(value);
    }
    return result.ToArray();
}
```

Imported CLR generic types can be closed with supported scalar or closed CLR type arguments and constructed with `new List<int>()` or `new Dictionary<string, int>()`. The constructor-call form `List<int>()` also works; `new` is accepted interop syntax. Return/parameter annotations bind imported types before function declarations. CLR indexer reads and writes retain reflected accessor identity. Nested generic owner types and unsupported type arguments receive bounded CLR generic diagnostics; this is not arbitrary TypeScript generic instantiation.

For CLR-only keyed storage, use `Dictionary<K, V>` with `ContainsKey` before indexed reads. Dictionary hashing, equality, and ordering are CLR behavior. No native insertion-order or Option-valued lookup claim attaches to this alternative. JavaScript emission cannot execute CLR interop.

## Branches and batch

The existing expression conditional remains the canonical branchy value construction:

```typescript
function magnitude(value: int): int {
    return if (value < 0) { -value } else { value };
}
```

Batch bodies can normalize a final structured `if`/`else` with one returned value per branch, or a final guard return followed by a fallback return, into this existing expression form. Bounded pure prefix branches are accepted. Loops, general early returns, and effectful bodies retain the purity restriction and receive expression/helper guidance. No second batch language or JavaScript ternary was introduced.

C# batch emission uses a sequential loop below 128 elements and `Parallel.For` otherwise. Output ordering and lowest-index failure selection remain deterministic. Failure storage is allocated on the first exception. The threshold is a measured count-only compromise: expensive bodies benefit from parallelism well before 128, while cheap bodies can favor sequential execution even at 16,384. It is not a universal crossover or a body-cost optimizer. JavaScript keeps its existing sequential realization.

## Layout and value semantics

Copeland's layout pressure is deliberate. In numerical and bulk-data kernels, prefer homogeneous scalar arrays or `record table` columns and operate directly on columns. Table rows are views over column storage; table/query compilation should not force row objects into column hot loops. A row array such as `MutableArray<Sample>` is convenient for value-oriented workloads, but is not a structure-of-arrays layout. CLR containers holding CLR objects add container and object costs; generic arguments without a CLR mapping remain unsupported. Measure the workload before choosing that representation.

Records are immutable values, not JavaScript property bags. Closed inferred object values and contextual nominal record construction retain their existing distinctions. Use `with` to update a record. `type` remains an erased alias/shape, `interface` a capability constraint, and `record` nominal runtime identity. Payload enums remain algebraic; cached payload-less cases do not expose reference identity as a source feature.

Option-oriented `??` and supported `?.` forms retain their existing laws. `null` and `undefined` receive Option construction guidance; they acquire no runtime representation. `any` receives concrete-type, nominal-record, or constrained-generic guidance. Explicit closure captures remain required. No truthiness, coercion, prototypes, hoisting, `var`, `eval`, or dynamic object-as-map behavior was added.

See [the M26 evidence report](../../milestones/copeland-typescript-instinct-corpus-m26-report.md) for qualification boundaries and measured results.
