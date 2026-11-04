# Shared affine analytic intersections

Hybrid visibility and reference path tracing now import the same sphere, box and
capsule intersection functions. Hardware ray queries visit conservative world
AABBs; a typed shader callback evaluates the actual transformed surface. The
reference does not substitute tessellation or coverage quads for these surfaces.

## Authoring and execution

```csharp
Matrix4x4 shear = Matrix4x4.Identity;
shear.M21 = .5f;
var capsule = new ReferenceAnalytic3D(
    AnalyticPrimitiveKind3D.Capsule,
    Matrix4x4.CreateScale(-.5f, .7f, .35f) * shear
        * Matrix4x4.CreateTranslation(.7f, .7f, -.35f),
    new ReferenceMaterial3D(new(.06f, .15f, .7f), Dielectric: 0));
using var capture = new ReferenceIntegrator3D(
    plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"),
    settings, camera, triangles, [], light, [capsule]);
capture.Accumulate(128);
ReferenceImage3D hdr = capture.Capture();
```

Canonical kinds remain sphere radius 1, box `[-1,1]³`, and capsule radius .5 about
the Y segment `[-.5,.5]`. `ReferenceSphere3D` remains a convenience API and now
lowers into this same affine path. Materials remain constant and opaque. Analytic
emission rejects with `AUR-REF-010` until it has a matching light sampler.

For host-authored diagnostic ray batches, use
`VulkanRayQueryScene.CreateAnalytic` with `AffineRayQuery3D.v.ts` and immutable
`RayQueryAnalytic3D` objects. Finite, affine, invertible transforms are required;
invalid transforms reject with `AUR-RAY-ANALYTIC-001`. The owner precomputes inverse
matrices and outward-rounded world AABBs once. Ray packets, path state and sums
remain device-only in reference captures. No CPU bounce loop, per-bounce dispatch
or new native CPU rendering dependency is introduced.

`RayQueryRequest.MinimumDistance` is explicit. Packets retain primitive identity,
face-forward unit normals, world ray distance, layers and masks.
`RayQueryHit.FrontFace` exposes the existing packet's facing bit. The legacy
six-argument/world-sphere ABI remains available.

## Shared shader and compiler boundary

`AnalyticIntersections3D.v.ts` owns canonical root selection. Rasterization and
`AffineAnalyticQuery3D.v.ts` import it. `AnalyticGeometry3D.v.ts` retains raster
camera reconstruction, UV gradients and attributes.

`IntersectAnalyticRange` accepts a local minimum distance. Sphere and capsule
roots are filtered against it; box slabs select the exit when the entry is
clipped. Rasterization calls the zero-minimum wrapper. Secondary queries use the
same function with their nonzero minimum.

If the world-to-local direction has length `s`, the callback normalizes it,
multiplies the world minimum by `s`, and divides the local hit distance by `s`.
Normals use the inverse transpose and are renormalized. This preserves world
distance units and normal geometry under nonuniform scale and shear; see
[PBRT's transformation derivation](https://pbr-book.org/4ed/Geometry_and_Transformations/Applying_Transformations).

The typed command has one optional closed symbol operand:

```typescript
RayQueryTraceClosest(Scene, Procedurals, Rays, Hits, Triangles, thread.x,
    AffineAnalyticIntersection);
```

This is the `compute.rayquery.m3` VD-MIR profile. The named function is compiled
statically, without device function pointers or general function values. Its
parameters must be readonly float storage, a primitive index, world origin, unit
world direction and minimum distance. Its result record contains `normal:
float3`, `distance: f32`, `layer: u32` and `mask: u32`; negative distance denotes
a miss. Invalid contracts reject with `COPE-GPU-RAYQUERY-0004`.

The callback cannot access opaque hardware traversal state. Existing helper
access checks, recursion checks and barrier restrictions still apply.
Reachability and physical resource specialization follow its ordinary helpers.
Legacy six-argument commands retain `compute.rayquery.m2`. Both profiles compile
to validated ray-query SPIR-V.

The affine procedural record has 16 words: three inverse affine float4 rows,
then kind/layer/mask/reserved uint bits. `VulkanRayQueryScene` remains the owner
of acceleration structures, packets, submission and retirement.

## Reflections and normal maps

The first executable case exposed `AUR-MESH-011`: retained instances rejected
negative determinants for every mesh. Analytic instances now accept reflections.
Triangle instances retain their positive-orientation requirement because their
winding contract has not changed.

Analytic tangent handedness also accounts for the determinant. The proof bakes
reflected triangle positions, normals, tangents and winding into an independent
orientation-preserving control mesh. At 96 slices, its normal-mapped result
differs from analytic rendering by **0.01951/255 mean RGB**. Deliberately wrong
handedness gives **7.00306/255**. That is an ablation, not a claimed historical
rendering of a previously admitted reflection.

## Native qualification

```powershell
./tools/aurelian-rendering-reference/Qualify.ps1 `
  -Output artifacts/local/rendering-affine/final

# Focused native experiment after building:
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --rendering-affine --output artifacts/local/rendering-affine/experiment
```

The qualifier runs full Aurelian and Copeland TS tests, this affine proof, the
existing reference/floor/furnace proof, hybrid visibility and diffuse rendering
regressions. It rejects Vulkan validation/synchronization diagnostics and source
changes during execution. `qualification.json` records source and assembly hashes.
`affine/` contains scene declarations, metrics, PNGs and RGB32F captures. The
previous R3a qualification remains at its original artifact path.

Final validation passes **1,609 Aurelian tests and 1,730 Copeland TS tests**,
with zero failures or skips. The build has zero warnings/errors. All four native
proofs pass with clean Vulkan synchronization validation. All 35 recorded source
hashes and both recorded assembly hashes match the qualified checkout.

RTX 3070 observations:

| Check | Result |
| --- | --- |
| Independent double-precision oracle | 7,560 rays: 7,148 hits and 412 misses |
| Maximum world distance error | 0.000005725 m |
| Maximum unit normal vector error | 0.00005469 |
| Raster/query interior correspondence | 47,638 hits over eight camera cases |
| Maximum D32 difference | 0.0000002385 |
| Seeded 31+225 versus progressive 256 spp | Identical float32 RGB |
| Sphere entry clipped at 2.01 m | Exit selected at 4 m |

The oracle independently inverts matrices in double precision, solves quadratic
roots and tests box face planes. It shares no production intersection code. Fixed
distance and normal gates are 0.0003; the D32 gate is 0.00003. Cases include
nonuniform scale, shear, negative determinants, parallel slabs, on-surface/inside
origins and clipped entry roots. Raster views cover front/back,
perspective/orthographic, inside and near-plane cases. Boundary pixels are
counted separately; the direct ray suite excluded none. Layer/mask exclusions,
stable duplicate order and triangle-versus-analytic ties pass. Legacy sphere
packets remain equivalent on the compatibility control.

The mixed reference scene contains a red affine sphere, a sheared green box and a
reflected blue capsule. Eight-vertex transport uses an independent 4,096 spp
control:

| Samples per pixel | Linear HDR RMSE |
| ---: | ---: |
| 16 | 0.0273601 |
| 64 | 0.0136981 |
| 256 | 0.00694909 |
| 1,024 | 0.00382950 |

A separate one-vertex capture puts the camera below blockers and looks down at
the unchanged floor. Central receiver energy falls to 0 for sphere/box blockers
and 0.06631 of the unblocked control for the capsule, proving actual secondary
visibility through the path tracer.

These are bounded single-precision workloads, not guarantees for arbitrary
conditioning or scene magnitudes. Analytic emitter sampling, analytic MASK/CSG,
native analytic shadow maps and reference material textures remain separate
boundaries. The [lighting continuation](rendering-r3a-lighting-temporal.md) now
qualifies sun/environment transport, FLIP and bounded temporal sequences. The
remaining full scene suite keeps R3a open.
