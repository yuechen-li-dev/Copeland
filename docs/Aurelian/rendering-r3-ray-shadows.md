# R3: retained ray shadows and exact local-light baseline

The ordinary `VulkanSolid3DRenderer` can evaluate up to 256 point/spot lights,
including a GPU occlusion ray for each contributing shadowed light. The pass is
part of the compiled frame tape, between surface geometry and surface lighting.
There is no CPU ray generation, per-ray readback, or separate shadow submission.
This is the first R3 implementation gate. Full R3 remains open.

## Configure the normal rendering path

Supply `LocalShadows3D.v.ts` in `SurfaceLightingPrograms.LocalRayShadows` when
constructing the renderer. The Vulkan plant must enable hardware ray queries.
The remaining surface, retained mesh and visibility programs are the same ones
used for R2. `RenderingBindlessProof.CreateRenderer(..., rayShadows: true)` is a
complete executable configuration in the graphics proof tool.

```csharp
renderer.EnableRayShadows = true;
renderer.EnableVisibilityBuffer = true; // Required when the snapshot has analytic primitives.
renderer.Settings = Graphics3DSettings.Default with
{
    Shadows = false, // The existing directional map path is independent.
    LocalShadowBudget = 256,
    AntiAliasing = AntiAliasing3D.None,
    BloomIntensity = 0,
};
renderer.LocalLights =
[
    new LocalLight3D(new(0, 3, 0), Vector3.One, 5, 6)
    {
        CastShadows = true,
    },
    new LocalLight3D(new(2, 3, 0), new(.7f, .8f, 1), 5, 6)
    {
        Kind = LocalLightKind.Spot,
        Direction = -Vector3.UnitY,
        CastShadows = true,
    },
];
var snapshot = new NativeInstanceSet3D(instances);
var scene = new Native3DScene([], []) { Instances = snapshot };
renderer.Render(scene, worldToClip, eye, new(0, 0, 0, 1));
```

Reuse the immutable snapshot for steady frames. Replace it when instances or
ray masks change. Ordinary light-table edits do not rebuild acceleration.
`LocalShadowBudget` admits 0..256 shadowed lights; its default remains two.
The map fallback still admits two spot shadows, and rejects point shadows with
`AUR-LIGHT-LOCAL-002`. It does not silently remove requested shadows.

The first ray profile admits one opaque retained snapshot. It rejects mixed
immediate/GPU-generated geometry (`AUR-RAY-SHADOW-002`) and MASK, blended or
transmitting instances (`AUR-RAY-SCENE-002`). An analytic coverage quad never
becomes an occluder. Directional shadow maps retain their existing analytic
restriction; ray-traced sun visibility has not been added in this gate.

## Shared scene authority and acceleration

`NativeInstanceSet3D.PackedInstances` owns the packed order used by raster
material IDs and TLAS custom indices. Authoring enumeration retains its source
order. `SceneInstance3D.RayLayer` and `RayMask` are immutable snapshot inputs.
The admitted lighting query uses all layer and mask bits; zero excludes a caster.

`VulkanSceneAcceleration3D` builds one indexed, local-space LOD0 BLAS per unique
mesh and a TLAS from those packed transforms. Multiple mesh instances share a
BLAS. Analytic meshes use conservative canonical AABBs under TLAS transforms;
the shared affine shader evaluates the actual sphere, box or capsule in world
ray coordinates. Raster frustum/HZB culling never removes ray occluders.

A replacement snapshot drains outstanding frames, rebuilds its TLAS and reuses
unchanged BLAS. Removed meshes release their input buffers and BLAS. Re-admission
builds them again. Steady snapshots do not walk instances or rebuild acceleration.
`RaySceneStatistics` exposes those counts. This path does not yet use TLAS refit,
BLAS compaction, per-LOD ray geometry, skinning or GPU-generated instance updates.

## Typed occlusion and light evaluation

The compiler-owned statement has the following closed contract:

```typescript
RayQueryTraceOccluded(Scene, Procedurals, Rays, Blocked, Instances,
    index, AffineAnalyticIntersection);
```

It publishes `compute.rayquery.m4`. The older closest-hit m2/m3 contracts remain
unchanged. The callback is required and follows the existing immutable m3
intersector signature. Opaque mutable traversal state does not escape into
Visual TypeScript. Binding authority, statement-only use and the closed callback
are checked by the compiler.

The ray input retains the four-word count header and twelve words per ray.
`Blocked[index]` is one numeric float, zero or one. Each retained instance has
four uint-bit words: primitive kind, layer, mask and procedural index. The
lowering rejects invalid ranges/masks and calls `Abort()` on the first valid
blocker. Closest-hit traversal still visits candidates to preserve deterministic
primitive ties; that work is unnecessary for an occlusion answer. See the
[DXR inline ray-query specification](https://microsoft.github.io/DirectX-Specs/d3d/Raytracing.html#inline-raytracing).

Local light data is one shared sixteen-word record. Both the reference integrator
and real-time surface resolve import `LocalAttenuation` and the existing direct
BRDF functions. Point sources use the existing inverse-square coefficient with
a smooth finite range window; spots multiply the same smooth cone factor. These
are delta sources. "Exact" here means deterministic evaluation of every admitted
light, without stochastic light selection; it does not imply area-light sampling
would be noise-free.

Light tiles contain eight 32-light masks. Surface resolve fetches one mask per
active group, then evaluates its allowed lights. The no-light case skips these
loops. GPU rays read the actual motion/depth and RGBA16F normal attachments.
Normals are decoded from a GPU buffer copy without requiring native CPU code or
half-precision arithmetic support. A 0.5 mm normal offset and 0.2 mm minimum ray
distance prevent reconstructed-depth self hits; the ray ends before the source.
These are explicit finite-precision visibility biases, qualified on the stated
metre-scale scenes, rather than a universal large-world precision policy.

The visibility transport uses four RGBA32F texels per pixel. Each numeric float
holds sixteen bits exactly, so nearest sampling preserves the masks without NaN
payloads. GPU scratch plus the visibility texture costs approximately 204 logical
bytes per pixel: about 179 MiB at 1280x720, plus parameters and acceleration.
This deliberately simple exact baseline has a substantial memory cost. Direct
buffer consumption or sampled-light accumulation can remove that transport in a
later comparison; this gate does not claim an optimized shipping representation.
Targets exceeding the device image extent, storage descriptor range or one-dimensional
dispatch capacity fail with `AUR-RAY-SHADOW-003` before ray-resource allocation.
No new native CPU tracing library or native runtime dependency was introduced.

## Reproduce and acceptance

```powershell
tools/aurelian-rendering-r3/Qualify.ps1
```

The qualifier runs the full Aurelian build/test solution, Copeland TypeScript
suite, the existing reference/affine/lighting/temporal/hybrid/diffuse proofs and
the new R3 proof under Vulkan synchronization validation. It records source and
binary hashes and rejects source changes during the run. Pinned NVIDIA FLIP 1.7
runs as offline tooling on native tone-mapped sRGB output.

The new proof checks:

- 256 shadowed sources: 192 point lights and 64 spots, with finite 3.5 m ranges,
  in a hall containing eight instances of one triangle mesh and an analytic sphere.
- Three moving-occluder snapshots against an independent world-space closest-hit
  reference acceleration path. Direct-only, one-sample references have no Monte
  Carlo light-selection noise. Both sides use identical BRDF, attenuation and tone mapping.
- Byte-identical tiled/unculled output, mesh BLAS reuse, snapshot TLAS replacement,
  steady-frame admission, and rejection of unsupported map/MASK paths.
- Isolated active-light indices 0, 15, 16, 31, 32, 63, 64, 127, 128, 191, 192 and
  255 against a one-light reference, with an off-camera analytic blocker.
- Independent layer/mask exclusion, removal/re-admission of a BLAS, and recovery
  of the same point shadow. Orthographic reference comparison also caught and
  fixed perspective-style view vectors in orthographic surface shading.
- Whole-image and reference-edge FLIP improvement over unshadowed controls.
- Steady interactive GPU frame time, CPU prepare/submit cost, wall throughput,
  first-frame admission cost and separately captured shadow-pass cost at 640x360
  and 1280x720. Timed runs use 24 warmup frames and 64 measured frames. The declared
  first gate requires 640x360 GPU frame time below 16.67 ms on the qualification device.

The initial accepted RTX 3070 baseline snapshot is recorded in
`artifacts/local/rendering-r3/final/qualification.json`: 72 source hashes and both
proof/graphics binaries match, 3,349 tests pass with zero failures/skips, and all
native proofs are clean under synchronization validation. The build has zero
warnings/errors. Existing reference, affine, lighting/temporal, hybrid and diffuse
regressions pass alongside the new R3 qualification.

| Resolution | Median whole GPU frame | Median CPU prepare + submit | Captured local shadow pass | Wall throughput |
|---|---:|---:|---:|---:|
| 640x360 | 3.514 ms | 0.800 ms | 1.902 ms | 275.7 fps |
| 1280x720 | 10.852 ms | 0.739 ms | 6.206 ms | 89.1 fps |

First-frame admission takes about 31.6 ms at both sizes, including synchronous
BLAS/TLAS preparation. Renderer/device construction and shader compilation precede
that measurement. Steady GPU frame cost includes the ordinary geometry, identity,
local-ray, lighting and output passes. CPU prepare/submit excludes frame-slot wait;
wall throughput includes that wait and final GPU retirement. These results apply
to this nine-instance scene and this device, rather than predicting dense-world
acceleration or universal many-light performance.

At 96x64, the three hall poses have display RGB MAE of 0.0000504..0.0000551,
versus 0.003177..0.003516 without shadows. Whole-image FLIP is
0.001931..0.002069 versus 0.020669..0.022384. Reference-edge FLIP is
0.003620..0.003733 versus 0.088463..0.096233. Tiled and unculled output is
byte-identical. The 48x48 off-camera point-shadow specimen improves FLIP from
0.337898 to 0.003453; its layer/mask exclusion controls match the unshadowed
output byte-for-byte. All twelve isolated light indices have the same
0.0000936 display RGB MAE against the independent one-light reference.

The [combined light-sampling qualification](rendering-r3-light-sampling.md) reruns
these exact controls against the current sampler-capable sources.

Keep initial failed attempts alongside the accepted run: the isolated orthographic
comparison and larger-capture self-shadow artifacts produced real fixes.

## Following experiments

The next comparison can use this exact baseline and its matching reference light
table: exact-few/stochastic-rest, fully stochastic light selection and ReSTIR DI,
with a fixed frame-wide visibility-ray budget. Area-source sampling, temporal
reuse/denoising and the resulting error/cost tradeoffs must be qualified separately.
This gate does not select any of those as the default.
