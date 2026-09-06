# Machina renderer-neutral nine-slice contract

Machina nine-slice follows this ownership chain:

```text
SpriteForge atlas metadata
-> MachinaNineSlicePrimitive
-> MachinaNineSliceLowerer
-> renderer adapter
```

`MachinaNineSlicePrimitive` contains a stable texture asset ID, atlas-space source rectangle, logical destination rectangle, source slice margins, edge and center modes, a border scale, and tint. It contains no Vulkan handles, descriptors, samplers, shaders, or swapchain state.

## Geometry law

Source margins are atlas pixels. `BorderScale` maps each complete source corner to a logical destination size. For example, a 76-pixel source margin at `0.5` becomes a 38-logical-pixel border. If the destination is smaller than both requested borders, the destination margins shrink proportionally and the center collapses to zero; negative geometry is never emitted.

Corners preserve their complete source regions and scale uniformly through the explicit border scale. An edge in `Stretch` mode spans its destination only along the edge's long axis. An edge in `Tile` mode emits repeated quads along that axis. A tiled center repeats along both axes. The last non-integral tile is cropped in source and destination space rather than stretched, overlapped, or omitted.

## Native sampling law

The Aurelian adapter lowers the semantic primitive to ordered textured quads. It uses the existing linear sampler with clamp addressing. Atlas subrect repetition is expressed as repeated quads, not hardware repeat across the atlas. UVs are inset to boundary texel centers, which prevents adjacent atlas regions from bleeding into a tile under linear sampling.

The adapter applies the same uniform logical-to-physical viewport transform used by background, portrait, overlay, and pointer inversion. Draw order remains the order produced by the semantic layer. A future WebGPU adapter can consume the same lowered rectangles and source subrects without changing Machina semantics.

## SUNKILL policy

SUNKILL uses the `dialogue` SpriteForge panel for the main-menu card and dialogue card. Its four edges use one-dimensional stretch and its source corners are presented at half scale. The existing Machina analytic button styles remain unchanged; the authored `button` atlas panel is available metadata but is not applied by SUNKILL.
