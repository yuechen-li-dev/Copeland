# R2: two-phase hierarchical depth occlusion

The visibility-buffer path now supports GPU temporal HZB selection and conservative current-frame
rescue. A dense occluded Forest drops from about 6.3–6.5 ms to 0.79 ms GPU frame time in the initial
experiment, with identical final pixels and triangle IDs. Sparse occlusion is approximately neutral;
the open Forest regresses from about 0.68 ms to 1.06 ms. HZB remains opt-in.

## Select it

```csharp
renderer.EnableVisibilityBuffer = true;
renderer.EnableHierarchicalZ = true;
renderer.Render(residentScene, worldToClip, eye, clear);
```

The existing immutable `NativeInstanceSet3D` remains the admission boundary. `NativeGameGraphics`
supplies `HierarchicalZPrograms` automatically; direct construction supplies `HzbCull3D.v.ts` and
`HzbReduce3D.v.ts` through `RetainedModel3DPrograms.HierarchicalZ`. The mode requires visibility-buffer
and indirect-cluster programs, seven compute storage bindings, and transfer-source support for D32 depth.
Unsupported configurations fail with `AUR-HZB-004`. The existing resident limits remain, and pyramid
admission checks the 16-level bound, device storage range and reduction-dispatch limit.

## The two phases

1. Reproject conservative cluster bounds into the previous final HZB to select an early set. Current
   frustum/cone checks still apply. Without history, the early set is empty.
2. Rasterize that set in the current camera, preserving MASK coverage, and build a max-depth pyramid.
3. Keep every seed candidate. Test the remaining current candidates against this current-frame pyramid.
   This step rescues disoccluded geometry and handles arbitrary camera changes without trusting stale depth.
4. Rasterize the accepted set in its original stable order, then build the final HZB for the next frame.
   The existing material resolve and surface consumers follow.

The final raster clears its attachments and redraws the seed set as part of the accepted sequence.
This costs more than appending only late geometry to loaded attachments, but preserves the renderer's
existing first-writer semantics for equal-depth triangles across instances and clusters. Switching to
depth-less-or-equal or perturbing depth to repair reordered ties would change those semantics. The extra
seed raster cost is included in all measurements.

The temporal selection/current-frame rescue structure is informed by the
[Nanite presentation](https://advances.realtimerendering.com/s2021/Karis_Nanite_SIGGRAPH_Advances_2021_final.pdf)
and [GPU Gems' temporal occlusion discussion](https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-6-hardware-occlusion-queries-made-useful).
This implementation uses the existing indexed cluster and indirect-draw path; it does not implement a
Nanite LOD hierarchy, software rasterizer or analytic geometry bridge.

## Conservative depth tests

Depth uses the renderer's conventional zero-to-one range with a clear value of one and a less test.
Each pyramid parent stores the maximum of its children. Odd dimensions round up, repeating edge texels
where necessary. Background and MASK holes therefore prevent false occlusion.

The existing conservative world-space cluster sphere supplies an enclosing AABB. All eight corners
are projected; a nonpositive w or a near-plane intersection falls back to visible. The screen rectangle
expands by two pixels, and the closest bound depth must exceed the queried maximum by 0.00001. The mip
selection guarantees the expanded rectangle fits inside at most two cells per axis; all four cells are
queried. Extra area can reduce culling but cannot create occlusion. Bounds outside the previous viewport
also remain eligible for the seed set.

The seed test is a prediction only. Even an obsolete previous view merely chooses a different subset
of real geometry to rasterize in the current view. Current-frame depth, not old depth, rejects remaining
candidates. Scene revision or mode changes discard the predictor with the existing timeline retirement.

## Storage, ownership and the frame tape

D32 images now permit `DepthAttachment | TransferSource`. A GPU depth-to-buffer copy provides mip zero,
and the owned compute backend reduces the remaining levels in storage buffers. No CPU depth readback,
compute image ABI, or raw-HLSL path is used during normal frames. See the
[Vulkan image-to-buffer copy contract](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdCopyImageToBuffer.html).

Two GPU atlases hold current seed depth and previous/final history. Camera records for early and final
selection are separate in each frame slot; recording the final phase cannot overwrite the early phase's
parameters. History itself follows graphics submission order, so it represents the preceding queued
frame rather than whichever frame slot was recycled. Stable prefix scans and compaction preserve source
ordering. GPU seed masks distinguish predicted geometry from candidates needing current-frame testing.

The flat frame tape declares seed culling, seed depth, seed pyramid, final culling, final identity
raster, and history pyramid, including the persistent history output and intermediate hazards. Pack
barriers cover depth-copy/reduction, argument reuse, and command-mirror reuse between the two rasters.
GPU timestamps expose all four added stages. Pyramid storage is approximately 2.46 MB at 640x360;
the existing visibility command mirrors and atlas remain. Both depth copies, both reductions, and
the additional raster/compaction are included in timing.

No Aetheris source or dependency was added. All new kernels use the existing Copeland compute binder,
VD-MIR and validated SPIR-V backend.

## Native qualification

```powershell
pwsh -File tools/aurelian-rendering-hzb/Qualify.ps1 `
    -Baseline artifacts/local/rendering-r2-hzb/before `
    -Output artifacts/local/rendering-r2-hzb/final
```

The pre-HZB binaries were saved outside the repository from the qualified visibility working tree,
and all nine existing proof modes were captured before editing. The previous visibility qualification
and its source manifest identify that baseline; Git HEAD alone does not include those uncommitted changes.
The command builds/tests `Aurelian.slnx`, runs the full compiler suite, executes all four R2 native proofs,
compares 225 baseline captures and records source/binary hashes.

`--rendering-hzb` verifies exact image and integer-ID equality with HZB disabled. It checks cold startup,
warm history, open and occluded Forests, camera cuts and reveals, near-camera bounds, MASK holes,
occluder removal, coplanar ordering, shadows/TAA, queued camera changes and in-flight source revisions.
The odd-size 641x359 fixture checks reduction edges. Both complete GPU mip atlases must equal an
independent CPU max reduction. Every rejected current candidate must also be hidden by an independent
double-precision projection and direct mip-zero rectangle scan; conservative extra survivors are allowed.

Timings use an RTX 3070, native 640x360 output, tiered compilation disabled, ABBA order, 128 warmup frames
and 96 samples. CPU preparation, complete GPU frame time and drained frame-loop throughput are recorded.
The timed cases disable shadows, TAA, AO and bloom. Admission, capture and qualification readback are
outside timing. Steady frames must report zero CPU instance visits and zero instance uploads. Clocks
remain unlocked. Both dense-occlusion repetitions must improve complete GPU frame time by at least 25%.

The dense specimen contains 20,000 procedural trees, 23.04 million source triangles plus a two-triangle
wall, and 64 tree materials. Trees occupy a compact visible region behind the wall, so frustum culling
alone cannot remove their geometry. The sparse occluded and open specimens retain the original wide
Forest placement as negative controls. Initial logs and evidence are retained under
`artifacts/local/rendering-r2-hzb/diagnostic-3/` and `diagnostic-4/`; complete final qualification is under
`artifacts/local/rendering-r2-hzb/final/`.

In the initial sparse occlusion experiment, 10,973 frustum/cone commands become one after history warms.
The first frame has an empty seed and draws the complete current set, demonstrating safe bootstrap.
The open Forest retains 6,468 of 10,972 commands but loses performance to setup and the additional raster.
HZB is useful when geometry rejection pays for that work; it is not enabled globally from these results.

## Final-source results, 2026-11-03

The full build passes with zero warnings/errors. All 1,582 Aurelian tests and 1,730 compiler tests
pass with no skips. The final HZB native proof passes synchronization validation, exact pixel/ID
comparisons, complete CPU pyramid reduction and the independent base-depth rejection checks.

| Workload | HZB off GPU frame | HZB on GPU frame | HZB off frame-loop FPS | HZB on frame-loop FPS |
| --- | ---: | ---: | ---: | ---: |
| Sparse occluded Forest | 0.752–0.842 ms | 0.753–0.757 ms | 1131.7–1279.9 | 1173.2–1258.9 |
| Dense occluded Forest | 6.270–6.274 ms | 0.784–0.786 ms | 155.5–155.6 | 1108.7–1209.0 |
| Open Forest | 0.684–0.685 ms | 1.059 ms | 1371.8–1407.3 | 916.0–918.7 |

The dense workload improves complete GPU frame time by about eight times. CPU preparation rises from
0.560–0.573 ms to 0.595–0.700 ms, so throughput becomes partly CPU limited. Sparse occlusion remains
approximately neutral, and the open case clearly regresses. All repeated controls, including the first
sparse raster sample at 0.842 ms, remain in the evidence.

After the dense history warms, 180,001 frustum/cone commands become one seed command and one final
command. Cold startup draws all 180,001 candidates. A camera-cut fixture grows from one seed candidate
to 11,023 final candidates, explicitly rescuing 11,022 in the current frame. Queued camera changes and
source replacements preserve captured pixels; MASK holes and coplanar tie fixtures preserve full IDs.
Both 640x360 atlases together occupy 2,457,952 bytes. The complete acceptance and source/binary hashes
are recorded in `artifacts/local/rendering-r2-hzb/final/qualification.json`.
