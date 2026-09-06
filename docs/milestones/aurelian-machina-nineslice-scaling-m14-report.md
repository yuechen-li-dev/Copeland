# AURELIAN-MACHINA-NINESLICE-SCALING-M14 report

## 1. Outcome A

Outcome A — scalable native 2D UI substrate qualified. SUNKILL now has a resizable native host, one 1280×720 logical coordinate system, uniform fit scaling, inverse pointer mapping, SpriteForge-authored nine-slice cards, native Vulkan realization, and deterministic small/odd/1440p proof artifacts.

Per the final product direction, SUNKILL buttons retain their existing analytic Machina styles. Nine-slice is applied only to the main-menu and dialogue cards.

Validation passed 773 Aurelian solution tests, 746 Machina solution tests, 21 focused SUNKILL tests, 128 Dominatus TOML-loader target-framework executions, and 24 SpriteForge target-framework executions. The default native launch smoke opened, rendered, and exited cleanly.

## 2. Baseline scaling/window audit

| Concern | Current behavior before M14 | Limitation | M14 action |
| --- | --- | --- | --- |
| Window | Avalonia window fixed at 1280×720 with `CanResize = false` | No drag, maximize, or restore scaling path | Enabled resize, minimum 640×360, and physical-pixel resize handling |
| Logical layout | Machina authored at 1280×720 | Assumed framebuffer matched logical pixels | Preserved 1280×720 as the sole reference space |
| Native targets | Compositor and layer renderers constructed at 1280×720 | Resize would rebuild presentation resources | Added same-format target retargeting and framebuffer-only recreation |
| Image presentation | Full-target quads | Would distort at a non-16:9 size | All logical presentation maps through one centered fit viewport |
| Background | Cover-cropped into the logical 1280×720 scene | Policy was implicit | Retained and documented logical-scene cover, then uniform physical scaling |
| Portrait | Logical 1280×720 transparent surface | Coupled to fixed framebuffer | Placed inside the same physical viewport |
| Input | Pointer coordinates treated as logical coordinates | Hit tests drift after physical scaling | DPI-aware physical conversion followed by inverse viewport transform |
| UI panels | Analytic flat cards and buttons | No scalable textured border primitive | Added renderer-neutral nine-slice and applied it to cards only |
| Texture sampling | Linear sampler, clamp addressing, full-image UVs | Atlas repetition could bleed across neighbors | Repeated bounded quads with half-texel-inset UVs |
| SpriteForge | Atlas grids, frames, sprites, and animation metadata | No UI panel slice contract | Added `[ui_panels.<id>]`, validation, and preview evidence |

The old path used one fixed reference resolution and effectively assumed matching physical pixels. It did not rebuild responsive layout, letterbox, or invert pointer coordinates.

## 3. Final reference-resolution law

SUNKILL remains authored at 1280×720. For framebuffer `(W, H)`, scale is `min(W / 1280, H / 720)`. The 1280×720 viewport is centered; unused pixels become letterbox or pillarbox area. X and Y never scale independently.

## 4. Logical-to-physical viewport transform

`MachinaViewportTransform` returns the uniform scale, centered physical viewport, logical-to-physical points/rectangles, physical-to-logical points, and physical containment. At 2560×1440 the scale is 2.0. At 1537×864 the scale is 1.2 and the viewport is `(0.5, 0, 1536, 864)`.

## 5. Input inverse transform

Avalonia pointer positions are converted from DIPs to physical pixels with the current render scale, then passed through `ToLogical`. Machina continues to hit-test in stable logical coordinates. The odd-resolution proof routes a physical pointer to the drawn Settings button and asserts that the application enters `RenScreen.Settings` with zero round-trip coordinate error.

## 6. Resize/swapchain behavior

`VnPresenter` listens to `SizeChanged`, ignores transient zero-sized framebuffer events, resizes the native compositor, replaces its writeable bitmap, and renders at the new physical extent. `NativeLayerCompositor.Resize` creates a replacement same-format target and rolls presenters back if any retarget fails.

`VulkanOrderedQuadRenderer.Retarget` recreates only framebuffers tied to the target extent. Pipelines, sampler, descriptor state, buffers, and uploaded textures remain live. The shrink/grow/restore sequence retained five stable uploads before and after: zero resize-time texture reuploads. A second native sequence renders Settings at 960×540, dialogue at 2560×1440, a choice at 960×540, and Save at 1537×864. Minimize is represented by ignoring zero extent until a positive restore extent arrives.

## 7. Nine-slice semantic model

`MachinaNineSlicePrimitive` owns stable texture identity, source rect, logical destination rect, source margins, edge mode, center mode, tint, and border scale. It contains no renderer types. Source margins cut the atlas in pixels; border scale maps the complete source borders to logical destination thickness.

## 8. Native lowering

`MachinaNineSliceLowerer` produces corner, edge, and center quads. `AurelianNineSliceAdapter` converts those rectangles to ordered native quad submissions, applies the shared viewport transform, maps tint, and normalizes UVs. Nine-slice does not introduce a shader, geometry stage, or app-specific Vulkan path.

## 9. Tile/stretch modes

Corners are never tiled. `Stretch` edges resize only along their long axis. `Tile` edges repeat only along that axis. A `Tile` center repeats along both axes; a `Stretch` center fills one center quad. The synthetic seam fixture exercises tiled edges and center. The final SUNKILL card uses stretched edges and center, as directed after visual review.

## 10. Gapless tiling policy

Atlas repetition uses adjacent repeated quads, exact destination accumulation, cropped final source rectangles, clamp addressing, linear filtering, and UV endpoints inset to boundary texel centers. Hardware repeat across an atlas is forbidden because it can sample neighboring content.

## 11. Partial tile behavior

The lowerer emits all complete repeats and crops the final tile proportionally in both source and destination space. It does not stretch, overlap, or leave a gap. CPU proof covers simultaneous horizontal and vertical partial tiles and records zero destination gaps.

## 12. Sampler/UV policy

Nine-slice reuses `Native2DPipelineOptions.SpriteLinear`: linear filtering and clamp addressing. Each emitted quad stays inside the authored atlas subrect. `AurelianNineSliceAdapter.ToInsetUv` rejects out-of-atlas rectangles and samples from the first and last texel centers.

## 13. Atlas bleed protection

Half-texel inset is sufficient for this atlas and sampler path. SpriteForge carries bounded `extrusion` metadata and validates 0–2 pixels for atlases that require duplicated edge texels, but M14 does not add a general image-processing pipeline. The SUNKILL atlas uses zero extrusion.

## 14. SpriteForge UI tileset schema

`[ui_panels.<id>]` contains `x`, `y`, `width`, `height`, four margins, `edge_mode`, `center_mode`, optional `border_scale`, and optional `extrusion`. The immutable `SpriteForgeNineSlicePanel` is the runtime metadata. SpriteForge and the sample now consume the same Tomlyn 2.9 package rather than using an API-compatibility shim.

## 15. SpriteForge validation

Validation rejects malformed IDs, non-positive source dimensions, out-of-bounds source rectangles, negative margins, margins exceeding source dimensions, empty tiled regions, unknown modes, non-finite/non-positive border scale, and extrusion outside 0–2. Diagnostics retain stable `spriteforge.*` codes and TOML source locations.

## 16. SpriteForge preview/audit

`spriteforge-slice-preview.png` shows the atlas, each panel source rectangle, and its inner slice guide. `nine-slice-wide.png` and `nine-slice-tall.png` are extreme-aspect native outputs. `tiled-seam-fixture.png` is the high-contrast native diagnostic.

## 17. SUNKILL UI atlas

`sunkill-ui-atlas.png` is original generated artwork: dark wartime metal and bakelite with orange accents, isolated on one texture sheet without text or logos. `sunkill-ui.toml` assigns stable `dialogue` and `button` panel IDs. Button metadata remains available, but SUNKILL does not apply it.

## 18. SUNKILL nine-slice integration

`VnUiSkin` loads and validates the real SpriteForge TOML and image, then maps panel metadata to Machina primitives. `VnMachinaLayer` emits one nine-slice for the main-menu card or dialogue card. The dialogue panel uses `edge_mode = "stretch"` and `border_scale = 0.5`, preserving the full 76-pixel source corners while presenting 38-logical-pixel borders. Buttons remain analytic Machina rounded rectangles/pills.

## 19. 720p proof

`sunkill-720p.png` is a real native 1280×720 Vulkan readback. It exercises the reference scale of 1.0 with the final card skin and existing analytic controls.

## 20. 1440p proof

`sunkill-1440p.png` is a real 2560×1440 Vulkan readback at uniform scale 2.0. Layout, card borders, text, portrait, and controls remain aligned.

## 21. Odd-resolution proof

`sunkill-odd-resolution.png` is a real 1537×864 readback at scale 1.2 with a half-pixel horizontal viewport origin. The screenshot and pointer activation proof cover non-integral placement, UV precision, and inverse input mapping.

## 22. Small-window proof

`sunkill-small.png` is a 960×540 readback at scale 0.75. The same logical layout remains usable, slice regions stay positive, and controls remain reachable. The native window enforces a practical 640×360 minimum rather than promising arbitrary tiny-window layout.

## 23. Seam fixture results

The native R8G8B8A8_UNORM readback repeats a high-contrast 16×16 dark/orange fixture horizontally and vertically. It samples 393 RGB boundary comparisons. Maximum channel error is 0 and mean channel error is 0.

## 24. Color parity

Nine-slice reuses the M11-qualified `ForwardTextured` R8G8B8A8_UNORM path. An orange corner sample expects RGBA `(255, 112, 12, 255)` and records maximum channel error 0. No second transfer function or nine-slice shader exists.

## 25. MSDF scaling

M14 does not change Machina typography or analytic-SDF semantics. SUNKILL's current M13 UI overlay uses the established readable bitmap text bridge rather than claiming an MSDF path it does not use. It is composited inside the same logical viewport at 0.75×, 1.0×, 1.2×, and 2.0×; visual inspection found stable alignment. Existing Machina typography/outline tests remain part of the full solution validation.

## 26. Input hit-test parity

Transform theory covers 16:9, pillarbox, and letterbox cases and exact pointer round trips. The real odd-resolution action test activates Settings through physical-to-logical routing. Existing SUNKILL tests cover button, dialogue, save/load, settings, keyboard, capture, and focus behavior in logical space.

## 27. Performance sanity

The representative odd-resolution UI pass records 10 quads, 2 draw calls, 0 descriptor writes, 1,032 CPU allocated bytes, and 0.170 ms command recording on the proof machine. This is the existing ordered-quad batch path; no special instancing system was added.

## 28. Fresh skin-extension proof

The read-only fresh-context audit found that a second atlas region can flow through `VnUiSkin.Create` and `VnMachinaLayer` without changes to `VnNativeRenderer` or Vulkan internals. It also found one honest application seam: SUNKILL has no warning-dialog semantic state or node yet, so a real warning style should be added only alongside that bounded app concept. Raw style IDs are the main discoverability weakness; a typed warning wrapper would be sufficient without a theme framework.

## 29. Fresh tileset-edit proof

The read-only fresh-context audit located `edge_mode` in `sunkill-ui.toml`, the SpriteForge and Machina tests, and the `--m14-proof` command. Switching edge mode and regenerating proof does not require Vulkan changes.

## 30. Owner-lane fixes

- Machina owns viewport and nine-slice semantics/lowering.
- SpriteForge owns atlas rectangles, slice metadata, modes, border scale, extrusion metadata, and validation.
- Aurelian owns native target retargeting, sampling, UV conversion, and ordered-quad realization.
- SUNKILL owns the `dialogue` panel choice and the decision to retain analytic buttons.
- The Tomlyn dependency was aligned at 2.9 in Dominatus instead of adding serialized compatibility artifacts or reflection.

## 31. Deferred systems

No theme engine, selector/cascade system, hot reload, theme packs, arbitrary atlas packer, image-processing framework, tilemap, animation integration, WebGPU backend, browser scaling, DPI asset variants, or general responsive-layout framework was added. MachinaCanvas was not required by the runtime or authoring change.

## 32. Exact M15 recommendation

Recommend one narrow M15: SUNKILL VN presentation transitions. Add only deterministic fade/crossfade facts and native realization for scene and portrait changes, with skip/replay-safe timing. Do not combine it with richer SpriteForge authoring, backlog/history/auto, WebGPU, or responsive-layout work.

## 33. Diff stat

Final unstaged worktree diff, including new proof artifacts: Copeland has 32 files, 2,123 added lines, and 40 deleted lines; Dominatus has 7 files, 400 added lines, and 26 deleted lines. Combined: 39 files, 2,523 additions, and 66 deletions. Binary PNGs count as files but not text lines.

## Qualification commands

```powershell
dotnet test tests/Integrations/Sunkill.Tests/Sunkill.Tests.csproj -m:1 -nodeReuse:false -p:UseSharedCompilation=false
dotnet test Aurelian.slnx -m:1 -nodeReuse:false -p:UseSharedCompilation=false
pwsh ./tools/Test-SunkillLaunch.ps1
dotnet run --project samples/Integrations/Aurelian.Ariadne.VnDemo/Aurelian.Ariadne.VnDemo.csproj -- --m14-proof

cd ../Dominatus
dotnet test tests/Dominatus.Assets.Toml.Tests/Dominatus.Assets.Toml.Tests.csproj -m:1 -nodeReuse:false -p:UseSharedCompilation=false
dotnet test tests/Dominatus.SpriteForge.Tests/Dominatus.SpriteForge.Tests.csproj -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```
