# Machina CPU MSDF Text Renderer M8k

## Purpose

M8k proves that the real managed Machina font-atlas pipeline can render a short readable string, not just an isolated glyph.

This is still a CPU reference/debug milestone inside standalone `Machina.Fonts`. It is not the final UI text renderer.

## Scope

Included:

- single-line left-to-right string input
- rune/codepoint collection into `GlyphKey` values
- real Typography outline extraction
- real `MSDF-Sharp.Core` generation
- deterministic atlas packing
- `.font-atlas.toml` + `.dfpage` export/import/readback
- CPU placement by glyph metrics and advance
- whitespace as metrics-only spacing
- deterministic RGBA composition
- dependency-free `.ppm` proof output

Still excluded:

- `TextBlock` integration
- component gallery integration
- Machina renderer replacement
- Vulkan or Aurelian integration
- PNG dependencies
- shaping, kerning, ligatures, bidi, grapheme clustering, fallback

## Pipeline

```text
string
  -> DistanceFieldTextRun
  -> GlyphGenerationPipeline
  -> MsdfSharpDistanceFieldGenerator
  -> GeneratedFieldAtlasPacker
  -> DistanceFieldAtlasArtifactExporter
  -> .font-atlas.toml + .dfpage
  -> FontAtlasArtifactImporter
  -> DistanceFieldPageReferenceReader
  -> DistanceFieldTextLayout
  -> CpuDistanceFieldTextRenderer
  -> .ppm proof image
```

## Text layout policy

Current proof layout is intentionally small:

- one input string
- one face
- one em size
- one baseline
- one advance stream
- no kerning or shaping

Placement policy:

- start pen at `options.X`
- place each glyph at the current pen X
- advance pen by `GlyphMetrics.Advance`
- keep baseline fixed at `options.BaselineY`
- use deterministic midpoint-away-from-zero rounding when converting final double placement to pixel coordinates

## Whitespace policy

Whitespace is metrics-only.

- Typography outline loading still provides metrics
- MSDF generation reports `EmptyOutline`
- the pipeline preserves metrics and advance
- packing skips atlas entry creation
- rendering advances the pen and draws no quad

Whitespace is therefore not treated as a missing glyph.

## Missing glyph policy

Missing visible glyphs fail the proof result.

- outline-load missing glyph diagnostics are preserved
- the pipeline returns `Success = false`
- the text renderer also throws if a visible placement is missing an atlas entry or page

This keeps the proof honest while the contract is still settling.

## Baseline / bearing policy

The renderer uses glyph metrics for placement and then compensates for the fixed-size distance-field canvas.

Current formula:

- X origin starts from pen X plus `BearingX`
- Y origin starts from baseline Y minus `BearingY`
- a centered field-padding adjustment is subtracted so the fixed-size generated field stays aligned with the metric box

This is a proof convention for the current centered-field generator. A final runtime renderer may choose a different atlas contract later.

## Coordinate orientation

Current convention after M8l:

- `.dfpage` page data is interpreted top-to-bottom
- output image rows are also top-to-bottom
- `FlipY` only changes sampling direction inside the glyph UV rect
- the real Typography/MSDF proof path currently renders upright with `FlipY = true`

That is a stabilized proof convention, not yet a locked production renderer contract.

## Sampling and compositing

Sampling remains the same managed proof policy introduced by the earlier glyph renderer:

- bilinear sampling over page UVs
- `Sdf` / `Psdf` use the scalar channel
- `Msdf` uses median RGB
- `Mtsdf` still ignores alpha for now

Foreground coverage is composited over the existing RGBA target in draw order.

## PPM proof output

Proof images are written as binary `P6` PPM files.

Why PPM:

- no PNG library dependency
- deterministic byte layout
- trivial header validation in tests
- easy local inspection

## Tests

Coverage now includes:

- synthetic string-render placement tests
- baseline and whitespace behavior tests
- missing-atlas-entry rejection tests
- real Typography + MSDF + pack + export/import + readback text proof tests
- deterministic PPM output checks

## Deferred issues

- no `TextBlock` or Standard.Text integration
- no production gallery text integration
- no renderer replacement
- no Vulkan/Aurelian path
- no kerning/shaping/fallback
- no multiline layout
- no HiDPI/runtime scale negotiation beyond the proof em-size path
- the current `FlipY = true` Typography/MSDF proof orientation is documented but not yet promoted into a broader runtime atlas contract
- centered field-padding compensation is still proof-only

MSDF still matters because it should avoid a per-scale bitmap atlas explosion, but final renderer integration is deferred.

## M8l and M8m follow-up

M8l builds on this by adding repeatable proof export, visual inspection, and convention stabilization without crossing into production UI integration.

That follow-up reuses:

- the real outline and MSDF generator seam
- the packed atlas artifact conventions
- the single-line placement proof lessons

without claiming that this CPU debug path is itself the final text backend.

M8m then consumes the same CPU proof path from the component gallery sample in an opt-in export-only card, still without replacing `UI.Text`, `TextBlock`, or the raster renderer. See `docs/Machina.UI/history/machina-component-gallery-msdf-proof-m8m.md`.

## M8n follow-up

M8n keeps this renderer in the proof/reference lane but hardens two important conventions:

- field-canvas placement now mirrors the generator's fit-to-drawable-area convention instead of inferring padding only from `fieldSize - metricsSize`
- layout can now consume optional Machina-owned pair adjustments before advancing the pen

The renderer is still not a production text backend:

- no `TextBlock` integration
- no production renderer integration
- no shaping, ligatures, bidi, or fallback engine
## M8p update

M8k's original proof renderer assumed it could reconstruct glyph field placement from metrics plus fixed field tile dimensions.
M8p narrows that contract:

- the renderer now consumes explicit `GlyphFieldPlacement` plane bounds from atlas entries
- draw width/height come from stored plane bounds, not raw atlas tile size
- centered-field compensation is removed from the main proof path

This remains standalone `Machina.Fonts` proof rendering only.
