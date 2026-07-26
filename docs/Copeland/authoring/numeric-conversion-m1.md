# Numeric conversion and canonical formatting

Copeland has two canonical numeric types: `int` is a signed 32-bit whole number and `float` is an IEEE-754 binary64 floating-point value. `number` is the TypeScript-compatible spelling for `float`; it has the same semantic compatibility, backend representation, arithmetic, and formatting behavior.

```ts
const count: int = 3;
const ratio: float = 2.5;
const legacy: number = 4.0;
```

Integer-looking literals infer `int`; literals with a decimal point infer `float`. An integer literal may be adapted where a known `float`/`number` destination is required. Stored values never widen implicitly. Arithmetic is homogeneous: `int + int` produces `int`, and `float + float` (including `number`) produces `float`. Mixed stored `int`/`float` arithmetic is rejected; write `Float.From(count) + ratio` instead.

`int` maps to `System.Int32` on CLR and a checked integer-valued JavaScript `number`; `float`/`number` map to `System.Double` and JavaScript `number`. `Int.Floor`, `Int.Ceil`, `Int.Truncate`, and `Int.Round` reject non-finite or out-of-range results on JavaScript and use checked CLR casts. `Int.Round` uses half-away-from-zero: `2.5 -> 3`, `3.5 -> 4`, `-2.5 -> -3`, and `-3.5 -> -4`.

Conversions are destination-owned:

```ts
const widened: float = Float.From(count);
const alsoWidened: float = Float(count);
const label: string = String.From(count);
const familiar: string = String(count);

const floor: int = Int.Floor(ratio);
const ceiling: int = Int.Ceil(ratio);
const rounded: int = Int.Round(ratio);
const truncated: int = Int.Truncate(ratio);
```

`Float.From` accepts `int` and floating values only. Text parsing is deliberately deferred; `Float("3")` is rejected rather than behaving like JavaScript coercion. There is no ambiguous `Int(value)` conversion: float-to-int conversion must name its rounding policy.

`String.From` has invariant canonical formatting for strings, booleans, ints, and floats. Booleans format as lowercase `true` and `false`. String interpolation applies exactly this same conversion law and evaluates embedded expressions left-to-right once:

```ts
const summary = `${count} items at ratio ${ratio}`;
```

Keep `+` strict. `string + string` is valid; string/numeric concatenation is rejected. Use `String.From(value)`, `String(value)`, or interpolation instead. Localized presentation formatting and `Int.Parse`/`Float.Parse` are intentionally deferred.
