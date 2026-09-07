# TINYFARM-HIGH-FIDELITY-WORLD-PRESENTATION-M25

## 1. Outcome

**Outcome B — presentation isolation works; the Riverside shoreline remains an artistic fidelity seam.** TinyFarm now opens at 1920×1080, supports exact 1440p and resize, and allows immediate HUD removal. Painterly resources use linear sampling at substantially larger display footprints. The farmhouse uses the user-approved north-up, three-quarter art convention. Native captures prove unchanged world pixels outside HUD chrome, camera/state parity, independent inspection, and clean capture restoration.

The remaining seam is concrete: water still appears as a softly feathered rectangular cyan field. The alpha transport is corrected, but the bank lacks coherent painterly material structure. This is not a renderer architecture blocker and does not justify Outcome C. It prevents claiming Outcome A's visually coherent shoreline.

## 2–3. M24 baseline and limitations

M24 supplied semantic spatial authority, independent navigation/collision/occlusion/interaction, approved generated art, painterly slab composition, and a live Riverside field. Its native default was 1280×720 at 48px/m, with world viewport `(22,24,904,648)`, nearest sampling for painterly sprites, and no functional native resize. Full HUD panel rectangles covered approximately 41.49% of the frame. A tree's 1218×1292 source occupied only about 168×178 screen pixels and northern art clipped.

The [baseline audit](../research/tinyfarm-presentation-fidelity-baseline-m25.md) records all major resources, including source file size, semantic size, screen footprint, sampling, and utilization. Before captures reproduce legacy camera/resources/composition; the current control legend and smaller prompt remain in that reproduction. They are not falsely presented as an untouched M24 binary.

## 4. World/UI composition law

World background/slabs → paths/patches and bank feather → live water with semantic-derived alpha → bridge → feet-Y ordered objects/actors → foreground player silhouette → effects → contextual prompt → optional HUD → optional inspector. Existing modal dialogue/pause/inventory screens retain their normal behavior.

The native world projection owns its geometry. UI uniformly overlays a separate logical canvas. Hiding HUD never changes world scale, camera, player position, navigation, collision, interaction, field bytes, or semantic save/replay state. Sampler changes preserve the existing ordered stream through contiguous sampler runs, rather than regrouping overlapping sprites.

## 5–6. HUD toggle and world-only mode

F9 toggles normal status, objectives, inventory/tool chrome, and persistent control legend. F10 independently toggles the inspector, including HUD-off + inspector-on. InputMan now owns portable F1–F12 identities; the Silk adapter maps physical function keys. No second input system was introduced. The sibling InputMan change was explicitly authorized by the user.

`--world-only` starts ordinary gameplay directly with HUD hidden. F11 hides normal HUD and inspector for one native capture, writes `artifacts/tinyfarm-captures/tinyfarm-<UTC timestamp>.png`, and restores previous visibility in `finally`. It retains the small local interaction prompt. Capture does not pause simulation, reposition actors, or enter save state; normal host frame progression continues. Existing modal screens are not suppressed, so use F11 during gameplay for the clean world frame.

## 7–9. Resolution, aspect ratio, and DPI

Default native size is **1920×1080**, with no proof flag required. The launcher forwards dimension and world-only arguments. World scale uniformly fits the existing 16×10m ground plus 3m canopy headroom inside a 16×13 presentation region. Decorative meadow extends through remaining aspect space. It is not additional traversable world. UI independently fits 1280×720 logical units, centering unused aspect space without distortion.

Native compositor, render target, readback, and swapchain follow physical `FramebufferSize`. Resize retargets renderers and resources without sprite reuploads. Same-window 1600×1000, 2560×1440, and 1920×1080 passed with exact physical extents and unchanged state. Zero-size minimized frames are skipped.

The tested desktop reported 1:1 window/framebuffer pixels. High-DPI plumbing was audited; no actual 150%/200% desktop scaling run is claimed. An initial decorated 1440p window was OS-clamped to 1421 pixels high. Detecting this clamp and retrying without a window border now yields exact 2560×1440. Earlier 1421p files are diagnostic history, not acceptance evidence.

## 10–12. Asset audit and scale experiments

Semantic size, source image dimensions, and screen dimensions are separate authorities. `TinyFarmPainterlyPolicy` controls presentation multipliers and sampling. Neither source dimensions nor scale sweeps alter trunks, walls, navigation, or interaction. Existing semantic canopy/roof occlusion remains unchanged; rendered overlap and foreground player visibility use the actual scaled sprite rectangles.

At 1080p a typical tree grows from 167.8×178 baseline pixels to **319.5×338.9**, approximately 3.6 times as many display pixels. Its source-to-screen ratio improves from 7.26 to 3.81 per axis. The final house occupies **481.1×381.6**, versus 285.6×238 before. Source ratio improves from 4.81 to 2.93 per axis, with a different approved source.

Native tree and farmhouse sweeps each test 1.0, 1.5, 2.0, and 2.5 multipliers at fixed high-resolution camera framing. The comparison sheets show that larger factors consume northern headroom, clip roofs/canopies, cover routes, and overwhelm composition. Final multipliers are independently selected: tree **1.1**, house **1.05**, with house base height210px at48px/m. Resolution/framing already provides most of the footprint increase; multiplying every asset by2 would be counterproductive.

The user identified the original farmhouse's rotated perspective as wrong. A handwritten SVG guide now constrains a broad horizontal roof and shallow south facade, with nominal 45° elevation, north-up, zero horizontal yaw, and parallel edges. Painterly generation reused M24 material reference. The initial perpendicular roof-plan interpretation was rejected after clarification. Only the corrected house is selected; M24 originals remain intact. See [art projection contract](../research/tinyfarm-art-projection-contract-m25.md), `generated-house-provenance.json`, and `house-generation-prompts.json`. This convention governs authored art; it does not replace the existing 2D semantic camera with a 3D projection.

## 13. Character readability

Existing 70px-at48px/m character art now occupies approximately121×121 at1080p. A restrained translucent player silhouette is drawn when foreground painterly cover overlaps, preserving location readability without globally dimming the environment. The pixel-style character remains visibly less cohesive than the painterly assets and is recorded in the ledger.

## 14–16. Sampling and mipmaps

PainterlySlab and PainterlyObject resource metadata select linear sampling. Pixel atlas resources retain nearest filtering; Profile/MSDF retains derivative-aware reconstruction. Live semantic field samples linear UNORM. Per-resource policy routes native texture scope selection without changing overlap order.

The fresh sampler experiment changed only `TreeSampling` to nearest, rebuilt, and captured actual native realization, then restored linear. Its recorded resource hash is unchanged. That diagnostic capture predates the final house revision and is evidence for tree sampling only.

Mipmaps were not added reflexively. Tree minification remains about3.8 source texels per displayed pixel per axis at1080p. Static linear-filtered edges are improved; these short runs do not establish a temporal shimmer gate. Deterministic alpha-aware mips remain a measured follow-up candidate, not something declared unnecessary. No runtime downsample variants were introduced.

## 17–19. Alpha edges, bank mask, and Riverside composition

Final tree and house have real RGBA alpha; native dark, meadow, and bank-adjacent views show no opaque checkerboard, broad bright matte, or large white fringe. Fine foliage softens under linear minification. This is native visual inspection plus source alpha-range verification, not pixel-perfect matte certification.

`TinyFarmBankMask` copies the original field interior byte-for-byte into a one-texel dry border and extends only the visual field bounds by that texel. The blue-channel wet mask controls alpha. The M25 field shader outputs straight RGB with wet-derived alpha, removing the prior extra RGB-by-wet multiplication. Soil feather draws before water; bridge draws after. No hard post-water bank bars remain in the new path.

Interior field data, semantic wet/dry authority, saves, and resolver hashes are unchanged. A focused test verifies every interior byte and every dry border value. The resulting edge is softer, but remains too rectangular and materially uniform: **the exact Outcome B seam**.

## 20–22. Camera, occupancy, and contextual prompt

The existing camera now frames the ground with three metres of canopy headroom. Scale remains uniform and independent of HUD. Wider aspect space receives decorative meadow rather than a flat dead border. This improves legibility without showing more navigable empty space or changing camera semantics.

| Coverage estimate | Before | After1080p |
|---|---:|---:|
| Semantic ground | 40.00% | 53.25% |
| Ground plus composed tree/house alpha | 41.03% | 57.88% |
| Decorative backdrop | 40.00% | 100.00% |
| Flat dead border | 58.97% | 0.00% |
| Decorative extension beyond active world | 0.00% | 42.12% |
| Normal HUD panel rectangles when visible | 41.49% | 41.49% |
| Normal HUD when hidden | 0.00% | 0.00% |

These categories overlap; they do not sum to100%. The alpha union excludes transparent padding using an alpha>20 threshold. Decorative fill is explicitly not counted as extra gameplay. Full HUD is intentionally not redesigned and remains large when enabled.

The prompt is a small measured panel near the player, such as `E Gather mushrooms`, independent of HUD. Persistent controls and the full quest panel disappear with F9. Wordy prompts and flat target art remain documented defects.

## 23–25. Source utilization, texture memory, and resolution ladder

`source-detail-utilization.json` reports both source-area/display-area and per-axis ratios. Transparent padding and different art style limit comparisons; this is evidence, not a numerical quality score. Higher resolution retains more tree/house detail, while the 1254² meadow source is already being magnified across the full framebuffer.

Resident game sprite texels total **25,105,648 bytes (23.943MiB)**: three painterly textures and the shared pixel atlas. The padded live field is1536bytes. Four sprite uploads occur at attach, with **zero** during warmed measurement or resize. Rejected art and M24 alternative house are not resident. These are exact RGBA payload counts, not driver allocation telemetry; font textures, alignment, descriptors, swapchain images, and buffers are excluded. Single RGBA render-target payloads are3,686,400 /8,294,400 /14,745,600bytes at720p/1080p/1440p.

All three native world-only and HUD captures exist.720p is usable but discards more painterly detail;1080p is the new default;1440p retains more material detail with unchanged scene framing and uniform UI. No low-resolution intermediate is upscaled to produce the evidence.

## 26. Performance

RTX3070, Release build, native Vulkan, VSync disabled for evidence,30 warm-up frames followed by180 measured frames per mode. Timing is CPU wall-clock for the complete host frame with synchronous Vulkan submission; GPU timestamp timings are unavailable. See the six performance JSON files for p50/p95/p99/worst, draw count, buffer/field uploads, projection, and UI allocations. The summary table is generated in `performance-summary.md` alongside them.

At1080p HUD-off p50/p95/p99 were6.973/9.948/16.919ms;1440p6.943/6.982/9.616ms. Both have short-run noise/spikes;1080p worst19.801ms and HUD-on worst21.879ms mean this is not a guarantee of every frame under16.67ms. HUD-off reduces draw count from about388 to116 at1080p, but sequential run timing does not support claiming a reliable timing speedup. No warmed sprite upload regression occurred. Baseline M24 timing is not an equivalent measured run, so no percentage speedup/regression claim is made against it.

## 27. Oblivion inspection

A fourth existing live inspection surface exposes actual native presentation facts: framebuffer/viewport, world scale, HUD/inspector visibility, resource sampler policy, and source/display ratios. It reports absence when native facts are unavailable. `oblivion-presentation-inspector.png` shows the HUD-off inspector; per-resolution JSON snapshots contain the same facts. This is a bounded extension of existing inspection registration, not an editor framework.

## 28–31. Fresh-context tasks

| Task | Exact entry point | Evidence |
|---|---|---|
| Hide HUD and capture world | F11 during gameplay; or F9 then F11 | `world-only-proof.json`, `hud-toggle-proof.json` |
| Make painterly trees40% larger without trunk change | Multiply `TinyFarmPainterlyPolicy.TreeVisualScale` by1.4 (current1.1 →1.54) | `fresh-scale-proof.json`; semantic scale/collision unchanged |
| Launch2560×1440 | `Play-TinyFarm.cmd --width 2560 --height 1440 --world-only` | Exact1440p readback; `resolution-policy.json` |
| Change one painterly prop sampler | Set its `TinyFarmPainterlyPolicy` sampling constant; no renderer rewrite | `sampler-nearest-tree-native.json` |

HUD-on versus off at frozen render sequence42 produced **zero changed pixels outside HUD chrome**, identical camera, and identical semantic/field hashes. A90-input movement test with different HUD/inspector preferences produced byte-identical semantic saves and equal hashes. Preferences never enter gameplay serialization.

## 32–33. Defects and next step

The [visual defect ledger](../research/tinyfarm-visual-defect-ledger-m25.md) classifies A–G observations and references native evidence. Choose **one primary next pressure: painterly ground-material composition, beginning with the Riverside shoreline and its connection to the path and bridge**, preserving semantic wet/dry authority. Do not begin a UI redesign or regenerate the full asset catalog to avoid this specific seam.

## 34. Diff and validation

`diff-stat.txt` records tracked diff statistics plus new-file inventory separately for Copeland and the explicitly authorized InputMan change. No changes were committed. Validation: TinyFarm solution Release build passes;341 TinyFarm and27 Spatial2D tests pass; InputMan solution76 Core,19 MonoGame, and7 Stride tests pass. The real visible native default-window smoke verifies title, Enter, movement, pause, and a rendered1920×1080 frame. Native proof runs qualify the three resolution sizes and same-window resize. Remote CI was not run.

Reproduce from the Copeland root, in this order (1080p last writes shared summary/capture files):

```powershell
dotnet build TinyFarm.slnx -c Release -m:1
dotnet test TinyFarm.slnx -c Release -m:1 --no-build
dotnet test ../InputMan/InputMan.slnx -c Release -m:1
dotnet run --project src/TinyFarm/TinyFarm.Native -c Release --no-build -- --window-smoke
dotnet run --project src/TinyFarm/TinyFarm.Native -c Release --no-build -- --m25-proof --m25-baseline
dotnet run --project src/TinyFarm/TinyFarm.Native -c Release --no-build -- --m25-proof --width 1280 --height 720
dotnet run --project src/TinyFarm/TinyFarm.Native -c Release --no-build -- --m25-proof --width 2560 --height 1440
dotnet run --project src/TinyFarm/TinyFarm.Native -c Release --no-build -- --m25-proof --width 1920 --height 1080
python tools/tinyfarm-presentation-m25-evidence.py
```

The final evidence script needs Pillow. The selected art and historical diagnostic experiments are checked-in inputs to this evidence process, not regenerated by it. All required M25 files are indexed and hashed in `artifacts/tinyfarm-high-fidelity-presentation-m25/manifest.json`.
