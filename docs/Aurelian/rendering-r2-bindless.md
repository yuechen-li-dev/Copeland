# R2: resident material tables and many-material Forest

The resident native path now renders opaque/MASK instances with one material descriptor-set bind per
geometry pass. Material values, per-instance material IDs, textures and samplers live in immutable GPU
tables. Consecutive groups sharing a mesh become one draw run, preserving instance and triangle order.
The existing GPU cluster culler consumes those same runs.

This implements the material-table portion of R2. Integer visibility targets, primitive identity,
compute material resolve, two-phase HZB and LOD remain subsequent experiments.

## Use it

`NativeGameGraphics` supplies `BindlessModel3D.v.ts` automatically. Author a `NativeInstanceSet3D` and
replace it when transforms or materials change:

```csharp
var instances = new NativeInstanceSet3D(authoredInstances);
var scene = new Native3DScene([], []) { Instances = instances };
renderer.Render(scene, camera, eye, clear);
```

Direct renderer construction supplies `RetainedModel3DPrograms.Bindless` alongside the existing vertex,
shadow and optional visibility programs. `EnableBindlessMaterials` defaults to true; false selects the
grouped control. A missing program or unsupported device retains grouped rendering.

The native profile requires Vulkan 1.2 nonuniform sampled-image indexing, 4,096 sampled-image descriptors,
64 sampler descriptors, and sufficient aggregate per-stage resources. Physical device limits gate its
selection. Arrays have fixed extents and every slot contains a valid descriptor; unused slots repeat a
valid fallback. Runtime arrays, partially bound descriptors and update-after-bind are unnecessary here.
See the [Khronos descriptor-indexing sample](https://docs.vulkan.org/samples/latest/samples/extensions/descriptor_indexing/README.html)
and [Vulkan 1.2 feature definitions](https://docs.vulkan.org/refpages/latest/refpages/source/VkPhysicalDeviceVulkan12Features.html).

## One material implementation

`SurfaceMaterial3D.v.ts` owns `EvaluateSurfaceMaterial`. Both grouped `SurfaceModel3D.v.ts` and the new
bindless shader call it. Texture filtering, mip selection, color spaces, tangent-space normals,
metallic/roughness, occlusion, emission, MASK, double-sided handling and subsurface output retain the
same evaluation. Shared lighting and temporal projection remain downstream.

`VulkanModel3DBatches` continues to own images, mip generation and samplers. The table borrows its handles.
Resident-only steady frames update one frame uniform rather than rebuilding material uniforms. Scene
replacement retires tables and unused images/samplers through the existing graphics timeline. Each frame
slot owns its frame uniform.

Material records are 128 bytes: five `float4` surface values (80 bytes), five texture indices (20 bytes),
five sampler indices (20 bytes), and two reserved words (8 bytes). A separate `u32` table maps the packed
instance order to material IDs. Vulkan's instance index includes `firstInstance`, so direct instancing
and compacted indirect draws address the same table. The native packer verifies compiled field names,
scalar types, offsets, strides, frame uniform and descriptor declarations.

Shaders compile through the existing TypeScript binder, VD-MIR, compiled graphics contracts, HLSL/DXC
and validated SPIR-V. The new owned profile is `graphics.tables.m1`; existing graphics profiles retain
their serialized contracts. Graphics now admits:

- Read-only `StorageBuffer<T>` for nonempty `f32`, `u32`, vector and plain aggregate transport values.
- Fixed `DescriptorArray<Texture2D<float4>, N>` and `DescriptorArray<Sampler, N>`, capacity 1..4096.
- Literal descriptor indices, or explicit `NonUniform(u32)` dynamic indices.

Compute and graphics share one word-transport layout authority. Graphics storage emits uint words with
explicit reconstruction and bounds checks, avoiding implicit HLSL/std430 record padding. Booleans,
resources and material-uniform declarations are not storage value types. Native admission/table
construction provides descriptor-index validity; the compiler does not clamp dynamic descriptor indices.

## Draw ordering and limits

Only consecutive groups using the same mesh merge. Heterogeneous mesh runs retain their order, including
opaque depth ties. Material count no longer determines descriptor-bind count; draw count still depends
on mesh runs. Shadows retain grouped direct draws, preserving offscreen casters. Blended/refractive
surfaces use the existing forward batch path.

Admission supports 65,536 instances and 1,024 mesh/material groups, subject to 4,096 unique resident texture
views and 64 samplers. Capacity is checked before GPU table allocation. Existing aggregate cluster
candidate and geometry-residency limits remain. Grouped GPU culling retains its 128-group limit; larger
material sets use merged bindless runs or direct resident drawing. Mixed legacy/resident scenes can still
incur per-material maintenance in the legacy batch owner. Texture streaming, mutable table updates and
GPU shadow-view culling are outside this step.

No Aetheris source, module or dependency was added. The existing [boundary policy](aetheris-boundary.md)
governs any future port or optional analytic bridge.

## Reproduce and inspect

```powershell
pwsh -File tools/aurelian-rendering-bindless/Qualify.ps1 `
    -Baseline artifacts/local/rendering-r2-bindless/before `
    -Output artifacts/local/rendering-r2-bindless/final-2
```

Pre-change captures were produced from binaries saved from `f5550f3c` outside the repository before
editing. The command never regenerates that baseline. It builds/tests `Aurelian.slnx`, runs the full
`Copeland.TS.Tests` project, runs native bindless and clustered proofs with synchronization validation,
then compares 225 existing captures across nine modes. Source/binary hashes accompany the acceptance ledger.

The new proof uses the real `VulkanSolid3DRenderer` surface/output path at 640x360 on an RTX 3070. Its
Forest contains 20,000 instances and 23.04 million source triangles. Counts are 1, 64 and 512 materials,
with unique base/emissive maps, shared normal/MR/occlusion maps, varied samplers, MASK, double-sided
materials and subsurface output. Four cameras per material count must match the grouped control exactly.
At 512 materials, the table contains 1,027 unique images.

Both timed paths use resident transforms and disable cluster culling to isolate material/draw overhead.
They disable shadows, TAA, AO and bloom. ABBA order, disabled tiered compilation, 12 warmup frames and 36
measured frames reduce warmup/order bias. CPU preparation/recording, submit, GPU full-frame intervals and
frame-loop throughput (including final drain) are separate. Admission/upload, captures, application scene
projection and swapchain presentation are outside timing. Clocks remain unlocked; results are local.

A separate native case enables GPU culling, shadows and TAA across two meshes, six materials, nonzero
instance offsets, transform/material revisions and in-flight replacement. It compares grouped captures
and checks GPU visibility against the existing CPU evaluator. This is an integration check; its timings
are not substituted into the many-material comparison.

## Local result

The final source builds with zero warnings/errors; 1,561 Aurelian tests and 1,730 compiler tests pass
with no skips. The native bindless and preceding clustered proofs pass synchronization validation.
The final many-material measurements retain both ABBA repetitions:

| Materials | Grouped CPU prepare/record | Bindless CPU prepare/record | Grouped frame-loop FPS | Bindless frame-loop FPS |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 0.451–0.454 ms | 0.338–0.414 ms | 142.9–192.2 | 191.3–192.5 |
| 64 | 1.000–1.124 ms | 0.379–0.444 ms | 195.7–195.8 | 194.2–194.6 |
| 512 | 5.201–5.836 ms | 0.362–0.379 ms | 147.9–169.5 | 192.0–194.0 |

At 512 materials, descriptor binds and draws fall from 512 to one. GPU intervals are 4.843–4.869 ms
grouped and 4.862–4.928 ms bindless: the improvement removes CPU submission pressure. The first one-material
control has a slower 5.376 ms GPU interval and 142.9 FPS; its repeated control returns to 4.916 ms and
192.2 FPS. It remains in the evidence and table. One-material and 64-material frame throughput is
otherwise effectively unchanged; there is no consistent GPU-time benefit. Earlier native runs repeat
the large material-count CPU improvement, with 512-material grouped CPU times near 6 ms versus bindless
near 0.4 ms.

All twelve Forest captures and mixed-mesh/revision captures match exactly. See
`artifacts/local/rendering-r2-bindless/final-2/rendering-r2-bindless/evidence.json` for these measurements
and `artifacts/local/rendering-r2-bindless/final-2/qualification.json` for qualification of the final source.
Earlier logs preserve initial compiler errors and successive native experiments.

The next visibility experiment can build on real resident tables and shared material evaluation. It
still needs typed integer attachment formats, primitive identity, explicit gradients and compute image
access to implement visibility storage and compute resolve through the owned shader path.
