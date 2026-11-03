# R0: retained geometry and pipelined native frames

Status: qualified on the local RTX 3070 against `71b795de`.

R0 removes repeated model expansion/upload from retained scenes and lets the CPU prepare the next native
frame while the graphics queue executes previous work. It keeps the current lighting, material fragment
shaders, temporal resolve, bloom, water, and transparency algorithms.

## Ownership and execution

- `NativeMesh3D` owns immutable, validated local vertices and uint32 triangle indices. Asset identity is
  separate from instance identity and temporal correspondence. A mutable legacy vertex array is never
  assumed immutable merely because its `TemporalIdentity` stayed the same.
- `GpuMesh3D` uploads indexed geometry to device-local buffers. `SceneInstance3D` supplies material,
  transform, and optional authored previous transform. The renderer otherwise retains previous transforms
  by instance identity and mesh reference.
- Matching mesh/material instances share indexed instanced draws. Existing material fragment SPIR-V is
  linked unchanged to an instanced vertex stage; normals use the inverse transpose and tangents are
  re-orthogonalized. Directional cascades and spot shadows consume the same mesh and instance data.
- `VulkanFrameInFlightSet` owns two retirement slots on the existing graphics queue. Beginning a slot waits
  for that slot's prior submission, rather than the just-submitted frame. Uniforms, changing descriptor sets,
  CPU-written vertices, particle commands, and query pools have distinct storage per slot.
- GPU-only attachments and temporal images remain shared on the ordered graphics queue. An explicit
  inter-frame dependency orders their reuse. Changed local lights upload through frame-owned staging in
  the scene command buffer. Removed meshes/material bindings retire after their last submitted use.
- Native shape/text overlays have separate frame vertex buffers; their material bindings remain immutable
  while in use. The swapchain copy submits asynchronously and signals a binary semaphore indexed by
  acquired swapchain image. Presentation waits on that semaphore. Image acquisition still uses the existing
  acquire fence. Resize and destruction drain outstanding device work.

This follows Vulkan's [frames-in-flight ownership](https://docs.vulkan.org/tutorial/latest/03_Drawing_a_triangle/03_Drawing/03_Frames_in_flight.html)
and [presentation semaphore reuse](https://docs.vulkan.org/guide/latest/swapchain_semaphore_reuse.html) rules.
R0 uses one queue; queue scheduling and render-graph compilation belong to R1.

## Game and compatibility paths

`NativeGameGraphics` defaults to interactive submission, including menus and presentation. Its
`enableFramesInFlight: false` option supplies a serial control for qualification.

`SceneGeometry3D.BuildScene(frame, cache)` retains imported models and opaque material primitives through a
scene-owned `SceneGeometry3DCache`. Cloudstep uses this during ordinary native play. Legacy `BuildScene(frame)`
and `NativeModel3DBatch` remain supported for existing callers and dynamic world-space geometry.
Beacon currently uses its existing immediate geometry and benefits from pipelined submission.

Scripted game playtests select Capture scheduling and the legacy projection for their entire run, preserving
their existing specimens and temporal sequence. They do not switch geometry paths only for a screenshot.
Direct renderer calls default to Capture mode; an explicit capture waits and reads back even in Interactive
mode. The R0 proof also checks serial/interactive equivalence on the retained path itself.

Current explicit budgets are 65,536 retained instances per frame and 256 MiB of active retained mesh data.
Pending retired meshes may temporarily coexist with the active set until their timeline completes.
Overflows reject with `AUR-MESH` diagnostics. The legacy immediate/frame geometry limits remain explicit.

Blended and refractive materials continue through the existing forward batch path. Planar reflections
expand retained instances through the compatibility adapter for that secondary view. Optional external
GPU producers, asset uploads, environment replacement, and topology-changing reallocations may still wait;
R0 does not claim that every optional producer is asynchronous.

## Reproduce and evidence

Run from the repository root:

```powershell
pwsh -File tools/aurelian-rendering-r0/Qualify.ps1
```

The script builds and tests `Aurelian.slnx`, runs the native R0 proof with synchronization validation, reruns
nine existing graphics proof modes, and compares every pre-change PNG under `artifacts/local/r0/before`
with its corresponding new capture. Missing baselines, differing images, validation errors, and command
failures reject qualification. Baseline files are local evidence, not replacement golden images.

The native proof covers indexed/legacy identity captures, a textured quad sharing four vertices across
two indexed triangles, moving nonuniformly scaled instances with TAA
and bloom, dynamic lights/material retirement, 5,000-instance geometry scaling, and actual Beacon/Cloudstep
views through native rendering, menu overlays, swapchain presentation, capture, and resize.

Timing boundaries distinguish CPU preparation/recording, frame-slot waits, submission, and completed
whole-host throughput. The GPU inter-scene gap includes UI, copy, presentation, and submission gaps;
it is not labeled pure GPU idle time. Serial controls run at the same checkout and settings. Window-system
pacing can limit throughput even when CPU submission cost improves. Driver/device identity is recorded.

## Qualified results

`artifacts/local/r0/final/qualification.json` accepts the complete run: Release build, **1,532 tests in
33 assemblies (zero failures/skips)**, nine graphics proof modes, and **225 byte-identical pre-change PNGs**.
The native R0 proof reports no validation errors with synchronization validation enabled. Beacon and
Cloudstep also pass their actual `--launch-smoke` shell entrypoints with validation enabled.

The final native R0 run disables .NET tiered compilation for matched controls and excludes warmup frames.
The 5,000-instance specimen repeats triangle geometry to isolate geometry submission scaling; it is not
a qualification of world-scale geometry, culling, streaming, or LOD.

| 5,000-instance specimen | Legacy completed frame median | Retained CPU frame median | Retained prepare/record median | Steady geometry uploads |
| --- | ---: | ---: | ---: | ---: |
| 36 source vertices per mesh | 147.72 ms | 3.32 ms | 3.18 ms | 0 bytes |
| 144 source vertices per mesh | 199.64 ms | 3.51 ms | 3.38 ms | 0 bytes |

Both retained cases issue one mesh/material draw group per pass and upload 720,000 instance bytes per
frame. The legacy cases expand/upload 11,520,000 and 46,080,000 vertex bytes respectively, with per-batch
draws. Four times the source vertices increased retained preparation/recording by about 6.4% in this run.
The legacy comparison includes CPU expansion and synchronous completion; the retained median includes
slot waits during sustained pipelined operation. These are explicitly different execution paths testing
R0's intended improvement, rather than isolated shader benchmarks.

Actual 1280x720 game views, native menu overlays, copy, presentation and resize use the production host:

| Game | Serial / interactive 3D submit median | Serial / interactive GPU inter-scene gap median | Serial / interactive completed host frame mean |
| --- | ---: | ---: | ---: |
| Beacon | 1.446 / 0.166 ms | 8.805 / 5.942 ms | 7.641 / 7.775 ms |
| Cloudstep | 2.098 / 0.188 ms | 5.103 / 3.785 ms | 7.556 / 7.732 ms |

The measured host benefit is lower 3D submission cost and smaller inter-scene gaps. Whole-window
throughput stayed roughly flat, with a small regression in these runs; R0 does not establish an FPS
improvement for these small, presentation-paced scenes. Cloudstep's retained view has 28 instances in
three draw groups; Beacon retains its immediate geometry path.

The first four original baseline commands accidentally shared an output directory. Their separate
captures were recovered by rebuilding the unchanged Git sources in an isolated local evidence tree;
`artifacts/local/r0/baseline-recovery.json` records the revision, source archive hash, capture counts and
the original presentation timing-proof failure. No current output was substituted for a baseline.
The extracted build was preserved in the task's temporary directory after recovery, leaving the source
archive and captures under `artifacts/local/r0`.

## Findings retained during implementation

The pre-change presentation proof failed because it expected 13 timing stages while the current renderer
already exposed 14, including `particles-update`. Its original failure is retained in
`artifacts/local/r0-before-presentation-graphics.log`; the expected stage list now matches the renderer.

The first whole-host R0 experiment exposed `SYNC-HAZARD-READ-AFTER-WRITE` from the final attachment layout
transition to the asynchronous swapchain copy. The original experiment is rejected in
`artifacts/local/r0/qualification-1/evidence.json`. The render-pass dependency now explicitly includes
transfer reads. A debug-utils collector makes subsequent validation errors fail the native proof directly.
