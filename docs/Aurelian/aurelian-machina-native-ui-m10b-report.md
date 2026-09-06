# Aurelian Machina Native UI M10B Report

## 1. Outcome

**Outcome A — native UI removes the changed-state stutter.** TinyFarm's canonical UI now goes from Machina semantic/layout presentation directly to native analytic-shape and MSDF glyph quads. The proof used no CPU raster fallback, raster bytes, dynamic UI texture upload, ordinary readback, or warm descriptor write. Combat fell from 27.258 ms to 8.155 ms and scene change from 25.408 ms to 5.087 ms. Every measured gameplay UI change remained below 16.67 ms.

## 2. Exact old changed-state path

`SupperUi` built a `UiNode`, `MachinaPresentationPipeline` prepared it, `MachinaPresentationTranslator` converted it to resolved 2D operations, and `AurelianCpuRasterRenderer` produced a full RGBA surface. TinyFarm then copied and hashed those pixels into a `SpriteAtlasResource`; `NativeSpriteResourceScope.Resolve` synchronously updated or created the Vulkan texture; one textured quad sampled the result.

## 3. Root cause

The semantic and layout work was not the 25–27 ms blocker. A changed key forced CPU rasterization, a large RGBA allocation/copy/hash, and a completion-waiting upload. An unchanged key returned the cached resource, explaining the approximately 2.2 ms repeat.

## 4. Native primitive inventory

| UI primitive/control | M10 realization | Native support before M10B | M10B action |
| --- | --- | ---: | --- |
| rounded panels, buttons, highlights, overlays | CPU-raster pixels | yes | `MachinaAnalyticShapePrimitive` to `AnalyticShape2D.v.ts` |
| ordinary text | readable bitmap raster | yes | Space Mono glyph runs to `MsdfText.v.ts` |
| rectangular clipping | CPU raster clip stack | yes | intersect semantic clips before quad submission |
| opacity | CPU compositing | yes | preserve Machina RGBA alpha in native fill/tint |
| portrait | persistent raster texture | yes | retain persistent textured portrait layer |
| vector icons | not used by canonical TinyFarm UI | yes | retain qualified vector-MSDF adapter; no duplicate path |
| item/world imagery | native analytic or persistent raster content | yes | unchanged |

## 5. Missing capabilities added

No shader, layout engine, control family, or UI framework was missing. M10B added the TinyFarm native realization/cache, a bundled Space Mono MSDF atlas warmup, and allocation-conscious append APIs on the existing Aurelian glyph adapters.

## 6. Final realization pipeline

`UiNode -> MachinaPresentationPipeline -> MachinaPresentationFrame -> analytic/MSDF adapter -> persistent VulkanOrderedQuadRenderer -> compositor target -> swapchain present`.

Machina remains semantic UI, layout, and interaction authority. `Aurelian.Machina.Graphics` adapts renderer-neutral primitives. Aurelian.Graphics owns Vulkan resources and draws.

## 7. Fallback policy

`AurelianCpuRasterRenderer` remains available for compatibility, tests, and non-native hosts. TinyFarm.Native no longer references it for UI. Canonical proof telemetry recorded zero fallback primitives, bytes, and uploads.

## 8. Text path

All title, HUD, hotbar, journal, dialogue, choice, feedback, pause, inventory, and completion text uses native MSDF. Two persistent 1024-square-capable Space Mono atlas resources cover the bounded printable ASCII UI corpus at 16 px and 24 px. They upload once at startup. Text layout stays on CPU; immutable qualified runs are cached by source, content, rectangle, style, color, and size.

## 9. Panels and shapes

Rounded rectangles, borders, selection highlights, prompt panels, dialogue panels, modal shade, and modal body lower to the existing analytic SDF pipeline. Alpha remains on the primitive; no faded bitmap is baked.

## 10. Icons

Canonical TinyFarm uses no Machina vector-icon control today. Existing vector-MSDF support remains qualified and unchanged. World interaction symbols remain intentional native/world content rather than UI raster fallback.

## 11. Raster images

Mara's portrait remains a legitimate persistent raster texture in its existing compositor layer. UI state changes neither recreate nor upload it.

## 12. Clipping decision

The realization consumes Machina's balanced rectangular clip operations, intersects nested rectangles, and clips analytic destinations or MSDF destination/UV pairs. TinyFarm needs no stencil hierarchy.

## 13. Cache and invalidation law

The M10 typed semantic keys remain authoritative. Unchanged base, clock, and prompt frames retain their exact immutable `MachinaPresentationFrame` references. The native presenter only rebuilds a segment when that reference changes; unchanged-frame proof asserts that neither native geometry count nor glyph-run cache count changes. Text cache keys contain no live mutable UI object.

## 14. Buffer reuse

The existing `VulkanOrderedQuadRenderer` persistent resizable vertex and binding buffers are reused. M10B's adapter append route avoids per-text result arrays and appends directly to the segment's retained submission arrays.

## 15. Descriptor and texture stability

The proof recorded zero warm descriptor writes, zero dynamic UI texture uploads, zero warm atlas uploads, and zero unchanged-image uploads. UI changes update native submission buffers, not descriptor topology.

## 16–21. Changed-frame results

| Scenario | Before M10B | After M10B | Improvement | Allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| first combat | 27.258 ms | 8.155 ms | 70.1% | 570,312 |
| scene change | 25.408 ms | 5.087 ms | 80.0% | 519,232 |
| dialogue open | not recorded | 14.076 ms | — | 960,520 |
| objective and hotbar update | not recorded | 8.673 ms | — | 667,096 |
| inventory open | not recorded | 7.369 ms | — | 605,704 |
| pause open | not recorded | 8.430 ms | — | 757,544 |
| completion | not recorded | 7.902 ms | — | 1,075,464 |
| stable frame | ~2.2 ms changed-repeat path | 6.946 ms full presented average | different scope | 42,996/frame |

The first title present was 33.608 ms. Its Machina geometry, glyph runs, atlases, shaders, and offscreen native draw were already warm; the remaining one-time cost is first swapchain presentation and is not a changed-state gameplay hitch.

## 22. Allocations

Steady allocation measured 42,996 B/frame versus M10's 41,180 B/frame. This misses the preferred 16 KiB headroom target but remains bounded and produced no frame-budget miss. Changed frames allocate presentation/layout and native submission objects, but allocate zero full bitmap buffers and zero fallback raster bytes. Completion's 1.08 MB first-use semantic/text geometry is the largest remaining allocation event; it completes in 7.902 ms and is cached afterward.

## 23. Sixty-second trace

The Release trace measured 3,600 frames: 6.946 ms average, 6.944 ms median, 6.950 ms p95, 7.017 ms p99, 7.519 ms maximum, 143.97 FPS, and zero frames above 16.67 ms. It recorded zero readbacks, zero descriptor writes, zero texture uploads, four buffer uploads, 90 draw calls, and five submissions per representative frame.

## 24. Busy trace

The canonical walkthrough kept world drawing, NPCs, dialogue, audio, 232-particle capacity, and the compiled SoftShockwave effect active. The ten-minute semantic/native stress partition retained at most 232 particles, one emitter, and one audio voice without exceeding capacity or losing frame correctness.

## 25. Loop stress

The proof repeatedly renders unchanged UI and asserts no geometry or text-cache rebuild. It traverses Farm, Town, Riverside, Hearth House, General Store, and Old Burrow, opens two Mara conversation sequences, and exercises inventory/pause open-close. The existing ten-minute stress partition continues to check bounded retained memory. A specialized high-count scene/dialogue/inventory loop is not necessary to explain or gate the removed raster-upload path because fallback and upload telemetry are exact zero.

## 26. Visual parity

Six required 1280x720 screenshots were captured from the real Vulkan target. Content, geometry, overlays, portrait ordering, selection highlighting, and effects remain present. Space Mono MSDF deliberately replaces the prior 5x7 CPU bitmap realization while retaining TinyFarm's crisp pixel/vector character; no UI feature was removed.

A direct pixel audit against the committed M10 CPU-raster screenshots found identical interior panel/world colors at representative title and dialogue coordinates, with at most one-channel rounding at an analytic edge. Five representative full-frame comparisons had RGB mean absolute error between 3.26 and 4.25 byte levels; differences concentrate in the deliberately changed glyph silhouettes/antialiasing. The visually dim base HUD beneath title/dialogue is caused by the authored translucent modal/dialogue shade and is present in both realizations. No palette compensation was applied because it would make the native colors incorrect.

## 27. Semantic parity

The final authoritative hash equals both semantic replay and the independently restored completion run. Renderer caches are process-local and absent from persistence. Gameplay rules and intent routing are unchanged.

## 28. Tests

The native executable gates canonical changed frames below 16.67 ms; no fallback bytes/uploads; no dynamic UI texture upload; zero warm descriptor writes; stable native geometry and text caches; explicit screenshot readback; shader visibility; save/load; and replay parity. `AurelianGlyphRunAdapterM2Tests` now proves append realization preserves existing destination storage and produces the same qualified geometry.

## 29. Validation totals

- `Aurelian.slnx -c Release -m:1 --no-restore`: 746 passed
- `TinyFarm.slnx -c Release -m:1 --no-restore`: 335 passed
- `JointTaskForce.slnx -c Release -m:1 --no-restore`: 3,479 passed
- `Machina.UI.slnx -c Release -m:1 --no-restore`: 739 passed
- `Aurelian.Machina.Tests`: 43 passed
- Native Release Vulkan proof: passed with Vulkan validation enabled
- `git diff --check`: passed after artifact reconciliation

## 30. Remaining performance issues

Steady managed allocation remains approximately 43 KiB/frame, mostly outside changed UI, and the native sequential compositor now uses five representative submissions because analytic and MSDF painter stages are distinct. Neither causes a frame-budget miss. The one-time first swapchain present is still above 16.67 ms. These are measured headroom items, not the removed changed-state hitch.

## 31. Exact next milestone

**`AURELIAN-TINYFARM-PUBLIC-DEMO-M11`**. Package and polish the qualified game without reopening renderer architecture unless a new measured public-demo trace identifies a user-visible issue.

## Evidence

Machine-readable evidence and the six required screenshots are in `artifacts/aurelian-machina-native-ui-m10b/`.
