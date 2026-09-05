# AURELIAN-NATIVE-LAYER-COMPOSITOR-M0

## Decision

**Outcome A — native layers compose cleanly in one frame.**

The native compositor presents the qualified textured world at semantic z 0, then a real Machina analytic/MSDF overlay at semantic z 100, into one compositor-owned Vulkan `R8G8B8A8_UNORM` target. The canonical path uses three compatible direct passes: world `CLEAR`, analytic UI `LOAD`, MSDF text `LOAD`, followed by one final readback. It allocates no intermediate color surfaces and records no composition copy or blit.

`Aurelian.Composition` remains dependency-free and unchanged. `Aurelian.NativeComposition` is the thin native integration package. World semantics remain in `Aurelian.GameWorld2D`; UI semantics remain in Machina; pipeline/resource realization remains with each presenter and `Aurelian.Graphics`.

## Existing compositor audit

| Concern | Existing behavior | M0 limitation | Change |
| --- | --- | --- | --- |
| Identity/order | `LayerId`; `(ZOrder, LayerId ordinal, registration sequence)` | native Vulkan did not consume that ordered result | native realization iterates `RunFrame` presentation DTOs exactly as returned |
| Visibility | `Enabled` descriptor plus compositor override | no native effect | disabled layers have no native presentation call or resource churn |
| Viewport/surface | typed viewport, extent, scale and host/offscreen kind | no native target binding | bounded native frame context carries the semantic viewport and target extent |
| Lifecycle | `Attach/Resize/Update/Present/Detach` | native resources were outside it | one native presenter contract mirrors target-bearing attach/resize/present/detach |
| Input | hit-test/opaque policy, focus and capture routing | none; rendering was simply separate | unchanged and still independent of native realization |
| Presentation mode | direct host pass or explicit offscreen surface | Vulkan compositor accepted one passthrough output | direct mode is realized; explicit offscreen remains explicit and is deterministically rejected until an isolation/effect implementation is supplied |
| Plant output | one passthrough source copied to a target | cannot express world plus UI | ordered renderers now bind one native frame target directly; plant passthrough remains intact |

## Native renderer audit

Before M0, each `VulkanOrderedQuadRenderer` owned its render target, clear render pass, framebuffer, command pool, submission and readback. Every `End2D` cleared and transitioned its private target to transfer-source layout. Textured sprite, MSDF and analytic pipelines already shared format, sample count, no-depth policy and straight-alpha compatibility where needed, but they could not retain prior content because target ownership and load operation were fixed inside each renderer.

M0 adds an optional shared-target constructor while preserving the standalone constructor and hashes. A shared renderer still owns its pipeline, descriptors, sampler, vertex buffer, atlas/texture handles and command recording. It does not own or replace the target. Direct compatibility requires the same Vulkan plant, target extent, `R8G8B8A8_UNORM`, sample count 1, and the existing no-depth color-pass contract.

## Composition models

| Model | Result |
| --- | --- |
| A: mandatory offscreen layer textures plus blend pass | rejected for the canonical case; it adds two color surfaces and a blend/copy pass with no semantic need |
| B: ordered direct native passes | sufficient for current compatible world and Machina pipelines |
| C: hybrid | chosen law: direct same-target passes by default; `OffscreenSurface` remains the explicit semantic request for future isolation/effects |

M0 implements the direct half needed by the canonical case and fails closed for an explicit offscreen request. It does not silently pretend an incompatible direct layer succeeded and does not introduce a render graph.

## Native frame and pass law

```text
VulkanNativeFrameTarget.BeginFrame(clear)
  Present(WorldLayer)
    ForwardTextured sprite pass: CLEAR, store -> transfer-source
  Present(MachinaLayer)
    AnalyticShape2D pass: LOAD transfer-source, store -> transfer-source
    MsdfText pass: LOAD transfer-source, store -> transfer-source
VulkanNativeFrameSession.EndFrame
  one final transfer-source readback/present boundary
```

`VulkanNativeFrameTarget` owns final target creation/disposal, extent/format/sample compatibility, clear color, frame lifetime and final readback. `VulkanNativeFrameSession` is the only issuer of direct passes; it makes the first pass clear and every subsequent pass load. Calling standalone `End2D` on a shared renderer is rejected, so a later layer cannot independently clear. A renderer bound to another target is rejected before command recording.

Sequential pass submissions wait for completion in the existing command-submit path. The prior pass final layout and later pass initial layout are both transfer-source; render-pass dependencies provide the existing color attachment ordering. No extra barrier exists between compatible direct color passes. A future offscreen producer-to-consumer handoff must add an explicit shader-resource transition; that is outside M0.

## Ownership and vocabulary

- A **semantic layer** is an `IAurelianLayer` ordered and routed by `Aurelian.Composition`.
- A **native target** is the final Vulkan color attachment owned by the frame compositor.
- An **offscreen surface** is an explicitly requested isolation/effect input, not the representation of every layer.
- The **final output** is the target after all ordered direct passes and before its one readback or future swapchain presentation.

The compositor owns order, target, clear and frame lifetime. Presenters own submissions, texture/font atlases, pipelines and their deterministic disposal. The compositor never interprets world sprites, Machina shapes or glyphs.

## Qualification result

The canonical 1280x720 scene contains the M1 tile floor, wall/occluder, player, NPC, animated object, transparent sprite and camera, overlaid by a Machina-authored rounded status panel, hotbar, analytic borders and native MSDF status text. The UI overlaps world pixels. Pixel oracles prove a transparent rounded corner preserves the exact world RGBA, an opaque/semitransparent panel region changes the world, and an MSDF ink pixel changes the analytic-only frame. World `(Layer, FeetY, StableId)` order remains internal; top-level order is exactly `world`, then `machina-ui`.

| Evidence | Result |
| --- | --- |
| Direct layers | world + Machina analytic + Machina MSDF on one target |
| Intermediate color surfaces | 0 |
| Composition copy/blit | 0 |
| Render passes | 3 (1 clear, 2 load) |
| Draw calls | 9 total in the canonical capture |
| Warm stress | 100 frames; stable hash; no target recreation, atlas upload or descriptor write |
| Resize | 1280x720 -> 2560x1440 recreates the target after presenters release old framebuffers; camera/UI submissions reproject |
| Hidden/detached | hidden UI disappears to the world-only hash; detached presenter is not called |
| Negative compatibility | another target is rejected before recording |
| Disposed target | beginning another frame throws cleanly |
| Validation | Khronos validation enabled on NVIDIA GeForce RTX 3070; zero reported errors |
| Canonical hash | `539030cf04b60cad870114569eaf05479c2de2b7c20b644339714005278cf3dd` |

The executable qualification and compact evidence live in `tools/Aurelian.NativeLayerCompositorM0` and `artifacts/aurelian-native-layer-compositor-m0`. The curated image is `world-machina-1280x720.png`.

Local validation:

- `dotnet test Aurelian.slnx -m:1`: 665 passed.
- `dotnet test Machina.UI.slnx -m:1`: 739 passed.
- `dotnet test TinyFarm.slnx -m:1`: 273 passed.
- `dotnet test JointTaskForce.slnx -m:1`: 3,434 passed.
- Focused native frame/render-pass tests: 12 passed.
- Renderer-neutral compositor: 10 passed; GameWorld2D: 11 passed.
- Native M1/M2/M3/M4/M5 qualification executables all reported Outcome A with Vulkan validation enabled and zero errors.
- `git diff --check`: passed.

No remote CI was run.

## Compatibility and future extension

A swapchain target can implement the same target/frame boundary without changing semantic layers or presenters. Future ordering is straightforward: `World`, `Particles`, `Machina`, `Debug`. Particles are not implemented here. Bloom, distortion, blur and post-processing may use explicit offscreen mode later; M0 adds no DAG, transient aliasing planner or graph compiler. Editor gizmos and inspection overlays can become ordinary ordered layers without unifying world and Machina semantics.

## Exact next milestone

Proceed to **`AURELIAN-GAME-HOST-INPUT-M2`**: host/window lifecycle, swapchain target, keyboard/gamepad action sampling, UI capture/focus-loss release and deterministic shutdown. Do not add physics, audio or application gameplay semantics to the compositor.
