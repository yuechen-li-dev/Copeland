# Wilderland polish-gap audit — M19

This is a visual-pressure audit, not an attempt to copy another game's art.

| Visible gap | M19 classification | Result |
| --- | --- | --- |
| Flat square terrain | Profile native realization and app-local style | Default Mossward now uses native Profile diamond tiles, path and water bands |
| Hard exploration boundary | Fog shader | Soft sampled transition with distinct unexplored, explored and visible levels |
| Blurred figures under fog | Composition-order plus MSDF reconstruction defect | Corrected to terrain -> fog -> crisp vector actors/buildings, then raised component-field resolution and added shared small-screen reconstruction compensation |
| Limited terrain variation | Asset-authoring issue | Three bounded tile recipes landed; richer patch vocabulary deferred |
| Flat asset color | Asset-authoring issue | Existing painter-layer facets and shadows survive native realization; gradients were not needed for the nine-asset pack |
| Weak depth | Profile composition recipes | Existing shadow ellipses, roofs and facets are preserved exactly |
| Building detail | Asset-authoring issue | Native path preserves all HQ/watchtower/lodge layers; additional authored detail is future work |
| Figure detail | Asset-authoring issue | 192 px Profile fields with a 40 px short-axis floor retain sharper edges; new silhouettes/poses are deferred |
| Motion | Deferred | Transform animation is possible, but the milestone did not justify an animation system |
| HUD structure | Solved in M18 | The same `StrategyHudProfile`, Crimson Text/Space Mono sources, copy and resolved layout are retained; 64 px minimum glyph fields and shared tiny-MSDF compensation sharpen small labels |
| Development inspection | App-local style/tooling | F10 spawns/despawns a small Machina overlay with Profile-cache and fog facts |
| Atmospheric depth beyond visibility | Deferred | No height fog, shafts, lighting, PBR, or post-processing graph was added |

The largest remaining visual gap is authored terrain/figure variety, not a missing native graphics abstraction. M20 should respond to measured repeated-draw pressure rather than expand paint features speculatively.
