# R4: regional diffuse GI policy

The regional experiment uses Dominatus to choose compiled lighting, residual
updates, or a full directional refresh independently across disjoint parts of
an existing probe grid. It keeps the retained renderer, shared TLAS, secondary
material evaluation and signed probe cache. There is no additional native
runtime dependency. Whole-Beacon qualification remains the next milestone.

## Authoring and ticking

Capture a stationary, fully covered `CompiledDiffuseProbeField3D` through the
existing runtime path, then install it with `SetCompiledDiffuseProbes`. Construct
`NativeDiffuseRegionalPolicy3D` with that grid, an integer partition (default
2 x 1 x 2), and `DiffuseRegionalPolicySettings`. Call `BeforeFrame` before the
ordinary renderer `Render`, then `AfterFrame` with its result. Pass elapsed
simulation time; there is no background worker.

```csharp
var policy = new NativeDiffuseRegionalPolicy3D(grid, settings: new()
{
    Hysteresis = .08f,
    MinCommitSeconds = .2f,
    MaximumRayUpdates = 2048,
});
policy.BeforeFrame(renderer, scene, elapsed);
var frame = renderer.Render(scene, worldToClip, eye, clear);
policy.AfterFrame(frame);
```

The plan is an immutable typed contract; the graphics layer does not depend on
Dominatus. Up to 32 nonempty regions partition at most 512 probes. A region's
Runtime command traces all directions when admitted. Residual traces a smaller
rotating subset; a zero budget explicitly defers an active region. Compiled
reads the captured baseline directly. The cap must fit the largest whole-region
refresh, and counts directional cache updates, not total ray queries: each
update can issue secondary lighting visibility queries, and receiver
connectivity work is separate.

Runtime refreshes use the same signed `current - baseline` storage as residual
updates. With full directional coverage, the total is the fresh finite runtime
basis. Per-probe cursors survive budget/strategy changes. Entering active mode
from compiled initializes untouched deltas to zero and retains baseline
backface evidence. Returning to compiled does not read uninitialized cache data.
When every region is compiled, the renderer skips cache updates, convolution
and telemetry dispatches and resolves the existing baseline directly.

## Feedback, invalidation and stability

A fourth compute phase reduces baseline-delta and temporal-change energy per
probe. Changed backface validity raises either proxy to at least one, so a
zero-RGB structural change cannot silently qualify compiled validity data.
A small mapped copy travels on the existing command submission and is
read only when its existing frame slot retires. Normal policy operation adds
no fence wait, queue submission, scene traversal or CPU ray tracing. At the
512-probe limit the copy is 16 KiB per active frame; CPU aggregation is O(probes).
The measurements are change proxies, not path-reference error or unbiased
variance. Full-frame GPU timestamps are observed separately and do not pretend
to measure the GPU cost of individual regions.

Each region has a real Dominatus utility selector with hysteresis and minimum
commit time. Stable near-baseline regions can compile, changed but settled
regions retain residual work, active regions can request a full refresh, and
compiled regions periodically audit. The scheduler admits incomplete regions
first and uses oldest-admitted order to prevent starvation within the hard
update cap. Invalidated incomplete regions have an explicit applied Runtime
correctness guard even if Dominatus still holds its Compiled state under
minimum commit. Inspector state reports selected and applied strategies
separately; deferred regions can retain stale light until their fair turn.

The bridge recognizes unchanged continuing resident snapshots using metadata
computed once at publication. Unknown geometry or local-light changes dirty
all regions. A caller may supply `affectedRadianceRegions` only when it can
conservatively bound all changed secondary radiance, including shadow and
bounce influence. An object's bounding box alone is insufficient. Sun,
environment, secondary-material controls and compiled-field replacement always
dirty every region, regardless of that hint. Epochs reject old work; policy
identities prevent pending feedback from a previous controller being adopted.
GPU observation sequences remain monotonic across cache-owner replacement.

## Executable qualification

```powershell
./tools/aurelian-rendering-regional/Qualify.ps1 `
    -Output artifacts/local/rendering-regional/final
```

The qualifier builds/tests Aurelian, runs compiler tests, then repeats the full
reference, affine, hybrid, temporal, many-light, ReSTIR, TLAS, raster-motion,
area/tail, diffuse, secondary-material and sun/environment lanes before the
regional native/FLIP proof. Source and binary hashes bind evidence to the run.
The previous accepted capture set must also remain byte-identical.

The motivating workload is four opaque rooms with a 4 x 4 x 4 probe grid and
128 directions. Walls optically isolate their single-bounce radiance below the
roof line. A bright local key switches off while an occluder enters room zero;
the caller supplies its certified region-zero radiance influence. Compare:

- stale compiled lighting;
- fixed 32 directions across all 64 probes (2,048 updates/frame);
- full runtime 128 directions (8,192 updates/frame);
- regional allocation capped at 2,048 updates/frame.

The proof compares actual resolved HDR and tone-mapped FLIP to an independent
2,048-sample, two-bounce GPU reference, checks the full-runtime endpoint,
signed negative corrections and unchanged-room compiled reuse. Alternating
8/24 directional budgets must cover the basis without restarting its prefix.
A global source change with a local hint must invalidate and fairly refresh
all four regions through delayed GPU feedback. Enclosing probes in an opaque
object must report changed backface validity even without RGB energy. Switching must preserve resident
geometry admission. A separate 720p run reports full-frame GPU/CPU cost and
policy CPU overhead.

The first occluder-only trial is retained under
`artifacts/local/rendering-regional/preflight-1`: its fresh finite-basis result
was worse against the independent reference than a fortuitously stale cache.
That is evidence of the existing coarse probe reconstruction limit. Reaching
a fresh basis is not by itself an image-quality guarantee. The light-switch
case supplies the accepted, falsifiable image-quality test; it does not qualify
unbounded influence hints, whole-scene policy quality, automatic probe placement,
a measured millisecond controller or a Tier R default promotion.

## Accepted result

`artifacts/local/rendering-regional/final-coverage/qualification.json` accepted
all native and FLIP lanes with clean Vulkan synchronization validation. The
full solution build had zero warnings/errors. All 3,401 tests passed (1,662
solution and 1,739 compiler), with no failures or skips. All 783 prior accepted
captures are byte-identical. The final verification checked 289 source hashes
and five binaries, including Runtime, Games and Dominatus, against the checkout.

At the same 2,048 probe-direction updates per frame, first-frame full-frame HDR
RMSE fell from 0.296624 with uniform fixed-32 updates to 0.072906 with regional
allocation (75.4% lower). Native tone-mapped FLIP fell from 0.061481 to 0.020438
(66.8% lower). Stale compiled HDR error was 0.332934. Regional reached the full
runtime endpoint in one frame; fixed-32 required four. Both the regional endpoint
and alternating 8/24-budget endpoint matched full runtime with exactly zero
HDR RMSE. Signed negative corrections totaled 14,894.81 across room-zero
samples. Changed backface validity, stale epoch rejection, global invalidation,
fair admission and preserved resident geometry also passed.

In the 720p stable specimen, fixed-32 measured 6.090 ms GPU and 0.898 ms CPU;
regional measured 6.141 ms GPU and 0.824 ms CPU, including 0.009 ms of policy
selection. These are full-frame measurements on the RTX 3070, with receiver
connectivity still dominating. The GPU difference does not establish a
speedup. The accepted benefit is faster image recovery at a bounded directional
update cap. This stable timing window uses a five-second audit interval;
periodic-audit behavior is separately covered by the controller tests.

The interrupted `final/` attempt and failed preflights remain unaccepted.
No Tier R default is promoted. Whole-Beacon image quality, motion, influence
bounds and frame cost are the next qualification target.
