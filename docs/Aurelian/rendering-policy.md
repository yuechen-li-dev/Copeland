# Dominatus visibility policy and native LOD boundary

LOD generation belongs to tooling. The `Aurelian.AssetCompiler` project owns meshoptimizer and its native
C API calls. `Aurelian.AssetTool`, compiler tests and graphics research tools explicitly reference it.
`Aurelian.Assets`, `Aurelian.Games` and the shipped starter game do not. The runtime asset loader reads
and validates existing prepared LOD data; `lod = true` without a delivery sidecar is rejected rather
than triggering native compilation at startup. Existing artifact bytes and content identities are preserved.

This removes the meshoptimizer runtime dependency introduced by the original LOD implementation. It
does not remove existing Vulkan/driver interop, Skia image/font paths or other previously present native
dependencies. Ordinary rendering control and the Dominatus policy below are C#.

## Policy ownership

`Aurelian.Runtime.Rendering.VisibilityPolicyController` uses the existing managed Dominatus HFSM utility
decision mechanism, including its actual `DecisionPolicy.Hysteresis` and `MinCommitSeconds`. It does not
implement a second switching state machine around Dominatus. A named agent and the existing bounded
inspector expose option scores, selected states and reasons such as `MinCommitActive` and `HysteresisBlock`.

The four options are baseline, prepared LOD, HZB and both. GPU selection of individual prepared mesh levels
remains a renderer mechanism, with its existing 20% screen-error hysteresis. Dominatus chooses which
mechanisms to use; it does not visit every instance or hold a coarse level beyond its quality limit.
Near-plane and quality-driven refinement remain immediate.

Default policy parameters:

| Setting | Value |
|---|---:|
| Utility hysteresis | 0.05 on normalized relative GPU cost scores |
| Minimum commitment | 0.75 seconds |
| Settling frames discarded after a switch/revision | 8 |
| Minimum accepted samples per mode | 8 |
| GPU cost estimator | EMA, new sample weight 0.125 |
| Reprobe age | 15 seconds |
| Default LOD quality ceiling | 0.25 pixels, as an error estimate |
| Pending timing identities | At most 64 |

An unmeasured applicable mode receives exploration priority. Once modes are measured, their utility is
proportional to the best measured cost divided by their own cost. Stale alternatives are periodically
probed so policy can adapt. Probes and initial trials can temporarily choose a slower mode; they are
bounded experiments, not a promise of the fastest setting on every individual frame.

Only complete GPU frame timestamps feed the cost model. Each submitted frame records its actual mode;
completed timestamps match that submission sequence, not the mode current when the timing arrives.
Duplicate, stale, unsettled, missing or nonpositive observations are ignored. Scene changes and large
world-to-clip changes invalidate the learned costs and pending identities. Capability withdrawal is an
immediate hard mask, even inside a commitment period. With no accepted timings, the cold policy remains
at baseline rather than claiming a measured benefit.

## Host and renderer integration

`NativeGameGraphics.EnableAdaptiveVisibility` defaults to true. Its existing `Render(Native3DScene, ...)`
entry point calls `NativeVisibilityPolicy3D` before/after rendering. The policy applies to resident
`NativeInstanceSet3D` visibility-buffer scenes; the visibility-buffer path remains explicitly enabled
through `Renderer.EnableVisibilityBuffer`. Other paths retain their existing controls and LOD0 fallback.
LOD availability is inspected once per immutable scene revision. Policy observations require no GPU
visibility-counter readback and no per-frame CPU instance traversal.

For manual qualification or a fixed authored policy, set `EnableAdaptiveVisibility = false` before
setting the renderer's `EnableHierarchicalZ` and `LodPixelError` directly. Low-level renderer users can
use the same `NativeVisibilityPolicy3D` bridge, supplying the admitted HZB capability and calling
`BeforeFrame`/`AfterFrame` around their existing renderer call. The renderer assembly has no Dominatus
dependency; policy ownership remains in the runtime/host layer.

The renderer now prepares both ordinary and HZB cull pipelines plus reusable HZB buffers for compatible
resident visibility-buffer scenes. Switching execution no longer destroys material tables, cullers,
instance streams or transport atlases. Resources remain resident while HZB is off. Current-frame rescue
still guarantees that stale HZB predictions cannot remove visible geometry. Both raster paths keep
their stable order and selected-LOD identity mapping.

## Evidence

Run:

```powershell
pwsh -NoProfile -File tools/aurelian-rendering-policy/Qualify.ps1
```

The script builds/tests the solution and compiler, runs native adaptive-policy cases plus all five
previous R2 native proofs, publishes a separate delivery-only consumer and the starter game, and
compares the 225 existing captures against the preceding qualified LOD output. Final evidence lives
under `artifacts/local/rendering-policy/final/`.

The delivery-only consumer loads the actual generated forest GLB/LOD sidecar in a fresh publish. It
requires no meshoptimizer/compiler files, no such dependency entries and no loaded meshoptimizer
process modules. The starter's fresh publish receives the same file/dependency check. This checks the
deployment graph as well as the absence of native simplifier calls in the delivery loader.

The native policy proof exercises the same bridge used by the game host. It verifies real Dominatus
traces, timing attribution, image equivalence with HZB disabled at the chosen LOD, and zero steady
resident instance visits/uploads across policy switches. The final native qualification selected:

| Workload | Selected mode | Observed complete GPU cost |
|---|---|---:|
| Unoccluded plane | Baseline | 0.392 ms versus 0.586 ms HZB |
| Dense occluded forest | HZB | 0.871 ms versus 8.155 ms baseline |
| Distant prepared forest, HZB unavailable | LOD | 1.433 ms versus 5.804 ms baseline |
| Distant prepared forest, all four modes | LOD | 1.415 ms versus 4.123 baseline / 8.115 HZB / 2.757 both |

These are online EMA observations collected in a changing GPU clock environment, not simultaneous
counterfactual timings or universal speed ratios. The final rerun records fresh costs, transitions,
full-loop throughput and measured policy CPU overhead. Median policy CPU cost is 0.0028–0.0031 ms.
All 1,589 Aurelian tests and 1,730 compiler tests pass, with no failures or skips.
Fixed-control LOD/HZB proof modes remain in
the qualification to independently check the mechanisms and their motivating workloads.

The initial attempted negative control assumed the earlier open-forest HZB ordering would repeat;
the controller selected HZB within the competitive measured-cost gate, so that expected-mode assertion
failed. The rejected run is retained. The negative control now uses a single unoccluded plane where
HZB provides no geometry rejection; it tests an actual overhead case without assuming a forest's
performance ordering is invariant. Unit tests also exercise all four utility choices, commitment,
hysteresis, immediate withdrawal and stale/duplicate queued observations.
