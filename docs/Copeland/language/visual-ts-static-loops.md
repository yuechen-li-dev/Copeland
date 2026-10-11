# Visual TypeScript static loops and closed value evaluation

## Status

Finite `static for` is implemented in both the compute and graphics binders. Expansion
uses the ordinary shader binder, the existing generic value environment, and Copeland's
bounded static evaluator. No runtime interpreter, shader text substitution, or separate
constant evaluator is involved.

## Iteration spelling

Use `for (const value of values)` to iterate values. JavaScript/TypeScript `for ... in`
iterates enumerable property keys. Copeland supports ordinary indexed `for` and
`for ... of` on its CPU path. Visual TS graphics supports its existing bounded indexed
`for`; ordinary compute loops and runtime GPU `for ... of` remain deferred. Neither
this change nor `static for` introduces GPU `while` or `goto`.

```ts
var sum: u32 = 0;
static for (let offset of [1, 2, 3]) {
    const square: u32 = static Square(offset);
    static if (offset == 1) {
        sum = sum + square;
    } else {
        sum = sum + offset;
    }
}
```

Both `let` and `const` iteration bindings are immutable. Every iteration receives a
separate lexical block. The iteration variable is substituted with its certified
value before body binding: static conditions select only the chosen branch, closed
function calls can fold, and no loop, iterator, or counter is emitted into HLSL.
The body may still contain runtime resource reads, mutable accumulators, and dynamic
branches. `static for` compiles the schedule; it does not make runtime inputs constant.

## Admitted inputs and limits

- Homogeneous finite lists of `u32`, `f32`, or `bool`, including empty lists.
- Pure, closed helpers returning a fixed `Array<T, N>` of those scalars.
- Named module `const` fixed arrays, including imported constants.
- Nested expansion, up to 256 values per loop, 4096 expanded iterations per module,
  and 16 active expansion levels. The existing static evaluation budgets also apply.
- Fresh iteration names; iterator writes, runtime-dependent lists, mixed scalar
  types, unsupported shapes, and budget overruns produce diagnostics.

Expansion currently requires closed lists. Lists whose contents depend on an open
generic static parameter are diagnosed rather than deferred to specialization.
Arbitrary runtime locals are not promoted into static facts automatically. Put a
closed calculation in a pure helper and explicitly request `static` evaluation.
Runtime helper and module-constant binding reset the static environment, so neither
can accidentally capture the caller's iteration variable.

## Payload enums and FLOW

The GPU static adapter now accepts the existing certified enum carrier and payload
layouts as finite value records. This allows the ordinary static evaluator to run
closed payload matches and FLOW helpers, with the same bounded semantics as ordinary
pure functions. Static enum results can be embedded through those same layouts.

```ts
// Replay is a pure helper with fixed inputs and explicit FLOW steps.
// The complete closed calculation is evaluated before HLSL emission.
Output[0] = Convert<f32>(static Replay());
```

The current native specimen emits `Output[0] = float(66);`, encoding flat state
positions. The earlier stack-depth specimen returned 34. The follow-up
[static FLOW composition](visual-ts-flow-tape.md) replaces GPU nested activation
storage with a flat graph and qualifies runtime-input nested stepping and local
checkpoint replay within the unchanged DXC budget. Persistent frame-buffer
restoration remains unqualified.

## Engine use and evidence

Bloom's nine-tap tent kernel uses static offset lists and compile-time weights. Its
runtime normalization accumulator is replaced by the exact weight sum, 16. Ambient
denoising expands its nine offsets before DXC while retaining runtime geometry and
depth weights. Long ray/sample loops retain runtime iteration to avoid large code
expansion.

The shader tests cover expansion, branch elimination, helper reachability, closed
array results, module constants, payload enum embedding, closed child FLOW, iterator
mutation, runtime rejection, empty lists, scope isolation, and expansion budgets.
The existing Vulkan FLOW proof also runs the five-step dynamic child through static
schedule expansion and checks the closed nested positional replay against 66 on the device.
These checks establish semantic preservation and frontend work elimination; they
do not establish a general GPU frame-time speedup.
