# R3a: progressive GPU reference captures

`ReferenceIntegrator3D` supplies an executable first reference instrument. It runs
through Aurelian's Vulkan acceleration structures and typed Copeland compute
compiler, shares the actual real-time BRDF, accumulates seeded samples, and exports
linear HDR data. The qualification covers Cornell+, five diffuse models, and the
emission basis of Beacon's existing compiled floor asset. Full R3a remains open.
The [shared affine continuation](rendering-r3a-affine.md) adds hybrid visibility's
sphere/box/capsule intersections, including shear and reflection, while retaining
the original measuring controls.
The [lighting and temporal continuation](rendering-r3a-lighting-temporal.md) adds
directional sun, shared HDR sources, actual native-output FLIP and scripted
temporal comparisons. The initial black-environment measurements below remain
the original bounded controls.

## Run and reproduce

```powershell
./tools/aurelian-rendering-reference/Qualify.ps1

# Capture only, after building the solution:
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --rendering-reference --output artifacts/local/rendering-reference/capture `
  --reference-lighting artifacts/local/beacon-compiled-lighting
```

The qualifier builds `Aurelian.slnx`, runs its full tests and the Copeland TS tests,
then runs the reference, hybrid visibility, and diffuse rendering proofs with
Vulkan synchronization validation. Its ledger records source and executable hashes.
The capture writes `evidence.json`, display PNGs, and little-endian packed RGB
float32 files. Cornell captures are 64×64, floor controls are 24×24, and the
Beacon receiver is 64×64; sample counts are recorded in the ledger. PNGs use
`pow(L / (1 + L), 1 / 2.2)` for inspection; errors use untransformed HDR values.

The Beacon fixture must contain `recipe.json`, `lighting.alight`,
`reference-manifest.json`, `testing.bin`, and `repeat.bin`. The comparison verifies
the scene identity and retained Cycles checksums. See [compiled diffuse
lighting](compiled-diffuse-lighting.md) to acquire this existing fixture. Capturing
the reference itself does not launch Blender or add a native CPU renderer.

## Author a capture

```csharp
var assets = new GameAssets();
var material = new ReferenceMaterial3D(new(.7f), Dielectric: 0);
ReferenceTriangle3D[] triangles = [
    new(new(-2, 0, -2), new(2, 0, 2), new(2, 0, -2), material),
    new(new(-2, 0, -2), new(-2, 0, 2), new(2, 0, 2), material),
];
var camera = new ReferenceCamera3D(new(0, 1, 3), Vector3.Zero, Vector3.UnitY, .8f);
var light = new ReferenceRectLight3D(
    new(-.5f, 2, -.5f), Vector3.UnitX, Vector3.UnitZ, new(4));
using var reference = new ReferenceIntegrator3D(
    plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"),
    new ReferenceCapture3D(64, 64, MaximumBounces: 8, Seed: 123),
    camera, triangles, [], light);
reference.Accumulate(128);
reference.Accumulate(128);
ReferenceImage3D image = reference.Capture();
```

The explicit session is an offline Capture API; the interactive renderer never
creates or schedules it. `Accumulate` submits 1–4096 additional samples without
reading pixels. `Capture` reads the current mean. A session owns immutable scene,
camera, material and seed settings; changing them creates a fresh session. Each
pixel maintains its own count and counter-based integer random sequence. Batch
partitions preserve both sample identity and floating-point addition order.

Triangles can carry additive emission in `ReferenceMaterial3D`. Up to 256 emissive
triangles share an area CDF; a bounded binary search selects a triangle and square
root barycentric sampling selects its point. One-sided emission is the default;
the static diffuse recipe adapter explicitly preserves Cycles' two-sided additive
emission. Emissive spheres reject with `AUR-REF-010` until they have a matching
light sampler. Perspective and orthographic captures are supported; pixel jitter
can be disabled for exact receiver sites.

## Transport and ownership

`VulkanRayQueryScene` remains the BLAS/TLAS, descriptor, command submission and
retirement owner. The resident variant adds immutable capture parameters and an
accumulation buffer to the existing five-binding query ABI. The same
statement-only `RayQueryTraceClosest` intrinsic accepts GPU-written ray packets;
opaque hardware query state still cannot escape as a source-language value or
cross a helper ABI.

One compute invocation owns one pixel and all its bounces, ray/hit packets,
throughput and sum. The host records one dispatch per sample, with compute memory
barriers between samples and whole-batch completion. It performs no bounce
decisions, ray uploads, hit readbacks or per-bounce dispatches. This is an offline
megakernel control, not a wavefront or interactive performance claim.

Ray packets, hit packets and accumulation use `GpuOnly` storage. Initialization
fills them on the GPU. Only an explicit `Capture` copies sums into a temporary
host-readable staging buffer; accumulating more samples does not copy pixels.

In the original area-light controls, each scattering vertex samples an emissive triangle and a cosine-weighted
hemisphere direction. Both techniques use the corresponding solid-angle PDFs and
the power heuristic. Emission reached by continuation receives the complementary
MIS weight. A terminal visibility ray preserves the same finite scattering-depth
definition in the BSDF-only ablation. The method follows the standard estimator
described in [PBRT's better path tracer](https://pbr-book.org/4ed/Light_Transport_I_Surface_Reflection/A_Better_Path_Tracer).

The integrator directly imports `DirectLightResponseModel` from `Lighting3D.v.ts`,
including GGX, Fresnel layering and all five diffuse models. Cosine sampling has
support across the reflective hemisphere and uses the matching PDF; it is an
inefficient proposal for narrow glossy lobes. Current qualification is diffuse.
Materials are constant opaque values, with face-forward geometric normals. This
does not yet share textured material reconstruction, normal maps or coverage with
the visibility resolver.

Captures declare 1–16 scattering vertices, with an additional terminal visibility
query. Omitted lighting gives a black environment; explicit sun/HDR environments
are covered by the continuation above. The ray range is 100,000 metres and the
minimum is 0.1 mm. There is no denoising, adaptive sampling, Russian roulette or radiance
clamping. Finite-depth transport is explicit; more samples do not remove its
truncation error. The proof also measures common-seed 8 versus 16 vertex captures.

## Measured controls

RTX 3070, 64×64 Cornell+, eight scattering vertices, independent 4,096 spp control:

| Samples per pixel | Linear HDR RMSE |
| ---: | ---: |
| 16 | 0.0787851 |
| 64 | 0.0351299 |
| 256 | 0.0218742 |
| 1,024 | 0.00908453 |

At 64 spp, the BSDF-only control has RMSE 0.138267. NEE/MIS reduces error by
3.94× at equal sample count. This compares error, not equal GPU time. A fresh
256-sample session partitioned as 17+239 is byte-identical to the progressive
16+48+128+64 capture. Scene geometry includes ordinary triangles and an analytic
sphere through the existing hardware-query owner.

The host-mapped prototype and final device-only storage path use identical
compiled shader bytes and produce 14 byte-identical HDR files. In sequential
qualification runs, the last 128-sample Cornell batch at the 1,024 spp checkpoint
fell from 187.260 ms to 21.238 ms GPU time. This is an observed local storage
comparison without clock control, not a portable speedup guarantee.

The common-seed 4,096 spp depth control measures RMSE 0.000660849 between eight
and sixteen scattering vertices. This isolates that finite-depth difference;
it does not establish an infinite-bounce error bound.

A separate floor test integrates a rectangular emitter with independent
double-precision area quadrature. It compares 1,024 spp GPU captures for Lambert,
Burley, Frostbite, EON and PathDiffuse; maximum observed linear RGB RMSE is
0.000537 against a declared 0.002 limit.

The GPU furnace integrates the actual shared diffuse response at 80 model,
roughness and view combinations using 128×256 cosine quadrature. It excludes
specular and Fresnel layering. The independent CPU equations use the exact EON
integral rather than the production fit. Observed reflectance ranges:

| Model | Minimum | Maximum |
| --- | ---: | ---: |
| Lambert | 1 | 1 |
| Burley | 0.598654 | 1.463419 |
| Frostbite | 0.215441 | 0.969152 |
| EON | 0.999117 | 1.000153 |
| PathDiffuse | 0.999550 | 1.000485 |

Maximum GPU/CPU discrepancy is 0.000482. Burley's energy gain and Frostbite's
attenuation are observations of the existing models. The reference does not
renormalize these models or reinterpret a white furnace as an exactly-one test
for every authored lobe.

## Existing compiled receiver

`ReferenceDiffuseScene3D` consumes the same `StaticDiffuseLightingRecipe` as the
asset compiler, preserving face albedo, positions, winding and additive emission.
The capture samples Beacon's 64×64 receiver sites with jitter disabled. It uses
the compiler's conservative ground-contact contributor AABB mask: 3,963 sites
remain. The comparison loads the actual `.alight` file, uploads it through
`VulkanSolid3DRenderer`, and evaluates the existing `CompiledDiffuse` GPU decoder.
Its scaled RGBA8 readback has an explicitly recorded linear quantization bound;
the GPU decoder is also checked against the asset's CPU coefficients.

The 4,096 spp reference measures error for the emission-total basis. Results
include both outgoing radiance and response per unit receiver albedo. The latter
has GPU-decoded RMSE 0.00849648 and CPU-coefficient RMSE 0.00847720. Against the
retained independent Cycles samples, the new reference has outgoing-radiance
RMSE 0.000739046. The compiled GPU decoder versus the reference has outgoing-
radiance RMSE 0.0019897. The ledger also records retained repeat noise and fitting error.

The historical asset RMSE combines both lighting bases **after multiplying by
receiver albedo**. It is not directly comparable to emission-only response per
unit albedo. The new ledger keeps these quantities separate. It does not rebake
or modify the asset or change ordinary rendering defaults.

## Remaining R3a work

The initial procedural geometry boundary is closed by the [shared affine
intersection profile](rendering-r3a-affine.md). Raster and reference queries now
use `AnalyticIntersections3D.v.ts`; the reference admits immutable affine sphere,
box and capsule declarations. A general retained-scene material adapter remains
open. Coverage quads are never supplied as reference geometry.

Directional sun, source environments, the sun-indirect compiled receiver basis,
FLIP and bounded static/camera/moving-occluder temporal sequences are now qualified
in the [continuation report](rendering-r3a-lighting-temporal.md). Remaining cases
include textured/coverage materials, efficient glossy sampling and the full
standing scene suite (Forest, alpha foliage/thin geometry, Wet Night and Hall of
256 Lights). `FullR3aComplete` remains false until the promised suite is present.

## Validation

`artifacts/local/rendering-reference/final/qualification.json` accepts the final
device-only implementation: 1,600 Aurelian tests and 1,730 Copeland TS tests pass,
with zero failures or skips. The final build has zero warnings and errors. The
reference, hybrid visibility and ordinary diffuse native proofs all pass without
Vulkan validation or synchronization diagnostics. The qualifier checks that its
recorded sources remain unchanged throughout execution and records the proof
and graphics assembly hashes.

The earlier coarse 64×128 GPU furnace rejected a grazing EON comparison against
the denser CPU oracle. Matching the 128×256 integration grids removed that
quadrature discrepancy; no BRDF or energy normalization was changed. Earlier
attempts and the superseded host-storage run remain under the same artifact root.
