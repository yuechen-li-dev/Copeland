# R2: resident scenes and GPU-driven clustered draws

R2's grouped-material native path now compiles immutable scene instances into resident transforms and
cluster bounds, culls on the GPU, compacts stable indirect commands, and renders through the existing
material fragment shaders and R1 frame tape. [Resident bindless material tables](rendering-r2-bindless.md)
now extend that path and remove per-material geometry descriptor binds. [Typed visibility buffers and
ID-driven material resolve](rendering-r2-visibility.md) are also implemented. [Two-phase HZB](rendering-r2-hzb.md)
is available as an opt-in follow-up. [Offline asset LOD and GPU selection](rendering-r2-lod.md) are also
implemented. Compute material scheduling and hybrid analytic visibility
remain open. The probes below document the original interface boundary; linked follow-ups describe
the subsequent implementations and their qualification.

## Use the resident path

```csharp
// Admission/upload happens when the scene revision is created, not while its camera moves.
var instances = new NativeInstanceSet3D(authoredInstances);
var scene = new Native3DScene([], []) { Instances = instances };
renderer.Render(scene, camera, eye, clear);
```

`NativeGameGraphics` supplies the three visibility compute programs through `GameAssets`. Direct renderer
construction can supply them in `RetainedModel3DPrograms.Visibility`. If the device lacks the required
indirect features, the resident set still uses cached transforms and existing direct instanced draws.
Ordinary mutable `Instances` lists retain their previous validation/upload behavior.

The immutable admission boundary snapshots instance records, validates identities/transforms/materials,
and caches mesh/material grouping, material features and temporal identity. Once both frame slots are
initialized, the renderer skips per-instance comparison, validation, grouping and transform uploads.
Camera and material uniforms still update. CPU work scales with mesh/material groups, not instance count
within those groups. This does not optimize application-side scene projection or dynamic transform streams.

Replacing the set creates a static scene revision and resets temporal correspondence. GPU buffers and
descriptors retire after their last graphics timeline use. Current and previous transforms are identical
within a static revision; stale previous poses cannot persist in alternating frame slots.

## Clusters, culling and compaction

`NativeMeshClusters3D` is an owned implementation. It splits the original index stream into contiguous
runs of at most 128 triangles, preserving every index and triangle order. Clusters carry enclosing spheres
and conservative normal cones. These indexed clusters can reference up to 384 vertices; they do not claim
the vertex-count constraints of a hardware mesh-shader meshlet.

We considered [meshoptimizer's clusterizer](https://meshoptimizer.org/#clusterization). R2 chooses the
small owned partitioner to preserve exact triangle order, avoid a native packaging dependency, and obtain
a measurable control first. It does not claim meshoptimizer-quality locality or optimal cluster bounds.
No meshoptimizer or Aetheris source was copied.

World spheres use a spectral-norm upper bound, `sqrt(||A||1 * ||A||infinity)`, so positive affine transforms,
including shear and nonuniform scale, remain conservative. Cone culling is admitted only for single-sided
materials under similarity transforms. Double-sided materials, shear and nonuniform scale keep their cones
disabled. Vulkan's zero-to-one clip-depth interval is used when extracting frustum planes.

Four modes provide controls: unculled, instance frustum, cluster frustum, and cluster plus cone. All commands
preserve source instance/triangle order. The compute sequence is:

1. Test instance sphere, then cluster sphere, then the admitted normal cone. A 64-lane workgroup scan writes
   local stable ranks and a block count. Tail lanes participate in all barriers and write zero visibility.
2. Scan block counts to global offsets and total draw count. The typed uniform parameter uses the existing
   bounded-loop profile: at most 512 iterations processing 32 blocks each. No global atomic extension is needed.
3. Scatter visible clusters into 20-byte `VkDrawIndexedIndirectCommand` records. GPU count and arguments feed
   `vkCmdDrawIndexedIndirectCount`, with one recorded draw command per material/mesh group.

The Vulkan plant queries/enables `multiDrawIndirect`, `drawIndirectFirstInstance`, and Vulkan 1.2
`drawIndirectCount`. Promoted timeline, device-address, float16 and memory-model features share the Vulkan 1.2
feature structure, preserving optional ray/cooperative feature chains. See the
[Khronos indirect-count guide](https://docs.vulkan.org/guide/latest/extensions/VK_KHR_draw_indirect_count.html).

Inputs and outputs stay in device-local buffers. Per-slot camera parameters, ranks, counts and arguments
preserve R0's two-frame ownership. Normal frames perform no visibility readback. The explicit qualification
method drains outstanding frames and copies ranks/counts to staging for comparison with a double-precision
CPU half-space/cone evaluator.

The R1 plan contains `cluster-visibility` followed by the geometry consumer. `FrameAccess.Indirect` makes
compute-write to indirect-read hazards explicit. Pack-internal scan barriers remain inside that stage.
The public GPU timing schema adds `visibility-cull`; presentation qualification checks that named stage too.

Limits are 65,536 resident instances, 128 grouped culling groups, 1,048,576 aggregate cluster candidates,
the physical device's per-group indirect-count limit, and the existing 256 MiB mesh residency limit.
Admission failures retain diagnostics and publish no partial culling group set. The later material-table
step raises resident admission to 1,024 mesh/material groups and merges consecutive same-mesh groups for
bindless culling/drawing; grouped GPU culling retains the 128-group limit.

## Materials and shadows

The grouped control preserves the current material ABI, texture/sampler bindings, alpha masking, normals, motion,
subsurface output and shared surface lighting. Its per-material descriptor binds remain; the later
bindless path shares surface evaluation through resident tables. Blended/refractive
materials continue through their existing forward batch adapter; resident admission rejects them explicitly.
Shadow casters retain their original direct instanced draws. Camera culling never incorrectly removes an
offscreen caster from a directional or local shadow map. GPU shadow-view culling is not claimed.

## Historical probes and the remaining compute image ABI

The proposal's visibility buffer would replace fragment material evaluation with ID storage, triangle
reconstruction and compute resolve. The [original visibility-buffer paper](https://jcgt.org/published/0002/02/04/)
motivates that separation. The linked follow-up implements identity rasterization and fullscreen material
resolve through the owned graphics backend. Compute material classification remains a later scheduling experiment.

The original `--rendering-r2` run generated three modified `SurfaceModel3D` sources and ran the real graphics binder:

| Probe | Actual blocking diagnostic |
| --- | --- |
| Integer visibility target | `COPE-GPU-TARGET-0003`: pixel targets require `float4` |
| Fragment primitive ID | `COPE-GPU-BUILTIN-0003`: `primitive_id` is not admitted for pixels |
| Material storage buffer | `COPE-GPU-RESOURCE-0001`: graphics resources are textures, samplers and canonical material uniforms |

Artifacts retain the complete compiler diagnostics and failing sources in `profile-capability-probes.json`
and adjacent `.v.ts` files. This identifies a shader-language/compiled-interface boundary, not a GPU hardware
limitation. The material-storage gap is now addressed by plain value records, graphics storage resources
and descriptor arrays in `graphics.tables.m1`. The old probe substitutes a material-uniform declaration
without changing its accesses, so it remains invalid and is not a current test of plain storage support.
The visibility follow-up adds `R32G32_UINT` attachments, primitive/draw builtins and integer image loads in
`graphics.visibility.m1`. Compute still consumes storage buffers rather than sampled/storage images.

The visibility implementation extends the existing binder, VD-MIR, compiled contracts, HLSL/SPIR-V
emission and Vulkan formats. Material evaluation is shared by its ID-driven and material-raster paths.
Future compute classification/resolve needs compute image access through those same owned interfaces.

The HZB follow-up adds current-frame depth production, GPU pyramids and conservative rescue across camera
cuts. It redraws the accepted set in stable source order to preserve equal-depth semantics. The original
opaque pass still discards its hardware depth after the pass; HZB uses the visibility raster's separate
stored depth target. [Offline LOD chains and hysteresis](rendering-r2-lod.md) are now implemented in a follow-up;
the original measurements in this report keep full source geometry and incur no simplification error.
Hybrid analytic/triangle visibility has not been timed
or killed by an experiment. It depends on the typed shared visibility/depth interface first.

## Aetheris reuse and licensing

R2 adds no Aetheris reference, source port, or relicensing grant. Its partitioner, bounds, scan and culling
are independently authored. Larger analytic modules remain candidates for an optional bridge. Any future
small AGPL-derived port needs file/revision provenance and an explicit author decision before being treated
as GPL-3.0. Existing notices and grants retain their scope. See [the boundary policy](aetheris-boundary.md).

## Qualification

```powershell
pwsh -File tools/aurelian-rendering-r2/Qualify.ps1 `
    -Baseline artifacts/local/rendering-r2/before `
    -Output artifacts/local/rendering-r2/final
```

Before editing, binaries from `b11b32d1` were saved outside the repository and all nine existing proof modes
were captured. Qualification builds/tests `Aurelian.slnx`, runs the new native proof with synchronization
validation and tiered compilation disabled, reruns those nine modes, compares all baseline PNGs, and records
source/binary hashes. The baseline is never regenerated by the qualification command.

The Forest has 20,000 procedural tree instances and 23,040,000 source triangles, including 21,440,000
nondegenerate triangles; the proof separately gates that nondegenerate count above 20 million. Eight frozen cameras compare
the original direct instanced path with all four GPU culling controls, then compare GPU ranks against the
CPU evaluator. Near-zero counts, non-multiple-of-64 tails, multiple materials/nonzero first-instance offsets,
back faces, double-sided surfaces, shear/nonuniform transforms, static revisions and temporal AA have
separate native boundary cases. Pixel equality checks complement the shared-bound CPU reference.

Performance samples use 12 warmup frames and 36 retained samples at 1,250, 5,000 and 20,000 instances, with
the same mesh/material groups. CPU preparation/recording, submit, and GPU frame intervals are recorded
separately. The path is the production `VulkanSolid3DRenderer` through the full surface/output pipeline,
rendering to a native target. It excludes swapchain presentation, application scene projection, initial
scene admission/upload and qualification readback. Shadows, AO, bloom and temporal AA are disabled for
the scaling measurement; boundary cases and existing captures exercise their compatibility separately.
Timings remain machine/workload-specific. Zero CPU instance visits/uploads and fixed indirect group count
provide structural evidence alongside the measurements.

## Local results, 2026-11-03

Authoritative evidence is `artifacts/local/rendering-r2/final-3/`; earlier attempt directories retain
compiler failures and preceding qualification runs. The final full build has zero warnings/errors and
all 1,553 tests pass with no skips. All 225 existing PNG captures are byte-identical across the nine proof
modes. The native R2 proof passes synchronization validation, verifies the resident direct-draw fallback
without compute programs, and exercises the transform/material/temporal boundary cases described above.
That fallback tests the program-availability branch on this RTX 3070;
the feature-unavailable branch has not been run on a second physical device.

| Instances | Direct mutable prepare/record | Resident GPU prepare/record | Direct GPU interval | Resident GPU interval |
| ---: | ---: | ---: | ---: | ---: |
| 1,250 | 0.884 ms | 0.247 ms | 0.989 ms | 0.431 ms |
| 5,000 | 2.747 ms | 0.251 ms | 3.107 ms | 0.314 ms |
| 20,000 | 11.377 ms | 0.249 ms | 4.918 ms | 0.559 ms |

The CPU improvement comes from resident admission/caching as well as fixed indirect submission; it is not
attributed solely to GPU culling. GPU intervals include the full configured native frame. Fixed profile
ordering and unlocked GPU clocks mean these short runs are diagnostic, not universal performance ratios.
Three successive native runs showed the same large-case improvement direction and approximately 0.25 ms
resident preparation cost across the tested instance counts.

For frozen pose zero, 180,000 cluster candidates produce 11,493 commands after instance-frustum culling,
10,825 after cluster-frustum culling, and the same 10,825 with cones enabled. Cones provide no additional
reduction for this tree/camera, while the single-sided back-plane specimen verifies a real zero-command
cone result. All 32 Forest culling/camera captures match the original direct-instanced render. CPU/GPU
classification has zero false negatives and zero false positives on that path; this is not an exhaustive
floating-point correctness claim for every possible scene.

The qualification ledger labels its scope as the resident GPU-clustered grouped-material path and marks
`FullR2Complete` false. Bindless tables and visibility/material resolve are now implemented in the linked
follow-ups. Two-phase HZB and offline asset LOD are also implemented. Compute material scheduling and the analytic comparison remain open.
