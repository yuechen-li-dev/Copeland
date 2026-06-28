# Machina CPU MSDF Reference Renderer M8k

This file now serves as the historical bridge note for the earlier single-glyph proof.

The current M8k milestone answer for string rendering is documented in:

- [Machina CPU MSDF Text Renderer M8k](machina-cpu-msdf-text-renderer-m8k.md)

## Purpose

The earlier proof established that packed distance-field atlas data could be sampled on the CPU and turned into a visible deterministic glyph image.

This is a validation milestone, not a runtime rendering integration milestone.

## Scope

Included:

- CPU-side `.dfpage` reading into a reference page model
- deterministic SDF/MSDF/MTSDF sampling helpers
- single-glyph rendering into a tiny RGBA image buffer
- dependency-free `.ppm` proof output
- synthetic sampling/render tests
- real Typography + `MSDF-Sharp.Core` + packing + artifact-read + render proof

Still excluded:

- Machina UI integration
- `TextBlock` integration
- component gallery integration
- Aurelian/Vulkan integration
- shader tuning as production behavior
- PNG encoding/decoding
- new native dependencies

## Why CPU reference rendering exists

M8i proved that real distance-field data can be packed and exported.

M8k answers the next question: can those `.dfpage` bytes actually be interpreted consistently enough to produce visible glyph pixels?

The reference path exists to validate:

- channel interpretation
- atlas rect and UV placement
- Y-orientation policy
- threshold behavior
- deterministic debug output

It is intentionally separate from the current raster text path and any future GPU renderer.

## Input data

The proof path consumes the existing M8i artifacts:

```text
TypographyGlyphOutlineSource
  -> GlyphGenerationPipeline
  -> MsdfSharpDistanceFieldGenerator
  -> GeneratedFieldAtlasPacker
  -> DistanceFieldAtlasArtifactExporter
  -> .font-atlas.toml + .dfpage
  -> DistanceFieldPageReferenceReader
  -> CpuDistanceFieldGlyphRenderer
  -> .ppm proof image
```

New reference-rendering code lives under `src/Machina.Fonts/ReferenceRendering/`.

## Sampling policy

Sampling is deterministic bilinear sampling over normalized page UVs.

Distance decoding policy:

- `Sdf` and `Psdf`: use the single scalar channel
- `Msdf`: use `median(r, g, b)`
- `Mtsdf`: use `median(r, g, b)` and intentionally ignore alpha for M8k

MTSDF alpha use is deferred until a later renderer contract needs it.

## Alpha / threshold policy

Coverage uses a deterministic smoothstep around an explicit threshold.

Current policy:

- default threshold: `0.5`
- explicit `PxRange`
- smoothing width derived from `PxRange` and output-to-glyph scale
- coverage = `smoothstep(threshold - smoothing, threshold + smoothing, distance)`

This is a proof policy, not final runtime shader tuning.

## Glyph render policy

Current API:

```csharp
public static class CpuDistanceFieldGlyphRenderer
{
    public static RgbaImage RenderGlyph(
        DistanceFieldPageReference page,
        GlyphAtlasEntry entry,
        DistanceFieldRenderOptions options);
}
```

Policy:

- render one glyph entry at a time
- map output pixels across the entry UV rectangle
- default Y policy is atlas-top-to-bottom (`FlipY = false`)
- optional `FlipY = true` exists to validate opposite orientation explicitly
- composite foreground over background before writing output

That limitation is what the newer text-renderer proof removes.

## PPM proof output

M8k adds a tiny binary PPM writer (`P6`).

Why PPM:

- dependency-free
- deterministic
- trivial to inspect
- no PNG/image package needed

Output writes composited RGB bytes only. Alpha remains internal to the in-memory RGBA image model.

## Typography + MSDF proof

The proof tests render `SpaceMono-Regular.ttf` glyph `A` through the full managed path and write a deterministic `.ppm` artifact in a temp directory during test execution.

The test asserts:

- export succeeds
- `.dfpage` reads back successfully
- the rendered image is non-blank
- repeated render/output bytes are deterministic

## Tests

New coverage includes:

- `DistanceFieldSamplingTests`
- `CpuDistanceFieldGlyphRendererTests`
- `PpmImageWriterTests`
- `TypographyMsdfReferenceRenderTests`

These cover synthetic sampling, rect placement, Y policy, foreground/background compositing, output determinism, PPM byte shape, and the real managed pipeline proof.

## Deferred issues

- no renderer or `TextBlock` integration
- no gallery integration
- no GPU/Vulkan/Aurelian consumer
- no PNG output
- no MTSDF alpha-channel usage beyond documented ignore-for-now behavior
- no final shader-quality antialiasing policy

## M8l plan

M8l can build on this proof by wiring the settled atlas conventions into a real renderer consumer, likely a GPU/MSDF path, without re-litigating page layout, UV conventions, or threshold basics.
