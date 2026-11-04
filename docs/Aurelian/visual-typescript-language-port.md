# Visual TypeScript language foundation

Visual TypeScript shares Copeland's parser and uses its richer value, enum,
static-evaluation and generic syntax through a bounded GPU profile. Both GPU
binders certify that profile before backend emission; sharing the parser does
not automatically admit every host-language construct.

The current binding vocabulary is `const` for compile-time values, `let` for
runtime immutable values, and `var` for mutable locals. The shader refactor and
native equivalence qualification are described in
[shader authoring refactor](shader-authoring-refactor.md).

## Ownership and reuse

Concept's CPU lowering supplied the architectural ideas: resolve declaration
identity before backend emission, and erase selected static operations before
runtime validation. Concept was inspected read-only. The implementation reuses
Copeland's existing parser, import/export facts, bounded `StaticEvaluator`, and
effect classifier. It does not introduce another parser or evaluator.

Both GPU binders produce typed VdMir. Aurelian's HLSL backends and Copeland's
direct graphics WGSL backend consume that IR, including explicit enum metadata;
they do not reinterpret source expressions.

## Modules

```typescript
import { Shade as Evaluate } from "./Lighting";

export function Adjust(value: f32): f32 {
    return Evaluate(value);
}
```

Named relative imports support aliases, private helper closure, transitive
dependencies, and exported functions, enums, constants, and admitted shader
types. Extensionless paths resolve against `.v.ts` and `.ts`; specifying `.ts`
or `.v.ts` resolves exactly. Missing or ambiguous targets, private imports,
alias collisions, duplicate normalized paths, malformed import shapes, and
cycles produce diagnostics. Default, namespace, side-effect, package, and
re-export imports are outside this pass.

The binder consumes a supplied source snapshot. `GpuSourceLoader.Load` walks
declared imports through a caller-owned reader; `GameAssets` uses its embedded
shader assets as that reader. There is no special Lighting3D injection anymore.
Existing source bags with no import/export syntax retain their legacy global
mode. Module mode gives private declarations stable identities. A sole entry
module preserves authored entry and resource names for existing game contracts.
The prefixes `Vts` and `__vts_` are reserved for generated declarations.

Function bodies are certified along reachable calls. Type declarations in the
supplied snapshot are registered eagerly; this pass does not claim arbitrary
unreachable host declarations can be supplied without diagnostics.

## Bindings and static operations

`else if` is supported. Five or more conditional branches in one chain produce
the shared, nonblocking `COPE-STYLE-0001` authoring warning recommending
`switch`/`match`. A final `else` does not count. Both graphics and compute modules
carry warnings separately from legality diagnostics; see
[conditional authoring](../Copeland/language/overview.md#conditional-authoring).

Visual TypeScript uses these bindings in both graphics and compute:

| Binding | Meaning | Reassignment |
| --- | --- | --- |
| `const` | A value evaluated by the compiler's bounded static evaluator | Rejected |
| `let` | An immutable runtime value | Rejected |
| `var` | A mutable runtime local | Allowed |

```typescript
const pi: f32 = 3.14159265;
const taps: u32 = 8 * 2;
let gain: f32 = input.value;
var total: f32 = 0.0;
total = total + gain;
```

This follows the conceptual distinction between Rust `const`, `let` and
`let mut`. A `const` initializer must be certified for compile-time evaluation;
shader inputs, uniforms, resource reads and runtime `let`/`var` bindings are not
eligible. `COPE-GPU-CONST-0001` explains failures and recommends `let` for runtime
immutable values. Dependent constants and closed pure helpers use the existing
static evaluator. Closed vector constructors and component reads are admitted,
alongside finite scalar values and the existing finite records/value shapes.
Other GPU intrinsics and unimplemented static operations still require runtime
bindings. An explicit `static` expression remains valid and useful.

Resource contents still follow their separate `readonly`/`readwrite` contract;
an immutable pixel index does not prevent writing a readwrite output buffer.
Only `var` opts into local mutation. This policy also holds inside helpers
evaluated at compile time. GPU flow lowering emits runtime immutable temporaries
as `let`. Ordinary Copeland TypeScript binding semantics remain unchanged.
Module constants must use `const` and evaluate without runtime inputs.

```typescript
function Square(value: f32): f32 {
    let factor: f32 = value;
    var result: f32 = 0.0;
    result = factor * factor;
    return result;
}

function Gain(): f32 {
    static if (true) {
        return static Square(0.5);
    } else {
        return unavailableHostOperation();
    }
}
```

Static scalar expressions and closed pure helper calls evaluate through the
ordinary bounded evaluator. Results embed finite `f32`, `u32`, or `bool`
literals, closed vector constructors, or the existing certified aggregate values. Arithmetic retains binary32 rounding and unsigned wraparound.
Static-only helpers leave the runtime function closure. `static if` selects its
branch before GPU binding, so an unselected host-only branch is erased.

Runtime parameters, resources, GPU intrinsics and runtime local bindings cannot
be captured into a static expression. Compile-time globals and local `const`
values can be referenced. Runtime-style local variables inside a closed pure
helper are evaluated as part of that helper's compile-time call. The adapter admits the existing
bounded graphics `for` shape inside static helpers; static loop generation,
vector/array static values, templates, reflection, and enum-valued static
operations are deferred. Budget, effect, recursion, and nonfinite-result
failures remain diagnostics.

## Payload enums and Oct-style match

```typescript
export enum SampleValue {
    Missing,
    Present(value: f32)
}

export function Shade(value: f32): f32 {
    let sample: SampleValue = SampleValue.Present(value);
    let gain: f32 = static Square(0.5);
    return match sample {
        SampleValue.Missing => 0.0,
        SampleValue.Present(payload) => payload.value * gain,
    };
}
```

Arms must use `Enum.Case`, match exactly once per case, and agree on result
type. A payload case binds one record whose fields retain their declared
types. Bindings remain inside their arm. The subject evaluates once and only
the selected arm executes.

Typed lowering uses a tag and fully initialized case fields, constructor and
payload helpers, and ordinary conditional IR. Enums are local values and
helper arguments/returns, with 1–64 finite cases. Graphics payloads admit scalar
and floating vector values; compute payloads are restricted by its existing
type surface. Nested sums and enums in storage, material, resource, or stage
interfaces are deferred. Read storage values before matching: storage buffers
and acceleration structures cannot be captured into the generated helper ABI.

Both graphics and compute support exhaustive enum match expressions. Compute
also supports statement `match` with block arms, as used by the dynamic GI phase
dispatch. Statement match has not been added to the graphics binder. Numeric
buffer/parameter ABI tags should be decoded to typed enums at their boundary;
missing or duplicate enum cases are compiler errors.

`while` remains rejected. This pass adds no `break` or `continue` support;
existing graphics compatibility support for `break` is unchanged.

## Verification

`GpuLanguagePortTests` checks module identity and visibility, deterministic
source ordering, static erasure, numeric behavior, payload diagnostics, direct
WGSL lowering, and local mutability in runtime and static helpers.
`VisualTypeScriptLanguageTests` compiles the graphics and compute witnesses to
validated SPIR-V and checks deterministic compute artifact hashes.

The native qualification command is:

```powershell
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --language-proof --output artifacts/local/graphics-starter/vts-language
```

It compiles shared imported enum/static helpers for graphics and compute,
validates SPIR-V, validates direct WGSL using Naga, and dispatches the compute
witness through Vulkan. Input `[-4, 0, 1, 4, 16]` must read back exactly
`[0, 0, 0.25, 1, 4]` on two dispatches before evidence is marked accepted.
`AURELIAN_NAGA` selects an existing Naga executable; the default points to the
repository's previously qualified toolchain. No tool is downloaded.

The scalar Vulkan probe is a qualification utility with exactly two scalar
storage buffers; production compute runners keep their existing contracts.
Naga validation is shader validation, not browser/WebGPU execution evidence.
The subsequent [fixed values pass](visual-typescript-fixed-values.md) adds
bounded value records, material layouts, arrays and tensor operations. Closed
generics are covered by the subsequent [generics pass](visual-typescript-generics.md).
Further intrinsic catalogue expansion remains separate work.

The [compute values and storage pass](visual-typescript-compute-values.md)
adds native compute vectors, typed packed buffers, qualified resource helpers,
block-arm exhaustive dispatch and finite `Indices<N>()` static iteration.
Expression matches retain their resource-capture boundary; compute statement
matches operate directly in the enclosing resource scope.

## Qualified results, 2026-10-09

- Release `Aurelian.slnx` build: zero warnings and errors.
- Full Aurelian solution tests: 964 passed, zero failed or skipped. The existing
  WGSL translation test used the repository's Naga executable via `AURELIAN_NAGA`.
- Full Copeland TypeScript tests: 1,431 passed, zero failed or skipped.
- Native language proof: exact scalar readback and identical repeat dispatch on
  NVIDIA GeForce RTX 3070; compute/graphics SPIR-V and direct graphics WGSL validated.
- Production graphics starter proof: shadows, materials, tone mapping, and
  GPU-skinned humanoid passed on the same device.
- Native gallery smoke: four frames and presentation controls passed.
- Beacon3D native proof: 3,681 frames, three pickups collected, win reached.

Evidence is retained under `artifacts/local/graphics-starter`: `vts-final-core`,
`vts-final-aurelian`, `vts-language`, `vts-port-native`, and `vts-beacon`.
