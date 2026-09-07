# Procedural strategy art: source mechanism and re-expression

The two useful visual references are [Astra / Wilderland](https://senko.net/vibecode-bench/2026/rts-gpt-6-astra.html) and [Sol](https://senko.net/vibecode-bench/2026/rts-gpt-5.6-sol.html). The source hash and symbol references are recorded in `corpus-audit.json`; the visual audit is summarized in `wilderland-art-audit.json`. No benchmark image or drawing function was imported into the game.

## Wilderland's actual mechanism

World art is immediate Canvas2D drawing, not pre-authored raster sprites. Small polygon, ellipse and line helpers compose shaded isometric boxes, gabled roofs, roof seams, faceted foliage, people, tools and resources. Translation, scaling, rotation and alpha assemble these parts. The portrait helper calls the same world unit/building drawing routines into an offscreen canvas and turns that result into a data URL. Reusing the asset for portrait and world is more useful than reproducing a specific portrait image.

The UI has a separate mechanism: HTML/CSS layout, restrained gradients/translucency, inline SVG symbols, media-query changes and text hierarchy. It is not one giant canvas. A slim resource header, an upper-left mission card, a large world area and a bottom selection/action/build/minimap dock establish hierarchy. Cream text and masonry, muted forest greens, terracotta and teal roofs, and sparse gold accents keep many elements coherent. The lower-contrast controls are subordinate to actions.

The world projects a grid point to `((x-y)*tileWidth/2, (x+y)*tileHeight/2)` before camera translation/zoom. The inverse serves picking; the minimap has its own semantic projection. Depth follows world position. Fog combines discrete explored/visible facts with a softer presentation strength; the softness must never grant sight to the simulation.

## Sol's different useful vocabulary

Sol builds an industrial science-fiction vocabulary with rounded silhouettes, nested chassis, vents, bands, rotor details and glowing crystals. Radial gradients soften glows; repeated angular crystal placements create richness cheaply. Animation is mostly transforms/pulses of composed primitives. These are evidence for palette application, repeated detail and reusable composition, not a reason to create a scene graph or import a second vector format.

## Capability decisions

| Source pattern | Existing authority / M18 treatment | Classification |
| --- | --- | --- |
| Polygon, ellipse, circle, thick line | Existing Profile `Polygon`, `Ellipse`, `Circle`, `Tube`, lowered to canonical contours | Existing primitive |
| Painter order and silhouette parts | Existing named `Layer` / `ProfileSource`; overlapping paint stays distinct | Existing primitive |
| Isometric walls, roof planes, ground shadow | Ordinary `Block`, `Roof`, `Shadow` functions in StrategyArt.ts | Reusable proof templates |
| Figure and small lodge family | `Figure` and `Lodge` with explicit typed palette/shape parameters | Reusable proof templates |
| Palette variants | `StrategyPalette`, ordinary records and `with`; no CSS-like selectors | Reusable authoring technique |
| Worker, heavy, ranger, HQ, lodge, tree, crystal, marker, watchtower | Nine original Profile assets; same compiled paths draw world, portrait and pack | Reusable proof assets |
| Terrain mottling, decorative pines, colors | Deterministic coordinate-derived sample drawing; no terrain authority | App-local style |
| Gradient glow, animated detail, soft fog | Not implemented in the bounded flat-fill adapter | Deferred presentation capability |
| Asset card inspection | Existing M17 cards compile ObjectAsset panels/regions, not Profile compositions | Deferred exact tooling seam |

An illustrative re-expression is `Block("Tower", 0, 0, 18, 9, 60, Palette)` followed by a named `Roof` and window paint. It describes shaded closed faces with semantic names. It does not paste the benchmark's Canvas code or hide an SVG parser inside a template.

## Qualified path and remaining seam

`StrategyArt.ts + *.profile.tsx -> ProfileTsxCompiler.CompileComposition -> resolved Profile paint items -> cached SKPath/SKPaint -> Avalonia bitmap presenter` is exercised by the real sample. It keeps Copeland binding/static evaluation and contour closure authoritative. The leaf flips Y once; it does not reverse canonical contours. Compilation diagnostics fail startup instead of silently substituting shapes.

The first pack qualifies flat fills, named painter order, reuse and canonical compilation hashes. It does not qualify every Profile paint style: stroke/gradient/opacity, arbitrary clipping, GPU tessellation, atlas baking, MSDF sprites, animation, hit regions or object-card editing. The adapter is deliberately a sample leaf, not a general renderer.

M17 `OblivionSpriteCardService.BuildProjection` calls `ObjectAssetCompiler.Compile`, then inspects a panel and atlas regions. Feeding it a Profile composition would cross a real document boundary. M18 leaves those cards intact and renders a compiler-produced asset pack instead. The next narrow owner task is a canonical Profile composition preview/cache adapter with a second game consumer and paint-support diagnostics, followed by a source-linked card projection. A general vector editor is unnecessary.

## Visual verdict

`wilderland-style-proof.png` demonstrates the diamond world, semantic dock/header/card, coherent woodland palette, serif hierarchy and reusable first-party vectors. `forest-farming-restyle.png` demonstrates a second fact/style projection. Both were visually inspected. M18 still has sparse terrain, hard fog edges, static figures and simpler detail density than Wilderland. Comparable polish is **not qualified**. This is the principal Outcome B boundary, not evidence that the benchmark lacks useful art.
