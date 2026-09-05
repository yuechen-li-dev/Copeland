# AURELIAN-NATIVE-GAME-WORLD-2D-M1

## Decision

**Outcome B — the reusable native world kit works; one bounded native composition seam remains.**

The new `Aurelian.GameWorld2D` integration owns typed coordinate conversion, a presentation-only camera, SpriteForge playback/frame lowering, stable painter order, typed sprite identities, and scene-scoped Vulkan texture realization. The native proof renders a tile floor, wall, two overlapping actors, an animated object, a transparent sprite, and a foreground occluder. It qualifies deterministic camera/frame/order hashes, straight alpha, viewport reprojection, disposal, content-hash replacement, and 100 warm frames.

Outcome A is not claimed because `VulkanCompositorPassthrough` rejects more than one plant output. The textured world and the independently qualified native Machina MSDF/analytic overlay cannot yet be combined into one native target through the real compositor. A proof-local CPU merge or rasterized HUD texture would hide that missing engine seam, so neither was added.

## Mandatory audit

| Concern | Existing owner/type | Reuse | M1 seam |
| --- | --- | --- | --- |
| Native quads | `VulkanOrderedQuadRenderer`, `NativeQuadSubmission` | yes | ordered sprite submissions target the existing renderer |
| Shader/ABI | `CompiledGraphicsProgram`, `ForwardTexturedM3.v.ts` | yes | no parallel shader or descriptor contract |
| Camera snapshot | `RenderCamera2D` | partial | kept intact; `Camera2D` adds the missing game-facing follow/snap/clamp controller |
| Renderer-neutral snapshots | TinyFarm `TinyFarmFrame`; Aurelian resolved snapshots | partial | `WorldPresentationSnapshot` is an observational sprite list, not game state |
| TinyFarm units | `ScenePosition`, `SceneUnitsPerTile` | yes as source law | explicit `TilePoint2`/`WorldPoint2` and `World2DUnitScale` prevent numeric mixing |
| Sprite metadata | `SpriteForgeAtlas`, `SpriteForgeResolver`, pivots/animations | yes | bridge selects playback frame and lowers rect/pivot/UV |
| Texture lifetime | renderer opaque texture handles | yes | `NativeSpriteResourceScope` owns typed ID/content-hash reuse and disposal |
| Machina overlay | `TinyFarmMachinaUiLayer`, native MSDF/analytic adapters | independently qualified | blocked only at multi-input Vulkan target composition |

No ECS, scene graph, gameplay extraction, collision, audio, particles, input host, asset database, or second renderer was added.

## Coordinate, camera, and projection law

- Coordinate types are `TilePoint2`/`TileRect`, `WorldPoint2`/`WorldRect`, `PixelPoint2`/`PixelRect`, and `UvRect`.
- Tile to world is `world = tile * TileSizeWorld`. The proof uses 256 fixed world units per tile.
- World to pixel is `viewportOrigin + (world - cameraTopLeft) * PixelsPerWorldUnit * Zoom`. The proof uses 1/8 pixel per world unit at zoom 1.
- Atlas pixels to UV are normalized against atlas width/height with top-to-bottom row orientation.
- Pixel-art snapping is explicit per sprite and rounds the final destination away from zero. `SpriteNearest` and `SpriteLinear` are explicit, separate policies; filtering is not global.
- `Camera2D` exposes position, viewport, zoom, bounds, `Follow`, `SnapTo`, `Clamp`, `Resize`, and immutable `Snapshot`. Follow centers the target then clamps the top-left position. Snap is immediate and also clamps. Camera state remains presentation-only.

## Sprite, animation, ordering, and resources

`WorldSprite` contains stable presentation ID, world anchor, typed asset ID, SpriteForge sprite/clip IDs, elapsed presentation time, explicit restart, visual scale/tint, coarse layer, independent `FeetY`, and pixel-snap policy. It contains no Vulkan handles.

`SpritePlaybackState` delegates frame resolution to `SpriteForgeResolver`. A same clip retains its origin, a new clip restarts, explicit restart resets the origin, loops wrap, and once clips clamp at their final frame. Playback reads elapsed presentation time and never decides movement, hits, damage, or other gameplay facts. SpriteForge visual pivot and offset determine quad placement; `FeetY` remains a distinct painter anchor.

`WorldSpriteProjectionAdapter` orders by `(Layer, FeetY, StableId ordinal)` before lowering to native quads. The renderer remains unaware of player/NPC/tree semantics. The proof changes player/NPC Y order and records a distinct ordering hash; the foreground occluder emerges from the coarse layer rather than a player-specific rule.

`NativeSpriteResourceScope` maps typed asset ID plus content hash to one opaque renderer texture. Unchanged content reuses the handle, changed content disposes the prior realization and uploads once, and scope disposal rejects future access. The proof uses one atlas for all sprites, reports zero warm uploads, and proves the replaced handle is rejected.

## Native rendering result

The sprite pipeline uses ordinary straight alpha:

```text
color: source-alpha / one-minus-source-alpha
alpha: one / one-minus-source-alpha
```

The transparent-corner proof exposed and repaired an old native winding defect: framebuffer-space winding had selected `ForwardTextured`'s back-face diagnostic branch, which returned tint without sampling the texture. Reversing the quad vertices makes the intended front-face texture sample authoritative. The opaque M0 proof and the full graphics suite still pass with refreshed deterministic hashes.

On the qualification machine (NVIDIA GeForce RTX 3070), Khronos validation was enabled with zero reported errors. Frame 0 and frame 1 hashes differ, 100 repeated frame-0 renders are stable, and the final warm frame performs zero descriptor writes. The canonical scene has 89 submissions and six draw calls because contiguous equal texture/tint bindings are coalesced without reordering.

## MachinaCanvas.JS / TinyTown tooling audit

Classification: **B — useful, but needs a bounded stabilization pass.**

The JavaScript workspace contains real sprite tooling rather than a dead demo: SpriteForge sidecar parsing/editing, subgrids, cut validation against alpha gutters, focus/preview UX, atlas dimension diagnostics, TinyTown artifact scripts, alpha-map attachment, and `deriveAlphaMapPixels`. Thirty-one focused Vitest tests pass after a clean dependency install.

The reusable parts are the sidecar/frame-cut workflow, alpha-aware cut audit, preview/inspection UX, and deterministic edge-connected silhouette extraction. The alpha generator samples one averaged edge color, however, so a two-color baked checkerboard is not yet a robustly qualified cleanup case. Runtime must consume cleaned straight-alpha assets; it must not remove checkerboards in shaders.

Recommendation: run `MACHINA-CANVAS-JS-SPRITE-TOOLING-STABILIZATION-M0` after closing native composition. Bound it to a two-color checkerboard recovery algorithm, TinyTown source-to-transparent fixture, exportable cleaned PNG, and deterministic tests. Do not refactor MachinaCanvas as a whole.

## SpriteForge owner-lane qualification

The CRLF fixture bug was repaired by normalizing CRLF and bare CR to LF before converting once to the platform newline. The hermetic fixture no longer performs a second conversion. The nested projects also explicitly opt out of Copeland central package management, preserving their Tomlyn 0.19 owner dependency when consumed from this checkout.

Result: 8/8 tests pass on net8.0 and 8/8 pass on net10.0. Loader, validation, grid/absolute frame resolution, pivot, and mixed-frame behavior are qualified. No SpriteForge schema or runtime semantics were forked.

## Integrated results and authority audit

| Required result | Result |
| --- | --- |
| Tile/world, wall, object | native canonical scene present |
| Player/NPC overlap | `(Layer, FeetY, StableId)` changes front-most actor deterministically |
| Animated object | SpriteForge 2-frame loop and once clip qualified; frame hashes differ |
| Transparent sprite | transparent corner reveals contrasting prior world layers; no opaque box |
| Camera motion/resize | follow, clamp, snap, zoom and viewport reprojection qualified; atlas retained |
| Machina overlay/MSDF | existing real paths remain qualified, but combined native target is blocked |
| Disposal/version | scope disposal and stale/replaced handle rejection qualified |
| Warm resources | 100 frames; 0 texture uploads and 0 descriptor writes after warmup |
| Vulkan | RTX 3070; validation layer enabled; 0 errors reported |
| Gameplay mutation | none; all inputs are immutable presentation data |
| Animation authority | presentation only; no gameplay outputs exist in the contract |

## Validation and artifacts

- `Aurelian.GameWorld2D.Tests`: 11/11 passed.
- `Aurelian.slnx -c Release`: 661/661 passed.
- `TinyFarm.slnx -c Release`: 273/273 passed (existing upstream typography warnings only).
- `Aurelian.Graphics.Tests -c Release`: 255/255 passed.
- SpriteForge: 16/16 target-framework runs passed.
- MachinaCanvas focused sprite/tooling lane: 31/31 passed. `npm ci` reported nine dependency audit findings; dependency upgrades were outside this milestone and no workspace files changed.
- Native M0 quad proof rerun: validation enabled, zero errors, stable 100-pass hash.
- `git diff --check`: passed.

Compact artifacts are under `artifacts/aurelian-native-game-world-2d-m1/`: `proof.json`, `camera.json`, `sprites.json`, `resources.json`, `rendering.json`, and `manifest.json`.

## Exact next milestone

Run **`AURELIAN-NATIVE-LAYER-COMPOSITOR-M0`** next: accept two or more ordered native plant outputs, source-over composite textured/MSDF/analytic layers into one target, preserve explicit layer order, resize and lifetime, and qualify the real TinyFarm Machina hotbar/status overlay above this M1 world. Then rerun M1 to Outcome A, run the bounded MachinaCanvas sprite-tooling stabilization, and only then proceed to `AURELIAN-GAME-HOST-INPUT-M2`.
