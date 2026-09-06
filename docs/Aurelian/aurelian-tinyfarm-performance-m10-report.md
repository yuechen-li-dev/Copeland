# Aurelian TinyFarm Performance M10 Report

## 1. Outcome

**Outcome B — normal gameplay is smooth and the catastrophic paths are gone; one bounded changed-UI hitch remains.**

The production client now renders and presents through Vulkan without reading the frame back to the CPU. The canonical 60-second warm trace sustained 144.0 measured frames/second with 6.949 ms p95, 7.038 ms p99, a 7.404 ms maximum, 41,180 managed bytes/frame, zero readbacks, zero descriptor writes, and no frame above 16.67 ms. The remaining first changed-state frames were 27.258 ms for combat and 25.408 ms for a secondary scene. Their immediately repeated frames were 2.189 ms and 2.161 ms excluding explicit evidence capture, isolating the hitch to CPU Machina rasterization and synchronous upload of changed UI—not gameplay, effects, shader compilation, or steady Vulkan drawing.

## 2. Baseline machine and configuration

- Windows 10.0.26200, x64
- NVIDIA GeForce RTX 3070
- .NET SDK 10.0.400; .NET runtime 10.0.11
- 1280x720 native client
- M10 evidence: Release, Vulkan validation enabled for the proof run
- Swapchain: three images, `PresentModeFifoKhr`
- Internal and window resolution are both 1280x720; no render-scale quality reduction was used

## 3. M9 baseline

The checked M9 120-frame sample averaged 28.232 ms/frame (35.42 FPS), had a 27.371 ms median and 29.342 ms p95, allocated 4,137,503 bytes/frame, rebuilt the full UI 130 times, and read back a 1280x720 GPU frame every ordinary frame. M9 did not record p99, maximum, draw/resource telemetry, or collection counts, so those before-values are reported as unavailable rather than invented.

## 4. What caused the visible stutter?

1. **Routine full-frame GPU readback and CPU presentation** was dominant: the mandatory readback array alone was 3,686,400 bytes/frame. Removing the readback/CPU channel-conversion/WinForms route accounts for most of the 21.29 ms average improvement and eliminates its per-frame GPU-to-CPU synchronization.
2. **Full 1280x720 Machina raster rebuilds for clock and prompt changes** produced large CPU buffers, hashing, and texture churn. M9 rebuilt 130 times in the representative run; M10 split stable base UI from bounded clock and prompt surfaces and rebuilt the full UI only 10 times in the complete walkthrough.
3. **Transient native render arrays and effect snapshots** allocated every pass/frame. Persistent resizable vertex/key buffers and reusable effect lists removed these arrays.
4. **Texture recreation for changed content** destroyed and recreated GPU resources and descriptor identities. Same-size content now updates the existing texture in place.
5. **Unbounded animated tint identities** slowly created new particle material descriptors. Native particle fade is now a visually bounded four-state projection; warm descriptor writes are zero.
6. **Remaining changed-UI realization** still rasterizes and uploads a changed CPU surface synchronously. It causes the isolated 25–27 ms first-change frames and is the reason for Outcome B.

## 5. Allocation-site audit before fixes

Only the first item had an individually measured byte count in M9. The remaining ordering comes from the hot-path source audit and before/after phase instrumentation; it must not be read as a sampled-profiler byte ranking.

| Rank | Site | M10 disposition |
| ---: | --- | --- |
| 1 | `VulkanNativeFrameTarget.Capture` readback array (3,686,400 B/frame) | Explicit capture only |
| 2 | Windows CPU RGBA/BGRA display realization | Removed from native production presentation |
| 3 | Full-screen UI RGBA buffer | Stable base cached; dynamic surfaces are small |
| 4 | `RasterSurface.CopyPixels` clone | Direct RGBA8 copy added |
| 5 | UI channel-conversion array | Removed |
| 6 | Per-pass vertex byte array | Persistent resizable buffer |
| 7 | Per-pass binding-key array | Persistent resizable buffer |
| 8 | Particle snapshot array | Reusable list |
| 9 | Particle native-submission array | Direct per-particle projection |
| 10 | Effect quad snapshot array | Reusable list |
| 11 | Shockwave projection array | Still bounded and event-only |
| 12 | `TinyFarmFrameProjector` view snapshots | Retained clear immutable boundary; 11,328 B/frame measured in steady state |
| 13 | Native compositor result/pass arrays | Retained; part of 8,315 B/frame composition phase |
| 14 | Swapchain/compositor diagnostic result objects | Retained; 2,688 B/frame measured |
| 15 | UI invalidation string joins | Replaced by typed key and looped inventory hash |
| 16 | Objective string array used only for invalidation | Replaced by objective bit mask |
| 17 | Stable sprite `ReadOnlySpan.ToArray` | Byte-array upload overload avoids the copy |
| 18 | Stable sprite destruction/recreation | In-place update |
| 19 | Per-draw guarded encoder result objects | Warm internal batch records validated Vulkan commands directly |
| 20 | Save serialization/compression on gameplay thread | Immutable snapshot captured, serialization/compression/IO moved off-thread |

## 6. GPU synchronization audit

Ordinary gameplay performs no framebuffer readback, blocking map, `QueueWaitIdle`, or `DeviceWaitIdle`. FIFO acquire/present synchronization remains required. The native compositor still uses one completion-waiting submission for each of two layer passes and one for the swapchain copy. Those three waits are avoidable with a frames-in-flight/ring-buffer design, but they are not the current frame blocker: the 60-second trace remains below 7.04 ms at p99. They are recorded explicitly in `gpu-sync.json` rather than hidden.

`DeviceWaitIdle` remains only in resource/target disposal. Upload helpers wait when content genuinely changes. Validation guards remain enabled in proof/qualification builds and are disabled for the production client; the warmed internal draw batch avoids rebuilding public diagnostic result objects for every draw.

## 7. Readback fix

The Silk window now creates the Vulkan surface and swapchain. `VulkanNativeSwapchainPresenter` acquires an image, copies the completed native target to it on the GPU, and presents it. `CaptureNextFrame` is the only TinyFarm route that asks `VulkanNativeFrameTarget` for pixels. The proof counted zero ordinary readbacks; seven explicit screenshot frames still exercised and validated readback.

BGRA swapchain targets are supported without changing screenshot semantics: explicit capture converts BGRA to canonical RGBA before hashing/PNG output.

## 8. UI invalidation fix

The UI is split into a stable 1280x720 base, a 400x34 clock surface, and a 710x38 interaction-prompt surface. A typed `SupperUiKey` tracks only semantic base dependencies. Unchanged UI performs zero topology/layout rebuilds; twelve repeated idle frames produced zero rebuilds. Dynamic content updates preserve the existing GPU texture and descriptor identity. The 60-second trace contained 12 legitimate small clock/prompt content uploads and zero uploads for unchanged resources.

Text and glyph work inherits this invalidation: identical strings reuse their raster resource and never reshape/rebuild on a stable frame. This milestone did not add a second text cache or UI framework.

## 9. Resource churn and buffer lifetime

- Vertex bytes and material binding keys are persistent, capacity-grown arrays.
- Effect particle and quad snapshots use reusable lists.
- Same-ID, same-size sprite changes upload into the existing Vulkan texture.
- Stable textures, samplers, descriptor sets, pipelines, shader modules, and atlases are reused.
- Warm trace: 0 descriptor writes, 0 unchanged texture uploads, 2 vertex-buffer uploads/frame, 3 submissions/frame.
- Retained managed memory stayed bounded during the ten-minute stress partition (approximately 13.47 MB at the sampled points).

## 10. Shader and pipeline warmup

Visual TypeScript compilation, VD-MIR lowering, SPIR-V production, Vulkan pipeline creation, atlas realization, and compositor attachment occur before the active game loop. The canonical interaction walkthrough observed no gameplay shader or pipeline compilation. The combat checkpoint retained the SoftShockwave pass; no effect or audio feature was disabled.

## 11. Present mode, pacing, and frames in flight

FIFO was selected deliberately for vsync correctness; there is no accidental half-refresh or `Sleep` pacing path. The old `Thread.Sleep(8)` loop was removed. The three-image swapchain can receive 60 Hz frames, although command completion is still CPU-serialized today. A later frames-in-flight change should be made in Aurelian.Graphics, not as a TinyFarm workaround.

## 12. Phase breakdown

Representative normal movement in Release:

| Phase | ms/frame | allocated B/frame |
| --- | ---: | ---: |
| Presentation projection | 0.067 | 11,328 steady |
| Native composition (Machina/world/command recording) | 1.621 | 8,315 steady |
| Swapchain copy/acquire/present | 4.824 | 2,688 steady |
| Host, input, simulation, resolver, dialogue, Spatial2D, effects, audio, misc | 0.475 | remainder |
| **Total** | **6.986 representative / 6.946 long-run** | **41,180 long-run** |

InputMan profile parsing and TOML IO do not occur during play. Audio remained at one active voice maximum and did not block the gameplay thread. Effects reached 232 particles/one emitter in stress without capacity growth or retained-memory growth. Actor/effect counts remain small enough that stable painter sorting was left simple.

## 13. Before/after metrics

| Metric | M9 before | M10 after | Improvement |
| --- | ---: | ---: | ---: |
| Average frame | 28.232 ms | 6.946 ms | 75.4% lower |
| Median | 27.371 ms | 6.944 ms | 74.6% lower |
| p95 | 29.342 ms | 6.949 ms | 76.3% lower |
| p99 | not captured | 7.038 ms | now gated |
| Max | not captured | 7.404 ms | now gated |
| Throughput | 35.42 FPS | 143.96 FPS | 4.06x |
| Allocation/frame | 4,137,503 B | 41,180 B | 99.0% lower / 100.5x |
| Gen0/Gen1/Gen2 per 60 s | not captured | 9 / 0 / 0 | now gated |
| Readbacks/frame | 1 | 0 | eliminated |
| Descriptor writes/frame | not captured | 0 | warm-zero qualified |
| Stable texture uploads/frame | not captured | 0 | unchanged resources are stable |
| Buffer uploads/frame | not captured | 2 | bounded persistent buffers |
| Draw calls/frame | not captured | 75 | measured, painter-order preserving |
| Submissions/frame | not captured | 3 | measured |
| Full UI rebuilds / representative proof | 130 | 10 | 92.3% lower |

## 14. Sixty-second steady-state trace

The deterministic harness measured 3,600 normal-game frames after startup/first-use warmup. Average was 6.946 ms, median 6.944 ms, p95 6.949 ms, p99 7.038 ms, and maximum 7.404 ms. No frame exceeded 16.67, 25, 33.3, or 50 ms. Allocation was 41,180 B/frame with 9/0/0 collections. It recorded 75 draw calls, 2 buffer uploads, 3 submissions, 0 descriptor writes, and 0 readbacks per frame.

## 15. High-load, transition, and save/load findings

The canonical combat checkpoint retained world, HUD, 232-particle stress capacity, SoftShockwave, and Windows NAudio. Its first changed-UI frame measured 27.258 ms; the repeat without UI change measured 2.189 ms excluding explicit readback. The secondary-scene first changed-UI frame was 25.408 ms; its repeat was 2.161 ms. This is bounded but visible and is the remaining M10 hitch.

The old synchronous convenience proof calls measured up to 152.414 ms save and 48.696 ms load. Production F/N input no longer calls them. F captures an immutable authoritative snapshot, then serializes, compresses, and writes off-thread; N loads and validates off-thread and commits on the host thread. The measured request costs were 0.555 ms save and 0.369 ms load. The largest host pump frames while those operations completed were 17.926 ms and 16.014 ms respectively. Snapshot consistency, load commit ownership, dialogue restore, and semantic hashes remain unchanged.

## 16. Visual, audio, gameplay, and replay parity

All seven canonical screenshots were regenerated through explicit capture. Title, farm, dialogue, farming/pickup, combat, secondary scene, and completion remain present; the combat image includes the shader quad. Windows NAudio remained active. The authoritative final hash matched both semantic replay and the independently restored completion run. No gameplay verb, objective, movement result, inventory rule, dialogue consequence, schedule, scene route, effect, or audio cue was removed or changed for performance.

## 17. Bugs found and fixed

- The native client was not presenting its Vulkan result; it read every frame to CPU and repainted through WinForms.
- Swapchain image layout tracking assumed `Present` before first acquisition; the real first layout is undefined.
- BGRA swapchain capture needed canonical channel conversion.
- Stable same-ID sprite changes recreated textures and descriptors.
- UI invalidation included clock and prompt in the full-surface key.
- Raster RGBA extraction cloned pixels and then allocated a second conversion buffer.
- Particle fade generated effectively unbounded material identities.
- F/N gameplay persistence synchronously waited on serialization/compression/IO.

## 18. Owner-lane changes

- **Aurelian.Graphics:** target format support, explicit capture conversion, persistent draw buffers, warm direct draw recording, in-place texture update, GPU swapchain presenter.
- **Aurelian.Rendering.Raster:** direct RGBA8 extraction.
- **Aurelian.Effects2D / integration:** reusable snapshot copies and bounded native material projection.
- **Aurelian.GameWorld2D integration:** stable texture identity with in-place content update.
- **Aurelian.NativeComposition:** swapchain-compatible target format.
- **TinyFarm native leaf:** Silk Vulkan window/presentation, explicit capture, performance harness, UI invalidation split.
- **TinyFarm Runtime/InputMan application:** immutable save request construction and nonblocking F/N persistence lifecycle. Core resolver authority was untouched.

## 19. Tests and regression gates

Added a focused effect-projection test that proves a particle lifetime maps to exactly four reusable native alpha/material states, and an application test with gated storage proving gameplay save request initiation does not wait for storage. The executable proof gates zero ordinary readback, explicit capture readback, zero warm descriptor writes, zero stable texture uploads, stable UI rebuild behavior, shader/pipeline warmup, bounded allocation, 60 FPS percentiles, save/load request responsiveness, and replay parity.

Final local Release validation:

- `dotnet test Aurelian.slnx -c Release -m:1 --no-restore`: 746 passed
- `dotnet test TinyFarm.slnx -c Release -m:1 --no-restore`: 335 passed
- `dotnet test JointTaskForce.slnx -c Release -m:1 --no-restore`: 3,479 passed
- Native M10 executable proof: passed with Vulkan validation enabled and no validation errors
- `git diff --check`: passed

No remote CI was run. Some solution projects intentionally select their existing Debug dependency configuration even when the test entrypoint is Release; the canonical performance executable itself was built and run Release.

## 20. NativeAOT audit

NativeAOT is not justified by this profile. The dominant defects were presentation topology, readback, invalidation, and buffer lifetime. Runtime startup/AOT work would not address the remaining changed-UI raster/upload hitch.

## 21. What remains before public demo

The public demo should not ship with the 25–27 ms first changed-UI frames if they remain perceptible on the target display. The right fix is to realize Machina text and simple analytic UI directly through the existing Vulkan native text/shape paths, retaining semantic UI ownership in Machina while removing full CPU surface rasterization/upload. After that, Aurelian.Graphics can replace the three completion waits with a two- or three-frame ring only if a GPU timeline proves further value.

## 22. Exact next milestone

**`AURELIAN-TINYFARM-VULKAN-UI-REALIZATION-M10B`**: port TinyFarm's existing Machina HUD/text/analytic panels from full-surface CPU raster textures to the qualified native Vulkan text and analytic-shape realization, prove first combat/scene/UI-change frames below 16.67 ms, and preserve the same screenshots and semantic replay hash. Only after M10B should `AURELIAN-TINYFARM-PUBLIC-DEMO-M11` begin.

## 23. Evidence

Canonical machine-readable evidence is in `artifacts/aurelian-tinyfarm-performance-m10/`: `baseline.json`, `frame-breakdown.json`, `allocations-before.json`, `allocations-after.json`, `gpu-sync.json`, `steady-state.json`, `high-load.json`, `transitions.json`, and `manifest.json`.
