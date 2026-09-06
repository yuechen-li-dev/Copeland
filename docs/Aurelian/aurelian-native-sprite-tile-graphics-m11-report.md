# AURELIAN-NATIVE-SPRITE-TILE-GRAPHICS-M11

Outcome: A — native color correctness, authored sprite/tile rendering, and the TinyFarm product integration are qualified.

## M11A — native color correctness

The visible native path was faded because TinyFarm matched its intermediate native frame target to the swapchain's sRGB format, while authored sRGB color numbers were uploaded as though they were linear shader values. Vulkan then performed the required sRGB attachment encoding on values that were already sRGB encoded. Middle gray `128` became `188`, for example.

The fix makes the policy explicit:

- Native 2D tints and analytic colors are authored in sRGB and decoded to linear before uniform upload when the target is sRGB.
- Authored RGBA sprite textures use `R8G8B8A8_SRGB` when sampled into an sRGB target, so Vulkan performs the input decode.
- MSDF atlases remain UNORM because their channels are distance-field data.
- Straight alpha remains unpremultiplied. RGB uses `srcAlpha / oneMinusSrcAlpha`; alpha uses `one / oneMinusSrcAlpha`. Blending occurs in linear light on an sRGB attachment.
- PNG evidence now includes explicit `sRGB` and `gAMA` chunks. PNG bytes are the native RGBA readback bytes; no screenshot color transform is used for the oracle.

The deterministic 4x4 palette covers white, black, middle gray, RGB primaries, CMY secondaries, three muted UI colors, and four alpha-over-background cases. On the NVIDIA GeForce RTX 3070 with Khronos validation enabled:

| Path | Average channel error | Maximum channel error |
| --- | ---: | ---: |
| Before: authored sRGB treated as linear | 15.34375 bytes | 71 bytes |
| Corrected analytic primitives | 0 bytes | 0 bytes |
| Corrected textured quads | 0 bytes | 0 bytes |
| Analytic versus textured parity | 0 bytes | 0 bytes |

The acceptance tolerance is one byte per channel to allow implementation-defined final quantization. This device produced exact byte equality, including the alpha cases computed as sRGB decode, linear source-over blend, and sRGB encode.

Evidence: `artifacts/aurelian-native-color-correctness-m11a/`.

## M11B — sprite sheet and tile graphics

The engine reuses the existing bounded presentation seams rather than adding a second asset or scene system:

- SpriteForge remains authority for atlas grids, static frames, pivots, animation clips, FPS, and loop policy.
- `Aurelian.GameWorld2D` remains authority for world-to-pixel camera projection and stable painter order `(layer, feetY, stableId)`.
- `NativeSpriteResourceScope` owns one resident atlas upload and stable handle reuse.
- `VulkanOrderedQuadRenderer` remains the native realization path with nearest/clamp sampling and straight-alpha blending.

The project asset is a 4x4 authored pixel-art sheet. A deterministic centered crop converts the generated 1254x1254 source into a 1248x1248 runtime atlas with sixteen explicit 312x312 cells. Frame metadata identifies four terrain tiles, wall/fence/tree/market props, four `walk-down` farmer frames at 6 FPS, and mint/well/hearth/lantern props. Every frame records a center or bottom-center pivot.

The source PNG had a usable direct alpha channel, so the older MachinaCanvas fake-checkerboard alpha derivation was audited but not reused. That algorithm is valuable for images whose transparency was baked into a checkerboard; applying it here would manufacture a second alpha estimate. The bounded stabilization instead thresholds the existing alpha at 128 and clears RGB under transparent pixels. Measured crop counts changed from 548,581 transparent, 1,008,349 partial, and 574 opaque pixels to 735,182 transparent and 822,322 opaque pixels. This removes unintended global translucency and sampling halos while preserving straight-alpha runtime policy.

TinyFarm owns a compact `A/B/C/D` authored terrain description and the mapping from semantic scene objects to sprite identities. The engine owns only slicing, playback, projection, resource lifetime, sampling, alpha, camera math, and ordering.

## TinyFarm proof

The real `TinyFarm.Native` Vulkan compositor now renders terrain tiles, walls and props, crops/items, a four-frame farmer, NPC overlap, camera projection, native Machina UI, particles, and shader effects on the same target. The captured representative scene contains 166 world sprites, uses one resident atlas upload, performs zero stable-frame sprite uploads, and uses no CPU raster fallback for world art.

The complete native supper walkthrough passed after integration, including scene transitions, dialogue, farming/pickup, combat, save/load, deterministic replay, completion, native UI, and the existing performance gates. Gameplay state and hashes remain resolver-owned; animation and camera are projections only.

Evidence: `artifacts/aurelian-native-sprite-tile-graphics-m11b/`.

## Validation

- M11A GPU palette proof: passed; exact corrected analytic and texture samples; validation enabled.
- `TinyFarm.Native --proof`: passed through the real hidden Vulkan window/swapchain path and emitted product screenshots.
- `Aurelian.slnx`: 752 tests passed.
- `TinyFarm.slnx`: 335 tests passed (308 TinyFarm plus 27 shared Spatial2D).
- `JointTaskForce.slnx`: all projects passed.
- Focused `Aurelian.Graphics.Tests`: 266 passed.
- Focused `Aurelian.GameWorld2D.Tests`: 11 passed.
- `git diff --check`: passed; repository line-ending notices only.

## Deferred

- Direction-specific character art beyond the qualified four-frame down-facing walk cycle.
- Atlas packing, mip generation, compressed textures, bindless descriptors, and streaming.
- A generalized tile editor or runtime scene graph.
- Generalizing the direct-alpha cleanup into MachinaCanvas. The existing checkerboard tool and this direct-alpha case solve different inputs; combining them deserves its own evidence if a second production consumer appears.
