# Visual TypeScript fixed records, arrays and tensor values

This pass extends Copeland's GPU profile, used by Aurelian's Visual TypeScript
shaders. It supplies fixed value records, general material packing, shaped
arrays, and a bounded tensor arithmetic profile through the existing parser,
typed VdMir, HLSL/SPIR-V backends, and direct graphics WGSL backend. It does not
add a CPU tensor runtime to ordinary Copeland TypeScript.

The design borrows Oct SDSL-V's separation of storage and tensor operations and
Concept's explicit fixed shape, row-major storage, and rank-one/rank-two tensor
aliases. Tensor axes remain independent of graphics coordinate spaces. Both
repositories were inspected read-only.

## TypeScript-style authoring

```typescript
type Weights = Matrix<f32, 2, 2>;

record Sample {
    value: f32;
    weights: Weights;
}

function Evaluate(value: f32): f32 {
    let original: Sample = { value: value, weights: [value, 2.0, 3.0, 4.0] };
    let sample: Sample = original with { value: value * 2.0 };
    let product: Weights = MatMul(sample.weights, sample.weights);
    let a: Vector<f32, 3> = [value, 2.0, 3.0];
    let b: Tensor<f32, 3> = [4.0, 5.0, 6.0];

    var grid: NDArray<f32, 2, 2> = [0.0, 1.0, 2.0, 3.0];
    grid.set(1, 0, value);
    return product.at(0, 1) + Dot(a, b) + grid.at(1, 0);
}
```

| Type | Meaning |
| --- | --- |
| `Array<T, N>` | Fixed rank-one inline storage |
| `NDArray<T, D0, D1, ...>` | Fixed rank-aware storage in row-major order |
| `Tensor<f32, D0, D1, ...>` | Fixed shape with elementwise arithmetic and explicit products |
| `Vector<f32, N>` | Alias for `Tensor<f32, N>` |
| `Matrix<f32, R, C>` | Alias for `Tensor<f32, R, C>` |

Existing `float2`, `float3`, and `float4` remain native shader vectors.
`Vector<f32, N>` denotes mathematical rank-one storage, including extents beyond
the native vector lane families. It does not silently replace those existing
types. A generic extent is an ordinary numeric literal in type syntax; the GPU
binder owns its meaning. No second shader parser was introduced.

Record fields use exact declared types. Literals initialize every field exactly
once. Constructor arguments retain authored property order while the returned
record retains declaration-order layout. Indexed-write arguments likewise keep
coordinates before the replacement value. Nested record/shaped literals receive type context from declarations,
function parameters and returns. `with` preserves the original and evaluates
its subject once. Plain type aliases and exported/imported records and aliases
use the existing module identities.

## Indexing, shape and mutation

- Rank-one values admit `data[index]` and `data.at(index)`.
- Higher ranks use `grid.at(row, column, ...)`.
- `var` values admit `data[index] = value` and `grid.set(row, column, ..., value)`.
- `let` and `const` values remain immutable. Record updates use `with` or replace
  a whole `var` binding; direct mutable record-field references are not added.
- `.rank` is the number of axes, `.length` the total element count, and `.shape`
  a fixed `Array<u32, Rank>` containing the extents. Subject evaluation is
  preserved even when a query result is known from its type.

Indices must be `u32` and match the rank exactly. A constant out-of-range
coordinate is a diagnostic. A dynamic out-of-range read returns the exact
element zero; a dynamic out-of-range write leaves the value unchanged. Bounds
are checked per axis, so a flattened index cannot accidentally alias a different
row after overflow or an invalid coordinate.

Initializers are flat and contain exactly the product of extents. For shape
`[2, 3]`, `[a, b, c, d, e, f]` represents rows `[a, b, c]` and `[d, e, f]`.
Array elements can be admitted scalar values, native graphics vectors, or
finite value records. Compute retains its existing scalar type restrictions.
Graphics semantic spaces belong to element values, and their physical storage
is retained separately from nominal type identity.

## Tensor math

`+`, `-`, and `*` are elementwise operations on exactly equal tensor shapes.
`Dot` takes two equal rank-one tensors and returns `f32`. `MatMul` takes shapes
`[M, K]` and `[K, N]` and returns `[M, N]`. `*` never guesses whether the caller
intended matrix multiplication. There is no implicit broadcasting, contraction,
or coordinate transformation.

The binder validates these operations before creating closed typed helpers.
Products lower into explicit scalar arithmetic in ordinary VdMir; the generated
shader executes the arithmetic on the GPU. This is not a tensor-core,
cooperative-matrix, BLAS, or large-matrix performance claim.

## Fixed storage and material layouts

`VdMirValueType` retains kind, shape, element type, row-major order, named fields,
offsets, sizes, alignment, and source spans. This first lowering strategy uses
finite register aggregates with generated element fields. It does not promise
native HLSL/WGSL array ABI compatibility. Dynamic access becomes bounded closed
conditional helpers; fixed tensor products become scalar expressions. Larger
datasets continue to use resource buffers. No heap allocation, runtime shape
descriptor, or hidden host interpreter is added.

General materials replace the old list of permitted game-specific field names.
The layout law admits certified scalar/vector/nested fixed values, with scalar
alignment 4, `float2` alignment 8, `float3`/`float4` alignment 16, and aggregate
alignment and tail rounding 16. HLSL uses explicit padding and zero-initialized
aggregate returns; WGSL uses corresponding field alignment and size attributes.
Uniform booleans require explicit `u32` encoding. Resources, enums, recursive
records, and aggregate stage-location expansion remain excluded.

The qualification material has fields `heading`, `parameters`, `gain`, and
`samples` at offsets `0, 16, 64, 80`, total size 96. Its nested parameters use
offsets `0, 16, 32`. Backend tests inspect the SPIR-V decorations, and Naga
validates the matching WGSL declaration.

## Deliberate bounds

- Rank 1–4; literal positive extents at most 256; total elements at most 256.
- Indexed mutation at most 64 inline elements, limiting expanded update helpers.
- Matrix products at most 4,096 scalar products.
- Inline records at most 256 fields, 64 KiB, and 64 dependency levels.
- Tensor arithmetic currently uses `f32`; no dynamic extents, borrowed resource
  views, slicing, arbitrary strides, symbolic Einstein notation, generic
  specialization, or aggregate static evaluation is added.
- `while` remains rejected. The pass introduces no runtime loop requirement.

These are named diagnostics, not fallback allocations or backend source patches.
The bounds keep the first value profile predictable while leaving resource
views and different lowering strategies available as future work.

## Qualification

```powershell
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --shape-proof --output artifacts/local/graphics-starter/vts-shapes
```

The witness shares an imported record/alias/tensor library between graphics and
compute. Its matrix and vector operands contain runtime buffer inputs. For
input `[-4, 0, 1, 4, 16]`, readback must be exactly `[34, 58, 62, 107, 467]` on
two dispatches. This also checks dynamic in-range writes and out-of-range access.
Evidence includes typed graphics/compute IR, HLSL, SPIR-V, WGSL, and device
readback. WGSL is validated by Naga; no browser execution claim is made.

`GpuValueShapeTests` covers both binders, invalid shape/rank/type/index cases,
mutability, recursive types, contextual literals, nominal graphics element
spaces, shape-query evaluation, and tensor incompatibility.
`VisualTypeScriptLanguageTests` covers actual SPIR-V compilation and nested
uniform layout decorations.

Local closeout on 2026-10-09: Release Aurelian solution build passed; all 1,449
Copeland TS tests and 966 Aurelian solution tests passed, with zero failures or
skips. The shape proof and preceding language proof passed on the NVIDIA
GeForce RTX 3070. The existing graphics starter proof also passed with GPU
skinning, shadows, and tone mapping. Test results are retained under
`artifacts/local/graphics-starter/shapes-accepted-core` and
`shapes-accepted-aurelian`; native evidence is under `vts-shapes`,
`shapes-language-regression`, and `shapes-native` in the same artifact directory.
