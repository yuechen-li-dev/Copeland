# Visual TypeScript and Copeland TS generics

The GPU and host profiles share one declaration syntax and parameter model.
Each profile binds an open body once and specializes its typed plan into its
own closed IR. Backend emitters receive closed functions and storage types.

## Two spellings, one meaning

```ts
function Identity<T>(value: T): T { return value; }

// Equivalent declaration spelling:
template<type T>
function Identity(value: T): T { return value; }

record Box<T> { value: T; }
type Boxes<T> = Box<T>[]; // Host arrays retain ordinary Copeland semantics.
interface HasValue<T> { value: T; }
```

The prefix also applies to `record`, `type`, and `interface` declarations.
Type parameters precede static parameters. A default may reference earlier
parameters. Interfaces describe readable fields and remain constraint-only;
they do not create storage types, objects, methods, or runtime dispatch tables.

```ts
interface HasValue<T> { value: T; }
function Read<T extends HasValue<f32>>(value: T): f32 {
    return value.value;
}
function Forward<T extends HasValue<f32>>(value: T): f32 {
    return Read<T>(value);
}
```

Constraints use the existing `extends A & B` syntax. A generic body sees only
the fields promised by its constraints. A caller's additional fields do not
make an otherwise invalid open body legal. Forwarded requirements are checked
before substitution.

## Static values and fixed shapes

```ts
record Grid<T, static Rows: u32, static Columns: u32> {
    cells: NDArray<T, Rows, Columns>;
}
function Square<static N: u32>(value: Matrix<f32, N, N>): Matrix<f32, N, N> {
    return MatMul(value, value);
}
function Make<T = f32, static Count: u32 = 2>(value: T): Box<T> {
    return { value: value };
}
```

Static arguments are compile-time values. GPU parameters admit `u32`, `f32`,
and `bool`; host parameters admit ordinary numeric types and `boolean`.
Wrong scalar types and runtime values produce diagnostics. Parenthesize
computed arguments and defaults, for example `M: (N + 1)`.

Arguments can be positional, or positional type arguments followed by named
static arguments. Named order does not affect specialization identity.

```ts
let grid: Grid<f32, Rows: 2, Columns: 2> = { cells: [1.0, 2.0, 3.0, 4.0] };
let matrix: Matrix<f32, 2, 2> = [1.0, 2.0, 3.0, 4.0];
let squared: Matrix<f32, 2, 2> = Square(matrix); // Infer N = 2.
```

Inference uses exact argument evidence: scalar types, nominal generic record
arguments, and symbolic shape axes. It does not solve dimension equations,
search overload candidates, or guess from a return type. Conflicting evidence
fails. Explicit calls fill omitted parameters from defaults; they do not mix
partial explicit type arguments with additional inference.

Built-in shape extents remain positional. Named extents belong to authored
declarations such as `Grid`. Ordinary Copeland arrays and host collection types
retain their existing representation; this pass does not add a host tensor
runtime.

## Specialization and execution are separate

Specializing a function preserves its runtime execution. Use `static` to request
compile-time evaluation through the existing bounded evaluator.

```ts
function Samples<static N: u32>(value: f32): Array<f32, N> {
    return [value, 2.0];
}
let samples: Array<f32, 2> = static Samples<N: 2>(4.0);
```

GPU static evaluation now represents closed scalar records and fixed shapes,
then embeds their values through the existing constructors. Resource access,
runtime inputs, and unsupported GPU intrinsics still cannot execute at compile
time. This is representation conversion into Copeland's evaluator, not a second
interpreter.

Construction templates keep their existing explicit `instantiate` phase:

```ts
template<static Seed: f32 = 2.0> Build: Box<f32> {
    return { value: Seed };
}
let box: Box<f32> = instantiate Build<Seed: 3.0>;
```

The GPU construction body uses the admitted shader statement language. Host
artifact templates keep the richer host template language and materializers.
This does not make filesystem or project artifacts GPU values.

## Boundaries and inspection

- At most eight type/static parameters per declaration.
- Function specializations retain 16 per declaration and 128 per compilation
  limits. Generic record storage is bounded too. Recursive expansion and
  excessive depth stop with diagnostics.
- GPU fixed values retain the 256-element limit and existing scalar/layout
  restrictions. Large or dynamic data belongs in resources.
- No partial specialization, SFINAE, runtime interfaces, or constraint methods.
- GPU `let`/`const` remain immutable and `var` mutable. `while` remains rejected.
- GPU scalar static arguments and defaults can call eligible closed helpers.
  Open host static expressions support formal parameters, literals, and
  unary/binary operations; arbitrary open compile-time call graphs are outside
  this slice.

VdMir's `GenericSpecializations` records declaration identity, normalized
arguments, emitted function, call/declaration source locations, and measured
open-body binding count. Repeated inferred/explicit calls share a specialization.
GPU generated names use deterministic full hashes rather than truncated cache keys.
GPU static `if` requires a closed condition at binding; open generic bodies can
use ordinary typed branches. This slice does not add a specialization-dependent
syntax expansion phase.

## Qualification

```powershell
dotnet test Copeland.TS.slnx -c Release -m:1
dotnet build Aurelian.slnx -c Release -m:1
dotnet test Aurelian.slnx -c Release -m:1 --no-build
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --generic-proof --output artifacts/local/vts-generics/native-final
```

`GenericLibrary.v.ts` is imported by compute and graphics witnesses. It combines
generic interfaces, records, aliases, forwarding, inferred matrix dimensions,
dependent defaults, and compile-time aggregate construction. Runtime inputs
`[-4, 0, 1, 4, 16]` must produce `[7, 23, 27, 39, 87]` on two Vulkan dispatches.
The native witness passed on the NVIDIA GeForce RTX 3070. Both graphics stages
and compute pass SPIR-V validation, and direct graphics WGSL passes Naga. WGSL
validation does not claim browser execution.

The host parity witness executes generated C# and JavaScript in Node. Both
produce `24|22|8`, including records, aliases, requirements, forwarding, static
arithmetic, and dependent defaults through aliases. Focused tests also cover argument identity,
invalid constraints, runtime static inputs, recursive aliases, specialization
limits, and lexical ownership of non-generic helpers.

Local evidence and test reports are retained under `artifacts/local/vts-generics`.

Local closeout, 2026-10-09: 1,915 Copeland TS solution tests and 966 Aurelian
tests passed with no failures or skips. Release solution builds passed; the
initial Aurelian build reported 14 existing third-party font warnings. The
generic, preceding language, and fixed-shape native proofs all passed on the
RTX 3070. Reports are in `final-copeland`, `ordered-syntax-core`, and
`final-aurelian`; the final generic shader evidence is in `ordered-syntax-native`.
