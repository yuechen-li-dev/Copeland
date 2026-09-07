# TinyFarm spatial-authoring baseline (M24)

This audit covers the bounded 16 x 10 Riverside/Farmhouse region before the M24 semantic scene replaces its ground realization. The authoritative sources inspected were the TSON-loaded scene model, resolver spatial adapter, navigation service, field runtime, native presenter, Profile sprite assets, and Oblivion live surfaces.

## Classification

| Class | Existing mechanism | Baseline finding |
|---|---|---|
| A — semantic world authority | `SceneDefinition`, `SceneObjectDefinition`, `SceneLayoutRow`, `TinyFarmSpatialWorldAdapter`, resolver-owned `SpatialWorld2D` | Object identity, placement, rectangular blocker extents, exits, and interaction meaning are authored data and resolved outside rendering. |
| A — semantic world authority | `TinyFarmFieldRuntime` | Riverside water, wet mask, disturbance, charge, save/replay state, and gameplay queries are a live field, not background pixels. |
| B — presentation-only | `TinyFarmFrame`, `WorldPresentationSnapshot`, `TinyFarmNativeRenderer`, `NativeLayerCompositor`, `VulkanOrderedQuadRenderer` | These project simulation state and must not acquire game authority. |
| B — presentation-only | Profile/SpriteForge atlas metadata | Animation frames, pivots, scale, UVs, and sprites are presentation facts. |
| C — tilemap-as-world coupling | `TinyFarmAuthoredTileMap.TileAt` plus `EnsureStaticTileSprites` | Every 16 x 10 ground cell becomes one sprite (160 sprites); the four-cell grass motif repeats visibly and the scene reads as a grid. |
| C — tilemap-as-world coupling | `SceneObjectKind` to sprite selection | Semantic kind directly selects prototype art. This is convenient but leaves no recipe/approved-art boundary. |
| D — prototype workaround | `SceneLayoutRow.Layer` and native feet-Y sorting | All current authored layers are zero. Elevation and occlusion are implicit in screen-Y/pivot behavior rather than world height. |
| D — prototype workaround | hard-coded analytic primitive ground/object accents | Useful for proving the native path, but not an authored painterly scene model. |
| E — reusable substrate | `Aurelian.Spatial2D`, `Aurelian.GameWorld2D`, Field2D, Profile, native Vulkan rendering, Oblivion cards | Shapes, authoritative collision queries, ordered sprites, live field projection, and inspection are suitable reusable mechanisms. |

## Coupling findings

- Art implies collision for prototype placed objects because the renderer and collision adapter both consume the same `SceneLayoutRow` rectangle. The image alpha is not queried, but visual and physical extents are effectively one authored fact.
- Tile identity does not directly drive resolver gameplay. The stronger coupling is presentation: `TileAt` decides ground appearance per grid cell, while object kind selects its sprite. Farming's discrete crop cells are legitimate gameplay data and should remain local to farming rather than become the global world ontology.
- Elevation is implicit. Ground, bank, bridge, actor, and roof have no common `(x, y, z)` authoring law; world sprites are principally ordered by feet Y.
- Navigation is independent of art pixels and tile IDs, but coupled to `SceneLayoutRow` blockers. The DotRecast projection rebuilds rectangles derived from the presentation-era layout rather than from explicit surfaces, paths, and footprints.
- Collision is already resolver-owned through `SpatialWorld2D`, which is the correct authority seam. M24 should change its authored input for Riverside, not add another movement engine.

## Camera and native realization

The game uses an orthographic `Camera2D` over a 16 x 10 world and a 48-pixel-per-world-unit scale. The native path is `TinyFarm.Native -> NativeLayerCompositor -> VulkanOrderedQuadRenderer`. Riverside Field2D is uploaded and drawn independently of ground sprites, so it can remain live between a static bank/base pass and composed bridge/object passes.

## M24 boundary

Keep the established scene tables and tile renderer for the other scenes. For Riverside only, add semantic surfaces, one curved path, local patches, and placed objects; compile collision/navigation/occlusion/interaction from those facts; and project a painterly slab plus separately ordered objects through the existing native path. This is a demotion of tilemaps, not a purge.
