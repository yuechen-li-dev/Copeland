# TINYFARM-SEMANTIC-SPATIAL-ART-AUTHORING-M24 report

## 1. Outcome A

The bounded Riverside/Farmhouse dogfood qualifies semantic spatial authoring as the better TinyFarm default. Riverside now runs from explicit world surfaces, path geometry, patches, and object footprints to independent navigation/collision/occlusion/interaction projections, while the native scene uses one painterly meadow slab, local overlays, the live M21 water field, and separately composed objects. Other scenes retain their tile realization.

## 2. Historical/problem framing

The prototype paid the historical tilemap tax: 160 regular ground sprites made production cheap but made a place look assembled from repeated cells. Modern generated assets and the existing native GPU path make a coherent illustrated slab practical, while semantic data gives agents a much smaller and safer edit surface.

## 3. Current TinyFarm tile/world coupling

The baseline audit is in `docs/research/tinyfarm-spatial-authoring-baseline-m24.md`. Gameplay already avoided pixel lookup, but navigation and collision shared layout rectangles with prototype object presentation; elevation was implicit, and `TinyFarmAuthoredTileMap` emitted one ground sprite per cell.

## 4. New authoring law

`semantic spatial intent -> navigation | collision | occlusion | interaction | presentation`. Art and physics are sibling projections. Neither pixels nor tile IDs are consulted by traversal.

## 5. Semantic spatial vocabulary

`Aurelian.Spatial2D` now provides the bounded reusable records `WorldSurface`, `WorldPath`, `WorldPatch`, `WorldObject`, footprints, presentation recipes, compiled cells, and a fixed world camera. No volume, terrain mesh, material graph, or general editor was added.

## 6. WorldSurface

Riverside defines meadow, restricted river, bridge deck, and crop plot polygons with stable IDs, elevation, semantic material, traversal policy, and recipe reference. Texture coordinates are absent from semantic truth.

## 7. WorldPath

The Farmhouse-to-Well-to-SouthGate dirt path is one centerline with width and material. The native ribbon and nav labeling read that geometry directly; there are no straight/corner road tiles.

## 8. WorldObject

The scene places one farmhouse, four trees, a well, a fence segment, and an exit. Each has a stable transform, independent collision/occlusion/interaction footprints, height, and recipe.

## 9. 2.5D coordinate model

Semantic positions are `(x, y, z)` metres. Ground is z=0, water is below ground, the bridge deck is elevated, and trees/farmhouse carry height. The implementation deliberately stops at planes and simple footprints.

## 10. Camera/projection model

`FixedWorldCamera` projects the same points through top-down and oblique fixed views. `camera-projection-proof.png` demonstrates re-projection without changing semantic world, collision, or nav.

## 11. Navigation compilation

`SemanticWorldCompiler` creates a bounded 0.5 m traversability grid from surface/path policies and object collision footprints. Riverside path queries use a simple BFS over these cells; all other scenes keep the existing DotRecast path. Textures and tile identities are never inspected.

## 12. Collision compilation

Object footprints compile to the existing `SpatialWorld2D`; the warmed resolver remains the sole movement authority. Tree trunks are small circles, farmhouse walls are a box, and the fence is a segment.

## 13. Occlusion compilation

Occlusion is a separate shape projection. Tree canopy circles and the farmhouse roof envelope are larger than collision, allowing visual cover without inventing physical walls.

## 14. Interaction regions

Door, well, four tree-chop regions, and the Riverside exit are stable semantic triggers. The crop plot and bridge remain explicit semantic surfaces rather than being inferred from pixels. No pixel color, alpha, or tile-number lookup exists.

## 15. Tilemap role after M24

Tilemaps remain supported for patterned floors, retro/pixel styles, modular repeated architecture, tactical cells, and genuinely discrete gameplay. They are simply not Riverside world authority.

## 16. Slab presentation

`SurfacePresentationKind.Slab` is a renderer-neutral recipe kind. The approved meadow asset maps once, full-UV, across the bounded 16 x 10 m scene and is cacheable.

## 17. Base meadow realization

The 1254 x 1254 painterly meadow is one coherent gouache/storybook illustration with no internal repeat. It replaces 160 ground sprites in Riverside and is visibly less tiled.

## 18. Path realization

The native presenter samples the authored path centerline into an irregular-edged analytic dirt ribbon. Editing the centerline changes the ribbon without tile surgery.

## 19. Riverside integration

The real M21 `TinyFarmFieldRuntime` remains live and is drawn after the base/bank and before bridge/world objects. Wet mask, combat disturbance, charge, save/replay, and field uploads are not flattened into the slab.

## 20. Tree model

Four semantic trees use one approved painterly sprite. A 0.34 m trunk collision, larger canopy occlusion, chop interaction, and height are independent facts. Existing feet-Y ordering interleaves trees and actors.

## 21. Farmhouse model

The farmhouse has a 3.6 x 2.3 m blocked wall footprint, 4.6 x 3.3 m roof occlusion, height, and a door trigger. Its larger transparent painterly image has no authority over those dimensions.

## 22. Fence/crop model

One fence is a semantic segment obstruction whose repeated Profile art remains an appropriate realization. The crop plot is a surface; only live crop mechanics retain their local cell grid.

## 23. Bake/composed/live classification

BAKED: meadow variation, tiny stones, minor weeds/flowers, and noninteractive ground accents. COMPOSED: trees, farmhouse, fence, well, bridge, and crop plot. LIVE: player/NPCs, Field2D water, crop state, doors, drops, and effects.

## 24. Scene bake

The reproducible evidence tool emits `static-scene-bake.png` from semantic static scene plus approved assets. It excludes interactive/live objects; runtime intentionally chooses composition so the live river and ordering remain inspectable.

## 25. Presentation recipes

The small renderer-neutral recipe enum covers slab, overlay, sprite, Profile, and extrusion. Riverside uses only the first four. There is no shader/material graph.

## 26. Generated-art workflow

Four assets were generated with OpenAI's built-in image generation, selected once, copied into `Assets/M24`, and consumed locally. Runtime does no generation and now rejects meadow/farmhouse/tree bytes whose file SHA-256 differs from the approved value.

## 27. Style authority

`style-authority.json` fixes palette, warm upper-left light, moderate saturation/value, soft gouache edges, readable silhouettes, compact prop proportions, and a fixed three-quarter/top-down assumption. This is descriptive authority, not a style-transfer system.

## 28. Provenance model

`generated-asset-provenance.json` records semantic subject, style, actual dimensions, intended camera, prompt spec, built-in source, transparency, selection note, and exact file hash. Human/procedural/generated assets would cross the same approved-art boundary; only metadata differs.

## 29. Oblivion spatial inspection

`TinyFarmOblivionLiveSurfaces` now registers `tinyfarm.spatial.m24` beside field and combat. It reports semantic IDs, projection counts, recipes, and independently inspectable surfaces/path/patches/footprints/collision/nav/occlusion/presentation facts. `semantic-layer-inspector.png` and `nav-collision-overlay.png` are headless equivalents.

## 30. Fresh flower-meadow edit

The fresh edit adds one `WorldPatch` and overlay recipe beside the path. It touches no base grass pixels or tiles and is covered by the focused M24 tests.

## 31. Fresh path edit

Changing centerline points makes the route curve around the well; compilation and native path presentation consume the same semantic edit. `path-edit-proof.png` shows before/after geometry.

## 32. Fresh tree edit

Moving a tree two metres east and enlarging only its canopy changes transform and occlusion while retaining the exact trunk radius. `tree-footprint-proof.png` and tests establish the non-identity.

## 33. Camera reprojection

The camera proof projects identical world points through two fixed transforms and asserts identical compiled collision/nav. No scene reauthoring occurs.

## 34. Farmhouse asset replacement

`farmhouse-replacement-proof.png` places two approved realizations side by side while holding farmhouse ID, collision, occlusion, interaction, height, and navigation constant.

## 35. Slab seam behavior

The meadow has one full-image mapping and therefore no internal repeat boundaries. The live-water boundary has a static green/brown bank overlay and independent bridge pass. The seam is stable and clean at the intended 48 px/m scale, though its bank transition remains intentionally simple.

## 36. Memory/streaming

The meadow occupies about 6.29 MB RGBA; all four approved proof assets total about 25.17 MB uncompressed. Runtime uploads four sprite resources once, reuses them for four trees, and uploads the small live field each frame. Multi-slab composition is supported by multiple recipe/surface records without requiring a 16K monolith.

## 37. Tilemap comparison

The honest comparison is in `tilemap-comparison.json`: 160 prototype ground primitives versus four surfaces, one path, two patches, eight objects, and one runtime ground slab. Semantic mode adds explicit recipes/provenance and texture memory, but removes grid repetition and separates edit domains.

## 38. Authoring-cost comparison

`authoring-cost.json` records representative costs. Adding flowers, moving a path, moving/enlarging a tree, or changing collision is a one-file semantic edit with no generated asset or renderer work. Farmhouse replacement is one approved asset plus a recipe/hash update, with no renderer or physics edit.

## 39. Visual before/after

`prototype-before.png` is the shipped repeated-tile Farm presentation; `painterly-after.png` is a real Vulkan capture of Riverside. The after image reads as a cohesive illustrated meadow with soft variation and painterly independent props while retaining clear player, well, crop, bridge, river, and UI silhouettes.

## 40. Performance

`performance.json` is the authority. It includes 120-frame prototype and semantic native samples on an NVIDIA GeForce RTX 3070, per-frame draw-call comparison, p50/p95/p99/worst frame time, four one-time sprite uploads, live-field uploads, 14 world sprites, 0.5 m semantic compile/query timing, bake time, and asset memory. This is local evidence, not remote CI.

## 41. What failed / needs refinement

The first native proof exposed a hard bank edge, so a dedicated post-field bank overlay was added and recaptured. Remaining refinement is presentation-only: the bank transition is geometrically clean but still simpler than the generated meadow, and generated images share the current nearest-sampled ordered sprite scope. Neither issue leaks into semantic authority.

## 42. Exact M25 recommendation

M25 should do one thing: qualify an ordered painterly-resource path with linear filtering plus a semantic alpha bank mask, while preserving the existing mixed-resource feet-Y order and live Field2D placement. Do not add an editor, material graph, general terrain, or new navigation system.

## 43. Diff stat

The final `git diff --stat` is captured in the completion response because generated/untracked files are not represented reliably by an in-document pre-commit diff. Scope is limited to Aurelian Spatial2D, TinyFarm Core/Runtime/Native, one Oblivion integration, focused tests, one evidence tool, approved assets, artifacts, and these two documents.
