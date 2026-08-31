# Oblivion Diagram Canvas — M20b

## State and camera

`OblivionDiagramViewportState` is Card-local session state:

```text
Zoom
PanX
PanY
FitMode = Fit | Manual
```

It is keyed by Card ID in `OblivionSessionState`, independent for simultaneously visible Diagram Cards, reconciled away when Cards disappear, and never persisted into Diagram semantics or the vault.

Fit is the default. The camera computes `min(viewportWidth/worldWidth, viewportHeight/worldHeight)`, centers the world rectangle, and clips it to the Card body. Manual zoom is relative to Fit and bounded to 0.25×–4×. Pan is screen-space camera translation with a bounded range; it never changes SVG, PNG, Mermaid source, Diagram nodes, or edges. Reset returns to Fit with zoom 1 and pan 0.

## Interaction and commands

- Ctrl+wheel zooms the Diagram around its centered camera and changes Fit to Manual.
- Middle-drag pans and changes Fit to Manual.
- Normal wheel remains available to Page/document scrolling.
- Ctrl+0 fits; Ctrl++ and Ctrl+- zoom the focused Diagram in Standalone.
- `diagram.fit`
- `diagram.zoom-in`
- `diagram.zoom-out`
- `diagram.reset-view`

Semantic commands resolve the focused slot and reject a non-Diagram focused Card with `OBLIVION-DIAGRAM-FOCUSED-CARD-REQUIRED`.

## Backend independence

The camera hosts one world-space image. Mermaid still produces the qualified cached PNG once; zoom/pan changes only the image transform. The M20b native experiment emits SVG plus a raster realization from the same resolved geometry, then uses this same camera. There is no per-zoom render and no backend-specific navigation.

In the half-height vertical slot the Mermaid world is 784×535 in a 2318×444 canvas. Fit scale is 0.8299. At 1.8× zoom, scale is 1.4938; a 180,−80 pan demonstrates local navigation. This makes individual labels inspectable, but the global four-path task still requires repeated navigation and Mermaid routing remains ambiguous. Pan/zoom therefore does not, by itself, make this Mermaid layout sufficiently usable for the motivating constrained task.

No Diagram IR or reflection query changed.
