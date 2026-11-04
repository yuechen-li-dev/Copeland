# R3: spatial ReSTIR and motion reprojection

The normal retained Vulkan renderer now supports motion-reprojected temporal
reservoirs and a separate spatial reuse pass. Both use the existing point/spot
light table, shared mesh/analytic acceleration, surface BRDF, TAA and tone mapping.
Exact lighting remains the default. This milestone implements finite-source direct
lighting; area-source samples, GI and a dedicated denoiser remain outside its scope.

## Authoring

Supply `RestirSpatial3D.v.ts` through `SurfaceLightingPrograms.RestirSpatial`, in
addition to the exact and sampled query programs. The complete construction is in
`RenderingBindlessProof.CreateRenderer(..., rayShadows: true)`.

```csharp
renderer.EnableVisibilityBuffer = true;
renderer.EnableRayShadows = true;
renderer.LocalLightSampling = new LocalLightSampling3D
{
    Mode = LocalLightSamplingMode3D.SpatiotemporalReservoir,
    FrameRayBudget = width * height * 4,
    Candidates = 16,
    HistoryLimit = 32,
    SpatialNeighbors = 4,
    SpatialRadius = 8,
    SpatialTargetMis = true,
    ReusePlaneTolerance = .01f,
    ReuseNormalCosine = .9f,
};
```

`ReprojectedReservoir` provides temporal reuse in one compute dispatch.
`SpatialReservoir` uses current-frame neighbors without temporal reuse.
`SpatiotemporalReservoir` generates current candidates with temporal reuse, then
combines them spatially in a second dispatch. Existing fresh RIS, same-pixel
temporal RIS, CDF importance and exact modes remain available as controls.

Spatial controls admit zero to eight neighbors and a one to 32 pixel radius.
Receiver rejection uses a configurable world-space plane tolerance and normal
cosine. The 96x64 quality experiment uses a two pixel radius; the 1280x720 timing
experiment uses eight pixels. Radius is measured in image pixels, not world units.

For moving instances, publish a continuing immutable snapshot each frame:

```csharp
var nextInstances = new NativeInstanceSet3D(currentInstances, previousInstances);
renderer.Render(scene with { Instances = nextInstances }, worldToClip, eye, clear);
previousInstances = nextInstances;
```

`currentInstances` contains the newly authored transforms without an explicit
`PreviousTransform`; do not copy a predecessor's derived previous transform into
the authoring input. Ordered identity, mesh and material must match the predecessor. The set
derives previous transforms, retains only a predecessor identity token and does
not retain a chain of old sets. A motion snapshot represents one frame: rendering
it twice raises `AUR-MOTION-002`. Advance the snapshot or publish a static set when
motion stops. A static set can be reused indefinitely with zero steady-state CPU
instance visits and uploads. Caller-authored previous transforms remain rejected.

This extends the retained upload path; it does not introduce a second motion or
scene authority. The initial qualification rebuilt the TLAS synchronously on
snapshot changes and spent about 30 ms in moving 96x64 capture prepare/submit.
The subsequent [TLAS update milestone](rendering-r3-tlas-updates.md) replaces that
work with frame-ordered updates for continuing snapshots. Skinning and deforming
meshes remain outside these milestones. The original steady static timing below
does not establish interactive moving-scene speed.
`ResetTemporalHistory()` explicitly resets TAA and reservoir history for a camera
cut. `ResetLightSamplingHistory()` resets only the sampling sequence and reservoirs.
Changing the sampler or light table also resets sampling history.

## What runs on the GPU

1. Raster writes current depth, previous projected UV/depth and normals through
   the existing G-buffer path. Previous UV already includes previous camera jitter.
2. Fresh RIS is generated for each allocated sample. Temporal reuse reconstructs
   the expected previous world point with the previous inverse camera matrix,
   searches four surrounding pixel centers and validates normal, plane distance
   and a depth-scaled pixel footprint. Previous transforms carry instance motion.
3. Spatial modes write those candidates into an immutable buffer. A compute
   write-to-read barrier separates this from neighbor reads. A separate buffer
   receives final history; no workgroup ordering or in-place neighbor race is used.
4. Each allocated sample streams a bounded random disk stencil. Receiver tests
   depend on geometry, not the chosen light or cached visibility. A donor slot is
   selected modulo its actual quota, including one-ray and fractional budgets.
5. The final selection traces visibility from the current receiver against current
   acceleration. Ordinary surface resolve evaluates the full shared BRDF with the
   resulting light ID and estimator weight, followed by ordinary TAA/tone mapping.

The frame budget remains an absolute upper bound. Reuse issues no extra visibility
rays: at most one current query per final allocated sample. Background and invalid
selections can leave slots unused. Normal frames perform no history readback, CPU
ray tracing, extra per-ray submissions or native CPU sampling-library calls.

The proposal target is positive over every ID in the finite light table, including
dark candidates. It uses intensity, color luminance, geometric attenuation and
normal cosine, with a positive floor. Visibility is excluded from reservoir
selection and stored weights. This common discrete domain uses an identity light
mapping with unit Jacobian across receivers.

Spatial reuse uses pairwise target MIS. For current/donor targets `tc`, `td` and
effective masses `Mc`, `Md`, the current partition for light `i` is
`Mc*tc(i)/(Mc*tc(i)+Md*td(i))`; the donor partition is its complement. Each incoming
normalized contribution weight is multiplied by its own partition and the current
target, then resampled and divided by the selected current target. The partitions
sum to one per light ID. This avoids treating a donor's target distribution as
the receiver's distribution, while keeping one streaming pass over neighbors.
`SpatialTargetMis=false` retains the constant mass-partition mixture for ablation.
Effective history mass is capped without changing the stored normalized weight.

`ImportanceProposals=true` is an additional negative-control experiment for the
three new modes. It replaces the fresh uniform-candidate stream with one draw
from the complete target CDF per reservoir, with source density `target/total`
and fresh mass one. `Candidates` controls the uniform stream only. This option is
off by default; it changes the useful history-mass balance substantially.

This follows the reservoir reuse ideas in
[ReSTIR](https://research.nvidia.com/labs/rtr/publication/bitterli2020spatiotemporal/)
and the contribution-weight reasoning in
[GRIS](https://research.nvidia.com/labs/rtr/publication/lin2022generalized/).
The implementation is Copeland VTS/C#; no RTXDI code or runtime dependency was added.

## Qualification and limitations

Run the complete build, tests, earlier rendering regressions, new native proof and
pinned FLIP evaluator with:

```powershell
tools/aurelian-restir/Qualify.ps1 -Output artifacts/local/rendering-restir/final
```

The wrapper records source hashes and proof/graphics assembly hashes. It rejects
source changes during the run. Native proofs enable Vulkan validation including
synchronization validation. Readbacks and independent reference captures belong to
qualification, and are excluded from interactive timing.

The quality workload has 256 point/spot sources, opaque mesh and analytic geometry,
24 measured frames after 16 warm-up frames and four allocated samples per image
pixel. Static, sinusoidal camera translation and moving-instance sequences use an
independent current-frame linear HDR reference. Residual-delta error compares
successive `(render-reference)` images, so scene motion is not counted as flicker.
The separate 64-frame TAA comparison uses exact native rendering with matching
jitter as its control. FLIP evaluates the saved final native tone-mapped images;
it is not a full-sequence perceptual temporal metric.

Single-source checks remove selection noise and compare against exact rendering
while both camera and receiver move. Other controls exercise camera-cut reset,
consumed-motion diagnostics, and one, fractional and four-sample budgets. Mean
energy is also checked against the independent many-light reference. These are
bounded executable controls, not a proof covering arbitrary scenes.

Spatial storage is substantial: two 96-byte history buffers plus one 96-byte
candidate buffer per pixel. Including the existing ray, hit, motion, normal and
visibility transports/output, this owner uses approximately 492 bytes per pixel,
or 432 MiB at 720p, before other G-buffers and acceleration. Allocation is lazy and
checked against device storage-buffer limits. Reprojection alone avoids the third
buffer and second dispatch. Spatial reuse remains an explicit research option.

## Native results

On the RTX 3070, mean per-frame linear HDR RMSE was:

| Sequence | CDF importance | Fresh RIS | Same-pixel temporal | Reprojected | Spatial only | Spatial + temporal |
|---|---:|---:|---:|---:|---:|---:|
| Static | 0.050192 | 0.385062 | 0.179325 | 0.178572 | 0.271449 | 0.131795 |
| Camera translation | 0.049267 | 0.416028 | 0.407885 | 0.203234 | 0.288517 | 0.156299 |
| Moving receiver | 0.050713 | 0.386681 | 0.187151 | 0.182024 | 0.273375 | 0.136822 |

All entries use the same four allocated visibility samples per pixel. The combined
mode lowers camera-motion error by 62% relative to fresh RIS and 23% relative to
reprojection alone. Camera residual-delta RMSE falls from 0.572315 in the old
same-pixel mode to 0.191720. Native motion readback finds 1,957 moving-receiver
pixels, with temporal reuse accepted on 1,816. All nine eroded interior pixels
newly revealing the floor reject history in the reprojected modes.

With 64 frames of native TAA, normalized display RGB MAE against matching exact
native TAA is 0.021397 for same-pixel reuse, 0.010636 for reprojection and 0.005698
for combined reuse: a 73% reduction relative to the original temporal mode.
This is distinct from the unjittered independent linear HDR comparison above.

The spatial ablation uses 32 measured static frames after 16 warm-up frames:

| Spatial combination | Radius 2 | Radius 8 |
|---|---:|---:|
| Constant mass partitions | 0.139849 | 0.215161 |
| Pairwise target MIS | 0.135254 | 0.174601 |

The positive-floor proposal and fresh final visibility preserve single-source
brightness, and the many-light sequences remain within 5% of reference total
energy. Pairwise MIS improves the broad stencil, but a smaller stencil is better
in this low-resolution scene. Neither setting is a universal image-quality policy.

Final-frame FLIP improves against fresh RIS in all three sequences and against the
old same-pixel mode under camera motion. Combined reuse does not beat reprojection
alone on static and moving-receiver final-frame FLIP. Linear HDR error, temporal
residuals and tone-mapped perceptual error measure different effects; all are kept
in the evidence rather than selecting only a favorable metric.

The existing full-CDF importance sampler is the stronger result in this workload,
including the identical 24-frame sequences above. At 64 TAA frames its display MAE
is 0.002225, better than every reuse variant tested. The finite 256-source domain
makes scanning the CDF affordable. The improvements to the reservoir path do not
establish superiority over that existing sampler.

Feeding the exact CDF into combined reuse lowers static raw HDR error from
0.135254 to 0.113227 at radius two, and from 0.174601 to 0.136769 at radius eight.
However, it worsens TAA display MAE to 0.020982 for combined reuse and 0.039694 for
reprojection alone, with the tested history limit of 32. A single fresh CDF draw
has mass one, whereas the original stream has sixteen candidates. Reusing old
mass under those settings strongly changes correlation; the observed regression
is retained, and CDF-input reuse is not promoted. These measurements do not
establish the best history policy across other scenes or budgets.

The native TAA FLIP measurements confirm the perceptual direction independently
of display MAE: fresh CDF is 0.010739, old same-pixel reuse 0.053165, reprojection
0.039273 and combined reuse 0.033462. CDF-input reuse regresses to 0.109744 in the
combined mode. This comparison uses the exact native TAA image as reference.

## Full-path cost and final ledger

The same timing batch uses the normal retained frame path at 1280x720, with 24
warm-up and 40 measured frames, capture disabled, ordinary materials/culling and
256 shadowed point/spot lights. GPU values are whole-frame medians. CPU values
include prepare/record and submission; throughput includes pipeline waits and the
final drain. The target is offscreen, so these are rendering throughput figures,
not a window compositor or display-refresh benchmark.

| Mode | GPU ms | CPU prepare/submit ms | Wall frames/s |
|---|---:|---:|---:|
| Exact | 11.539 | 0.691 | 83.1 |
| CDF importance | 9.164 | 0.771 | 104.1 |
| Fresh RIS | 7.535 | 0.743 | 127.2 |
| Old same-pixel temporal, history 32 | 9.157 | 0.762 | 103.3 |
| Reprojected, history 32 | 9.154 | 0.759 | 104.4 |
| Spatial only | 13.684 | 0.780 | 69.5 |
| Spatial + temporal, history 32 | 13.799 | 0.814 | 69.3 |
| CDF-input reprojected, history 32 | 10.389 | 0.693 | 91.8 |
| CDF-input spatial + temporal, history 32 | 14.906 | 0.748 | 64.4 |

Motion reprojection fits the previous temporal cost in this run. Spatial reuse
buys lower reservoir-path error at substantially higher GPU cost and memory use.
CDF importance is both faster and cleaner on this workload. No sampler default
was changed, and these data do not establish a general ReSTIR performance win.
The subsequent [TLAS update milestone](rendering-r3-tlas-updates.md) removes the
continuing-motion acceleration rebuild bottleneck and reports moving-frame costs.

`artifacts/local/rendering-restir/final/qualification.json` accepted the final
source. The Release solution build has zero warnings/errors; all **3,356 tests**
pass with zero failures/skips, including 1,730 Copeland TS tests. Earlier reference,
affine, lighting/temporal, hybrid, diffuse, exact ray-shadow and sampling native
proofs and their applicable FLIP checks also pass. Vulkan and synchronization
validation are clean. `verification.json` independently confirms all **82 source
hashes**, the proof assembly and the graphics assembly match the qualified files.

The final `restir/` folder contains raw JSON counters/metrics, HDR references,
native captures, FLIP maps and `taa-controls.png`. The latter enlarges the native
96x64 frames four times with nearest-neighbor sampling and includes the stronger
CDF control and the failed CDF-input experiment. Earlier attempts and the first
qualified uniform baseline remain in `artifacts/local/rendering-restir/`.
