# Visual TypeScript compute values and storage

Compute authoring now uses native vectors, typed packed buffers and ordinary
resource helpers. Cloth and humanoid skinning exercise the production path:
Copeland's parser and GPU binder produce canonical VD-MIR; Aurelian emits HLSL,
then DXC and SPIR-V validation produce Vulkan shaders. No shader-specific parser,
reflection serializer or domain-specific cloth language is added.

## Authoring

```typescript
record Particle {
    position: float3;
    inverseMass: f32;
}

function Advance(readwrite particles: StorageBuffer<Particle>, index: u32,
    velocity: float3, step: f32): void {
    let particle = particles[index];
    particles[index] = particle with {
        position: particle.position + velocity * step,
    };
}
```

`float2`, `float3` and `float4` are native compute values, matching their graphics
spellings. Constructors require exactly the declared number of `f32` lanes.
Matching vectors support componentwise `+`, `-`, `*`, `/`; multiplication by an
`f32` works in either order, and vector division by an `f32` is supported.
`Dot`, `Cross`, `Length` and `Normalize` lower to native shader instructions;
`Cross` requires two `float3` values. Swizzles use the available `xyzw` lanes.
There is no implicit numeric or vector-width conversion. Normalizing a zero
vector retains the shader intrinsic's behavior; domain code must guard it.

Records remain values. `let` and `const` are immutable; `var` permits replacing
the binding, and `with` produces an updated record. A readwrite buffer permits
whole-element assignment and declared field writes, including a single vector
lane. Multi-lane swizzle writes and nested array reference writes are excluded;
replace the value when those updates are needed.

Helpers declare `readonly` or `readwrite` buffer parameters explicitly. The
binder rejects writes through readonly resources, permission escalation,
resource return values, resource-bearing records and local resource aliases.
A readwrite resource can be passed to a readonly helper. HLSL requires distinct
physical RO/RW buffer signatures, so the emitter specializes only reachable
transport contexts, including forwarding calls. Source permissions remain
readonly in canonical IR. This is compile-time lowering, not runtime dispatch.

## Packed storage and host agreement

`StorageBuffer<T>` admits finite `f32`, `u32`, native vectors, records, fixed
arrays and shaped values composed of those types. Storage flattens scalar leaves
in declaration order into contiguous 32-bit words. A `float3` occupies 12 bytes.
Nested arrays use their existing generated `e0`, `e1`, ... field paths.
Empty records, recursive values, booleans, enums and resources cannot occupy
this transport. Encode tags or flags explicitly when crossing the host boundary.

The module's `StorageLayouts` metadata owns byte stride, leaf types and byte
offsets. This packed storage law is separate from uniform/register alignment;
using a uniform layout to pack these buffers would be incorrect. Aggregate
buffers lower to scalar uint storage with bit-preserving float conversion, so
HLSL struct padding cannot silently change the host ABI. `u32` values retain all
32 bits; they are not converted numerically through float.

`GpuStoragePacking.Write` packs a named scalar leaf into a host byte span with
type checking. `GpuStoragePacking.EmitCSharp` generates ordinary C# stride and
field-writing methods from the same metadata. Invalid C# identifiers and
colliding generated method names are rejected. The generated source is compiled
in tests. The existing cloth/skinning host transports remain compatible, with
their strides asserted against compiler metadata:

| Value | Bytes |
| --- | ---: |
| ClothHeader | 128 |
| ClothParticle | 64 |
| ClothAnchor | 16 |
| ClothStencil | 48 |
| RestVertex | 96 |
| DualQuaternion | 32 |
| PoseHeader | 76 |
| SolidVertex | 40 |

Skinning joint indices intentionally retain their existing numeric `f32`
encoding. Choosing a `u32` field would change that transport's bit interpretation.

Aggregate/vector buffer indexing checks bounds before multiplying the index by
the word stride. Reads return zero and writes do nothing outside a complete
element. Explicit packed transfers support heterogeneous existing buffers:

```typescript
let particle = Load<ClothParticle>(state, vertex * WordCount<ClothParticle>());
Store<ClothParticle>(state, vertex * WordCount<ClothParticle>(), particle with {
    velocity: particle.velocity + gravity * step,
});
```

`Load<T>` and statement-only `Store<T>` take **word offsets**, while layout
metadata and C# packing take **byte offsets**. `WordCount<T>()` is a compile-time
constant. Transfers accept bound buffers with either float or uint backing and
preserve bits. Their checks avoid overflow and require the full transferred value
to fit. Legacy direct scalar `StorageBuffer<f32/u32>[index]` access retains its
low-level behavior; callers must bound it, or use checked explicit transfers.

## Finite iteration and exhaustive dispatch

```typescript
enum Action { Move(delta: float3), Stop }

match action {
    case Action.Move(move) => {
        Advance(particles, index, move.delta, step);
    }
    case Action.Stop => { return; }
}

static for (let influence of Indices<6>()) {
    // The existing static evaluator expands this body before HLSL emission.
}
```

Statement `match` and `switch` accept block arms and optional `case` prefixes.
Each enum variant appears exactly once. Payload arms bind one immutable record.
The subject evaluates once; mutation stays in the enclosing scope, and `return`
returns from the enclosing function. Lowering uses a flat switch, avoiding
dynamic nesting or synthetic result stacks. Its last certified enum case becomes
the default, allowing DXC to prove exhaustive return without invented values.
Only payload-free enums support `==` and `!=`; payloads are never silently ignored.

Expression match remains available with expression arms. It still excludes
opaque resource captures; use statement arms for operations on buffers. Block
arms are currently a **Visual TS compute capability**. Host Copeland reports
`COPE-MATCH-0008` and graphics reports an unsupported statement, instead of
silently ignoring the new syntax. Host and graphics expression match are unchanged.

`Indices<N>()` currently requires a literal extent from 0 through 256, with no
runtime arguments. It reuses the existing static-for evaluator and its 256-value
loop, 4096-iteration module and 16-level expansion budgets. It creates no runtime
array. Symbolic generic extents and runtime iteration are not introduced here.

## Production qualification

The real cloth shader now has typed headers, particles, stencils and anchors,
native vector arithmetic, exhaustive phase dispatch and named solver helpers.
It preserves the explicit solve tape and contact model. Skinning has typed rest
vertices and dual-quaternion palettes; six influences use one static-for body.
Its anatomical-unit conversion and solid-vertex output ABI are unchanged.

Run the native regression with the saved scalar source pair:

```powershell
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --compute-refactor `
  --baseline-dir artifacts/local/vts-compute-values `
  --output artifacts/local/vts-compute-values/qualification
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --cloth-systems `
  --output artifacts/local/vts-compute-values/cloth-final
```

The local saved baselines are inputs, not an installed dependency. A missing
baseline is an explicit file error. The proof renames baseline vector helpers
that now conflict with compiler intrinsics; it preserves their arithmetic and
records hashes of the original sources. Source hashes, canonical IR, HLSL,
SPIR-V, generated packing source, numerical errors and paired timings are saved.
Evidence starts rejected and becomes accepted only after actual native checks.

The storage witness verifies complete-element and field writes, mixed word
transfers, exact high-bit integer transport, overflowing loads/stores and a
side-effecting match subject. Skinning compares 30,000 vertices over 12 poses,
six unequal influences, antipodal quaternion signs, corrective shapes and rigid
world transforms, plus an independent identity/corrective oracle. Cloth compares
289 vertices over 24 ticks against the saved GPU shader and the CPU solver,
then checks exact restore/replay. The separate cloth systems proof covers offset
contact, driven pins, sphere contact, bending and drape regressions.

Paired timings use five warmups and 32 measured samples with alternating order.
They include upload, command recording, submission and completion, and exclude
readback/restore. These are local end-to-end timings, not GPU timestamp measures
or guarantees across devices. Bounds checks and expanded typed helpers increase
generated shader size; source reduction does not imply smaller SPIR-V. The
emitter excludes unused storage-load overloads, but further bounds-hoisting or
shader-size optimization remains separate work. Browser compute is unqualified.

### Qualified local results, 2026-10-10

Both native proofs passed on NVIDIA GeForce RTX 3070. Cloth matched the saved
GPU shader's positions exactly; maximum CPU disagreement was 1.802 micrometres,
with exact position/velocity rewind replay. Skinning's maximum position difference
was 0.337 micrometres and its normals/colors matched exactly. The independent
identity oracle and storage transport checks passed.

| Workload | Baseline median / p95 ms | Refactored median / p95 ms |
| --- | ---: | ---: |
| 30,000-vertex skinning | 2.9520 / 3.1785 | 2.5205 / 2.5667 |
| 289-vertex cloth step | 2.8355 / 2.9715 | 2.6149 / 2.7902 |

Generated SPIR-V grew from 391,264 to 620,360 bytes for cloth and from 15,096 to
18,256 bytes for skinning. The measured compile/validate/disassemble run took
3.224 / 4.689 seconds for baseline/refactored cloth and 0.114 / 0.225 seconds for
skinning. These single compilation timings include compiler/tool startup and
are descriptive, not a controlled throughput benchmark. Skinning authored source
shrank from 8,111 to 3,867 bytes. The explicit bounds/access guarantees cost
generated code even though the local dispatch timings improved.

Release `Aurelian.slnx` built with zero warnings/errors. Full Copeland compiler
tests passed 1,495 cases and CLI tests passed 80. Full Aurelian solution testing
passed 1,210 cases; the final shader-suite rerun passed all 278 cases, including
six additional negative cases added after that full run. The existing graphics
WGSL validation used the repository's Naga executable via `AURELIAN_NAGA`.
No tool was downloaded. `git diff --check` passed.

Evidence and exact logs are under `artifacts/local/vts-compute-values` and
`artifacts/local/cloth-research-checks/values-*`. The baseline sources and evidence
are local qualification inputs/artifacts; they are not required to run games.
