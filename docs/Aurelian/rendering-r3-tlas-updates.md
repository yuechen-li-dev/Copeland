# R3: retained TLAS updates

Continuing rigid-motion snapshots now update the existing TLAS on the ordinary
frame command buffer. They reuse its storage, scratch and shared mesh/analytic
BLAS. The update path performs no acceleration allocation, frame-queue drain or
separate build submission. Cold admission and composition changes retain a
synchronous full rebuild.

## Authoring and admission

The default is `SceneAccelerationUpdateMode3D.Automatic`. Continue the existing
immutable snapshot protocol:

```csharp
var next = new NativeInstanceSet3D(currentInstances, previousInstances);
renderer.Render(scene with { Instances = next }, worldToClip, eye, clear);
previousInstances = next;
```

The predecessor must be the snapshot previously admitted by this renderer.
Ordered identity, mesh and material must match. The constructor derives previous
transforms for raster motion; callers still author current transforms only.
Continuing snapshots can change transforms and the existing ray layer/mask data.
Triangle transforms still preserve orientation; reflected analytic transforms
remain supported. This work does not add deforming or skinned BLAS refits.

A new static snapshot, changed count, changed identity/composition or an unrelated
predecessor takes the rebuild path. Reusing an unchanged static snapshot performs
no acceleration work. A motion snapshot still represents one frame and cannot be
rendered repeatedly without advancing it.

For an explicit full-rebuild control or to abandon a degraded refit hierarchy:

```csharp
renderer.RaySceneUpdateMode = SceneAccelerationUpdateMode3D.Rebuild;
```

This rebuilds changed snapshots; it does not rebuild an unchanged static snapshot.
Switching from the rebuild control to streaming updates requires the streaming
storage to be admitted on the next changed snapshot. There is no automatic
hierarchy-quality controller or periodic rebuild heuristic in this milestone.

## Recording and lifetime

The owning seam remains `VulkanSceneAcceleration3D`, called by the normal local
ray-shadow owner. The initial TLAS is built with `ALLOW_UPDATE` and the same build
flags subsequently used by `UPDATE`. Update mode supplies the existing structure
as both source and destination, with unchanged instance count and geometry layout.
This follows the [Vulkan acceleration-build contract](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdBuildAccelerationStructuresKHR.html).

Streaming admission retains two frame slots of mapped instance descriptors,
instance metadata and analytic transforms. Each slot is written only after
`VulkanFrameInFlightSet.Begin()` retires its previous use. The frame selects that
slot's descriptors and metadata together. The shared TLAS and aligned scratch
remain GPU-owned across both slots.

Inside the compiled local-ray pass, explicit barriers order previous compute ray
reads and acceleration writes before the in-place update, make host input writes
available to the build, and make the resulting structure visible to current ray
queries. The build runs on the same graphics queue as those queries. There is no
cross-queue ownership transfer or separate compute-queue performance claim.

Scratch storage covers the maximum of build and update requirements plus device
alignment. Composition rebuilds drain before releasing old resources and preserve
the existing active-mesh BLAS cache. The forced-rebuild control uses one mapped
input set, rather than paying for unused streaming slots. No native CPU tracing
library, runtime P/Invoke dependency or shader ABI change was introduced.

`RaySceneStatistics` now exposes cumulative `TopLevelUpdates`,
`SnapshotAllocations`, `SynchronousBuildSubmissions` and `InstanceUploadBytes`, in
addition to the existing BLAS/build counters. Snapshot allocations count
acceleration input/metadata, TLAS backing and scratch allocations, not every
renderer allocation. Upload bytes cover the descriptor and metadata streams.

## Native qualification

Run the complete build, tests and earlier rendering regressions, followed by the
new TLAS proof:

```powershell
tools/aurelian-tlas/Qualify.ps1 -Output artifacts/local/rendering-tlas/final
```

The qualifier records source and proof/graphics assembly hashes and rejects source
changes during execution. Native runs enable Vulkan and synchronization validation.

The new proof includes:

- **72 moving capture pairs:** 24 frames each of exact, CDF importance and combined
  spatial/temporal reservoir lighting. Updates and forced rebuilds must produce
  byte-identical output. Both triangle and analytic instances move; the sequence
  includes rotation, nonuniform scaling, analytic reflections, camera movement and
  ray-mask changes. Three exact poses also compare against the independent GPU
  reference, rather than relying solely on agreement between maintenance modes.
- **Rebuild boundaries:** a static replacement, changed instance count and changed
  mesh identity rebuild, while an unchanged static snapshot repeats no work.
- **Queued frame-slot reuse:** 1,033 moving triangle instances render 49 frames at
  720p, with capture disabled until the final frame. This produces 48 TLAS updates,
  exercises both frame slots and their retirement waits, and requires the final
  image to match a separate full rebuild byte-for-byte. Only the initial BLAS/TLAS
  build submissions are allowed.
- **Performance:** the ordinary hybrid visibility renderer moves nine instances
  under 256 point/spot lights using CDF importance, with 16 warm-up and 40 measured
  720p frames. The update path must reduce median CPU prepare/submit by at least
  15% and improve wall throughput by at least 10% against the full-rebuild control.

The first queued fixture attempted a reflected triangle transform and correctly
failed the existing `AUR-MESH-011` admission contract. The corrected fixture keeps
triangle orientation positive and retains reflections in the supported analytic
cases. That failed attempt remains with the other native artifacts.

## Results and limits

The native experiments pass all 72 byte-identical capture comparisons. Each
streaming capture sequence uses two initial shared BLAS builds, one initial TLAS
build and 23 updates. Across motion there are no further acceleration allocations
or synchronous build submissions. The larger queued case passes with 48 updates
and 47 observed frame-slot waits, then matches the rebuilt final image exactly.

Final RTX 3070 results at 1280x720, with capture disabled:

| Mode | CPU prepare/submit ms | Frame-slot wait ms | GPU frame ms | Wall frames/s |
|---|---:|---:|---:|---:|
| Full rebuild | 34.180 | 0.0002 | 9.390 | 29.03 |
| Inline update | 17.028 | 7.928 | 9.510 | 38.44 |

CPU prepare/submit elapsed time falls by 50%, while complete throughput improves
by 32%. These CPU-side durations include waits; they are not CPU utilization or
active-core time. Some waiting moves to ordinary frame-slot retirement, which is
reported separately. Wall throughput includes source-snapshot authoring, slot
waits and the final GPU drain, so it verifies a complete-path gain rather than
only a change in timing attribution.

GPU frame timestamps include the inline update. The full-rebuild control performs
its synchronous build before the frame timestamp interval, so its separate build
work is excluded from the GPU-frame column but included in CPU elapsed time and
wall throughput. These numbers do not establish a reduction in total GPU work.

During the 40 measured frames, rebuilds allocate 200 acceleration buffers and
submit 40 synchronous builds. Updates perform 40 inline refits with zero new
acceleration buffers or separate build submissions. The exact captures' maximum
display MAE against the independent reference is 0.0000683. All 53 comparable PNG
captures/maps from the preceding ReSTIR qualification remain byte-identical.

`artifacts/local/rendering-tlas/final/qualification.json` accepts the final source.
All **3,358 tests** pass with zero failures/skips, including the two new admission
tests and 1,730 Copeland TS tests. The full build succeeds; its 14 warnings are in
unchanged upstream/integration code. All earlier reference, affine,
lighting/temporal, hybrid, diffuse, exact shadow, sampling and ReSTIR native proofs
and applicable FLIP checks pass. Vulkan and synchronization validation are clean.
`verification.json` confirms all **86 source hashes**, the proof assembly and the
graphics assembly match the qualified files.

At this milestone, the remaining moving-frame CPU cost included retained raster admission: replacing
the snapshot still recreates visibility/material resources in
`VulkanRetainedModels3D.Upload`. This milestone removes TLAS rebuild/allocation
and submission work; it does not establish an allocation-free moving renderer.
The direct retained queued test isolates actual frame-slot reuse without those
unrelated admission uploads. The primary timing comparison keeps hybrid visibility
and culling enabled in both cases.
The subsequent [raster admission milestone](rendering-r3-raster-motion.md) retains
those resources across continuing motion snapshots. Its comparison holds TLAS
updates fixed; the historical TLAS comparison holds raster re-admission fixed.

The existing synchronous 96x64 moving-receiver capture still spends approximately
29 ms in prepare/submit. Its capture latency is largely unchanged, despite the
new no-capture throughput gain. Removing retained raster resource re-admission is
a separate remaining performance task, not an improvement claimed here.

The evidence is bounded to rigid instance motion on the RTX 3070. It does not
qualify dense-world hierarchy quality over long animation histories, BLAS
deformation, acceleration compaction or an adaptive rebuild policy. The explicit
rebuild control remains available for those future comparisons.
