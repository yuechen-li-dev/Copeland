# R2: hybrid analytic and triangle visibility

The resident visibility-buffer path now admits opaque canonical spheres, boxes and capsules alongside
triangle meshes. Instance transforms provide placement, rotation, nonuniform scale and shear; a transformed
unit sphere therefore renders an ellipsoid. Intersections, normals, UVs and depth are reconstructed in GPU
shaders and use the same material and surface-lighting functions as triangle hits.

The [shared affine reference continuation](rendering-r3a-affine.md) extracts those
intersections into a common module used by ray queries and permits reflected
analytic instances with corrected tangent handedness. Triangle instance winding
admission remains unchanged.

This is a bounded implementation of the hybrid proposal. It does not admit arbitrary field programs/CSG,
analytic MASK coverage or analytic shadows. Those combinations fail explicitly rather than drawing the
coverage proxy as if it were the surface. Triangle-only rendering and its existing forward paths remain intact.
No native CPU geometry library, runtime simplifier or Aetheris module is added.

## Use it

```csharp
var sphere = NativeAnalyticPrimitives3D.Create("polished-ellipsoid", AnalyticPrimitiveKind3D.UnitSphere);
var material = new ModelMaterial("metal")
{
    BaseColor = new Vector4(.7f, .45f, .2f, 1),
    Metallic = .8f,
    Roughness = .2f,
};
var instances = new NativeInstanceSet3D([
    new SceneInstance3D("ellipsoid", sphere, material,
        Matrix4x4.CreateScale(2, 1, .7f) * Matrix4x4.CreateTranslation(0, 1, 0)),
    // Ordinary triangle mesh instances can share this same set.
]);
renderer.Settings = renderer.Settings with { Shadows = false };
renderer.EnableVisibilityBuffer = true;
renderer.Render(new Native3DScene([], []) { Instances = instances }, camera, eye, clear);
```

The game host and graphics-proof renderer factory supply `AnalyticRaster3D.v.ts` and `HybridResolve3D.v.ts`
through the optional `VisibilityBufferPrograms.AnalyticRaster` and `.HybridResolve` properties. Direct
renderer construction supplies the same programs. The admitted scope requires a resident set, bindless
visibility-buffer support, those programs, no directional shadows and no shadow-casting local lights.
`AUR-HYBRID-001` diagnoses unsupported rendering paths; `AUR-HYBRID-002` diagnoses analytic MASK materials.

Canonical shapes:

| Kind | Local surface |
|---|---|
| `UnitSphere` | Radius 1 at the origin |
| `UnitBox` | Closed box from -1 to +1 on each axis |
| `Capsule` | Radius 0.5 around the Y-axis segment from -0.5 to +0.5 |

Nonuniform transforms produce affine images of these canonical surfaces, including ellipsoidal capsule
caps. This version does not expose an independently variable capsule segment/radius ratio. Materials
support the existing opaque base colour, metallic/roughness, normal, occlusion, emissive, unlit and
subsurface channels through shared evaluation. The profile is static resident geometry; replace the
instance set to change transforms. Collision remains separately authored.

## Visibility and shading

An analytic mesh owns four vertices and six indices describing screen coverage. These are never a
tessellation of the actual surface. Culling uses authoritative canonical bounds, transformed with the
existing conservative sphere machinery. Analytic normal cones are disabled.

The analytic vertex stage projects all eight corners of the transformed canonical bounds, expands the
screen rectangle by two pixels and draws that rectangle. Bounds crossing the near/eye plane use full
viewport coverage. Exact intersections then reject misses and hits outside the clip-depth interval.
This also covers views inside a shape and surfaces whose bounding box crosses a clip plane.

For each covered pixel, two clip-space pixel planes and the near plane define a world ray. This avoids
a perspective-only eye-ray assumption and works with orthographic views. The existing normal-matrix
rows also provide the inverse linear transform for converting the ray to object space; no additional
per-instance inverse or descriptor array is introduced.

Spheres use closest approach plus the two quadratic roots; boxes use parallel-safe slab intervals;
capsules combine the finite cylindrical body with their hemispherical ends. The final hit writes
`frag_depth` into the **same D32 attachment** used by triangle rasterization. Both kinds therefore
participate in the existing hardware depth comparison and stable source draw order. HZB consumes that
actual shared depth and uses its existing current-frame rescue.

The shared `R32G32_UINT` identity format is:

| Word | Triangle | Analytic |
|---|---|---|
| X | Packed instance + 1 | Packed instance + 1 |
| Y | Packed mesh triangle × 2 + facing bit | `0x80000000` + facing bit |

The instance transport's final u32 is now explicitly named `primitiveKind`; the stride remains 160 bytes.
The admitted mesh-residency limits keep triangle IDs below the reserved high bit. The graphics frontend
now admits typed u32 bitwise operations through VD-MIR and DXC/SPIR-V; floating-point bitwise operands
remain rejected. IDs never pass through floating-point encodings.

Triangle-only scenes use the original identity raster and triangle resolve. Hybrid scenes retain the
triangle raster for mesh groups, select the analytic raster for analytic groups and use a shared resolve
variant that branches on the hit-kind bit. Triangle attribute/barycentric/quad-gradient reconstruction
is factored into `VisibilityGeometry3D.v.ts`, used by both variants. Material interpretation stays in
`SurfaceMaterial3D.v.ts`; no second BRDF or lighting pack is introduced.

Analytic UVs use local XY projection for curved primitives. Box faces use XY, -ZY or X-Z projection with
face-consistent tangent frames. Curved surfaces have a projection fold rather than conventional polar
sphere UVs. Pixel-quad gradients trace neighboring rays, with tangent-plane extension at missing helper
hits. Exact hit position and normal, the canonical UV/tangent convention, previous instance transform
and existing temporal projection produce the same surface outputs consumed by lighting, AO and TAA.

These are finite-precision analytic intersections, not a claim of infinite numerical resolution or
universal raster identity. The implementation is independently authored. Numerical references include
[PBRT's sphere treatment](https://www.pbr-book.org/4ed/Shapes/Spheres) and its
[object-space intersection interface](https://www.pbr-book.org/4ed/Shapes/Basic_Shape_Interface).

## Qualification

```powershell
pwsh -NoProfile -File tools/aurelian-rendering-hybrid/Qualify.ps1
```

The script builds/tests Aurelian and the TS compiler, runs the hybrid proof and all preceding R2 native
proofs, repeats the tooling-only native dependency publish checks, and compares all 225 existing captures
against the preceding qualified policy run. Final results and source/binary hashes are under
`artifacts/local/rendering-hybrid/final/`.

The full build has zero warnings/errors. All 1,595 Aurelian tests and 1,730 compiler tests pass, with no
failures or skips; the native proofs run with Vulkan synchronization validation enabled.

The final qualification checks 82,990 analytic/triangle interior samples across eight camera cases,
including perspective, orthographic, opposite views, inside views, near clipping and nonuniform/sheared
transforms. Its independent CPU oracle uses double-precision matrix inversion and intersection tests;
box faces use plane tests rather than the GPU's slab algorithm. It checks hit instance, kind, facing,
triangle identity and hardware depth. Maximum observed depth error is **1.95e-7**, below the fixed 3e-5
gate. Grazing, shared-edge and near-tie samples are counted separately. HZB images and identities are
exactly unchanged in those cases.

The textured/normal-mapped material comparison against 96-slice meshes has RGB mean absolute error
**0.00982/255**, with **0.00304%** changed foreground coverage. The tests also execute queued temporal
camera changes and confirm that analytic shadow-proxy rendering is rejected.

At 640×360 on the RTX 3070, the native timing experiment uses ABBA order, 128 warmup frames and 96 measured
frames per repetition. Mesh controls use existing GPU culling and the prepared LOD selector at a 0.25-pixel
error estimate. The 20,000-sphere source has 32M triangles; the selected meshed control submits about 5.97M.
Both representations pass the fixed image gates: RGB MAE <1.2/255 and changed coverage <0.5%. Timing captures
must contain more than 1,000 foreground pixels.

| Workload | Meshed complete GPU frame | Analytic complete GPU frame |
|---|---:|---:|
| 20,000 spheres, prepared LOD control | 1.265 ms | 0.385–0.386 ms |
| 20,000 boxes, exact 12-triangle control | 0.399–0.400 ms | 0.385 ms |
| One large capsule | 0.142 ms | 0.138 ms |
| 256 overlapping large spheres, HZB off | 0.667–0.672 ms | 1.067 ms |

The curved instance workload materially improves. The cheap box/capsule controls are close to neutral.
The high-overdraw case is a real regression: analytic depth requires intersection work before the depth
result is known, whereas triangle rasterization can benefit from early depth rejection. It is retained
as a negative control. Analytic geometry remains an explicit representation choice. The existing Dominatus
policy can choose HZB for admitted scenes, but this step does not automatically substitute a meshed asset
for an analytic instance. All steady measured frames retain zero CPU instance visits and instance uploads.

The curved instance loop improves from 745–767 FPS to 2329–2443 FPS. The large capsule loop is CPU-limited
at roughly 2600 FPS despite its small GPU improvement. The overdraw control drops from 1407–1443 FPS to
907–909 FPS; the regression is preserved rather than averaged into the positive workload.

General field/CSG traversal, multi-hit alpha coverage and analytic shadow integration remain outside this
profile. The shared hit/depth/material seam is now executable and qualified for the admitted primitives.
