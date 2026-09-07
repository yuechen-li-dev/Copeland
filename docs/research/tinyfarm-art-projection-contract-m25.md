# TinyFarm art projection contract

TinyFarm uses a **north-up, fixed three-quarter orthographic art convention**, nominally 45 degrees above the ground and looking north, with zero horizontal yaw. This is the roof-plus-front-facade convention discussed with the user, not a perpendicular architectural roof plan and not a diagonally rotated isometric view.

- Building ridges, front eaves, window lintels, and thresholds run horizontally on screen. Door jambs run vertically. Parallel features stay parallel; there are no vanishing points.
- The south-facing facade is visible but shallow. The roof occupies roughly 60 percent of the building's visible height, the facade roughly 30 percent, and the entry/steps the remainder. These are guide proportions, not a renderer-enforced taxonomy.
- A building viewed from the south exposes its roof and south facade, with no large east/west side wall. A chimney may show its top and shallow front face, matching the main building.
- Trees need readable upper canopy and a short exposed trunk. Characters need visible heads and fronts. Their retained M24/M11 art is audited against this convention; this milestone does not regenerate the full catalog.
- Soft upper-left light, terracotta/cream/moss colors, and painterly material detail remain the style reference. Pixel-art reference images inform camera convention only; they are not copied as game art.

The current native game still uses its existing 2D semantic ground projection. The nominal elevation constrains **authored sprite appearance**, not a claim that the native camera has acquired a new 3D view matrix. Semantic coordinates, collision, navigation, and save/replay authority remain unchanged. M24's independent fixed-camera experiment remains available; converting all ground projection mathematics is outside M25.

The farmhouse control guide is `src/TinyFarm/TinyFarm.Native/Assets/M25/farmhouse-three-quarter-guide.svg`. It specifies the silhouette, eave, ridge, chimney cap, front windows, and door direction before painterly generation. The earlier roof-plan guide and generated roof-plan asset are retained as rejected perspective studies, not the chosen convention.

Fresh asset workflow: draw the guide in this convention, render the guide, use it as the geometry authority and approved M24 art as material reference, inspect the generated perspective, verify actual RGBA transparency, then verify the asset in the native world before selecting it.
