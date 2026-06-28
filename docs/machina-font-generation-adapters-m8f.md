# Machina Font Generation Adapters M8f

## Purpose

M8f lands the first compile-checked generation adapter seam inside `Machina.Fonts`. It adds Machina-owned outline records, generation diagnostics, fake outline loading, fake distance-field generation, and a small pipeline helper that proves the boundary without adopting any real font or MSDF dependency.

## Boundaries

M8f stays fully standalone inside `src/Machina.Fonts`.

- no `Typography.OpenFont`
- no `MSDF-Sharp.Core`
- no `SixLabors.Fonts`
- no `FreeType` or `SharpFont`
- no real font parsing
- no real outline extraction
- no real MSDF generation
- no image writing
- no renderer, Avalonia, Aurelian, or Vulkan integration
- no dependency on `reference/dominatus`

The existing fake atlas worker and artifact flow remain unchanged.

## Outline model

The new Machina-owned outline model lives under `src/Machina.Fonts/Generation/`:

- `GlyphPoint`
- `GlyphBounds`
- `GlyphContour`
- `GlyphOutline`
- `GlyphLineSegment`
- `GlyphQuadraticSegment`
- `GlyphCubicSegment`

These records validate finite numeric values, valid bounds, and non-null contour/segment collections. They intentionally stop short of winding normalization or orientation policy.

## Outline source interface

`IGlyphOutlineSource` is the seam for future real font loading and contour extraction:

```csharp
public interface IGlyphOutlineSource
{
    ValueTask<GlyphOutlineLoadResult> LoadGlyphOutlineAsync(
        FontFaceId face,
        int codepoint,
        GlyphOutlineLoadOptions options,
        CancellationToken cancellationToken = default);
}
```

`GlyphOutlineLoadResult` carries success state, optional outline, optional metrics, and generation diagnostics. Normal missing-glyph cases return diagnostics instead of throwing.

## Distance-field generator interface

`IGlyphDistanceFieldGenerator` is the seam for future SDF/PSDF/MSDF/MTSDF generation:

```csharp
public interface IGlyphDistanceFieldGenerator
{
    GeneratedGlyphDistanceField Generate(
        GlyphOutline outline,
        MsdfGenerationSettings settings,
        CancellationToken cancellationToken = default);
}
```

`GeneratedGlyphDistanceField` stores Machina-owned metadata plus flat float channel data. Cancellation propagates through `OperationCanceledException`.

## Fake outline source

`FakeGlyphOutlineSource` is deterministic and file-free.

- whitespace returns success with metrics and an empty outline
- configured missing codepoints return `MissingGlyph` diagnostics
- most glyphs produce one rectangular line contour
- `~` produces quadratic segments
- `&` produces cubic segments

This is intentionally synthetic. It exists to exercise the adapter seam and record model, not to simulate real font semantics in depth.

## Fake distance-field generator

`FakeGlyphDistanceFieldGenerator` validates data shape and emits deterministic float output derived from:

- glyph codepoint
- metrics
- contour count
- bounds
- output coordinates
- distance-field kind
- generation settings

It does not produce a real SDF or MSDF. It only proves the output contract and deterministic memory layout.

## Generation pipeline

`GlyphGenerationPipeline` composes the two seams:

1. load outline
2. stop early if the outline is missing or failed
3. remap the returned outline onto the requested `GlyphKey`
4. generate the fake distance field
5. combine diagnostics

Success requires both stages to complete without error-severity diagnostics.

## Diagnostics

M8f adds generation-local diagnostics:

- `InvalidGlyphKey`
- `MissingGlyph`
- `UnsupportedGlyph`
- `EmptyOutline`
- `OutlineLoadFailed`
- `DistanceFieldGenerationFailed`
- `Cancelled`
- `InvalidGenerationSettings`

Diagnostics are carried in outline-load, generated-field, and pipeline results. Cancellation remains exception-based instead of diagnostic-based.

## Tests

Focused tests cover:

- outline record validation
- deterministic outline generation
- metrics and whitespace behavior
- configured missing glyph diagnostics
- quadratic and cubic segment coverage
- distance-field channel counts and data length
- deterministic float output
- invalid settings rejection
- empty-outline diagnostics
- pipeline short-circuiting, combination, and cancellation propagation

## Deferred real dependencies

M8f deliberately does not adopt any third-party font or MSDF package yet. The adapter seam keeps future implementation choices swappable:

- `Typography.OpenFont` or another outline source can sit behind `IGlyphOutlineSource`
- `MSDF-Sharp.Core` or another generator can sit behind `IGlyphDistanceFieldGenerator`

This keeps Machina in control of its public contracts and avoids leaking third-party types across the boundary.

## M8g plan

M8g can now focus on one real outline-source proof without changing the rest of the generation contract:

1. choose the first packaging strategy for `Typography.OpenFont`
2. translate one fixture font into Machina-owned outline records
3. verify deterministic metrics and contour extraction
4. keep renderer integration and atlas-page image output deferred

## M8g landed follow-up

M8g now implements that first real outline-source proof.

- `Machina.Fonts` now references `WycliffeAssociates.Typography.OpenFont` `1.0.0`.
- `TypographyGlyphOutlineSource` sits behind `IGlyphOutlineSource`.
- one checked-in OFL fixture font is loaded from an explicit file path in tests.
- glyph metrics and contours now translate into Machina-owned outline records.
- the existing fake distance-field pipeline is now proven against a real outline source.

Still deferred after M8g:

- no `MSDF-Sharp.Core`
- no real distance-field generation
- no atlas integration
- no renderer integration
- no native fallback

See `docs/machina-typography-outline-adapter-m8g.md`.
