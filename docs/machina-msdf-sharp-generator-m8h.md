# Machina MSDF-Sharp Generator M8h

## Purpose

M8h lands the first real distance-field generation proof behind Machina's `IGlyphDistanceFieldGenerator` seam.

This milestone proves:

- Machina-owned outlines convert into `MSDF-Sharp.Core` shapes
- real `SDF`, `PSDF`, `MSDF`, and `MTSDF` generation runs inside `Machina.Fonts`
- the existing Typography fixture outlines can flow through the existing `GlyphGenerationPipeline`

This milestone still does **not** add atlas integration, TOML export integration, PNG writing, renderer integration, TextBlock/gallery changes, Aurelian/Vulkan work, or native dependencies.

## Dependency choice

Chosen package:

- `MSDF-Sharp.Core` `1.0.2`

Package evidence:

- NuGet registration metadata shows `MSDF-Sharp.Core` `1.0.2`
- TFM: `net9.0`
- license expression: `MIT`
- package dependencies: none

Scope rule:

- `MSDF-Sharp.Core` is the only new MSDF dependency in M8h
- `MSDF-Sharp.Extensions` is intentionally **not** used
- no `SixLabors.ImageSharp`
- no `SixLabors.Fonts`
- no `FreeType` or `SharpFont`

## Adapter shape

New implementation lives under `src/Machina.Fonts/Generation/MsdfSharp/`:

- `MsdfSharpDistanceFieldGenerator`
- `MsdfSharpShapeConverter`
- `MsdfSharpShapeConversion`

Public seam:

```csharp
public sealed class MsdfSharpDistanceFieldGenerator : IGlyphDistanceFieldGenerator
{
    public GeneratedGlyphDistanceField Generate(
        GlyphOutline outline,
        MsdfGenerationSettings settings,
        CancellationToken cancellationToken = default);
}
```

`Msdfgen` types stay internal to the adapter layer.

## Shape conversion

Current mapping:

- `GlyphOutline` -> `Shape`
- `GlyphContour` -> `Contour`
- `GlyphLineSegment` -> `LinearSegment`
- `GlyphQuadraticSegment` -> `QuadraticSegment`
- `GlyphCubicSegment` -> `CubicSegment`

Conversion behavior:

- one Machina contour becomes one `Msdfgen.Contour`
- edge control points are copied directly from Machina outline coordinates
- all edges start with `EdgeColor.WHITE`
- shape Y orientation is set to `Upward`

Policy:

- empty outlines fail conversion with `EmptyOutline`
- this includes whitespace outlines for the current proof
- no extra outline normalization is introduced before conversion

## Settings mapping

`MsdfGenerationSettings` maps into `MSDF-Sharp.Core` like this:

- `Kind` -> `GenerateSDF` / `GeneratePSDF` / `GenerateMSDF` / `GenerateMTSDF`
- `Width`, `Height` -> `Bitmap<float>(width, height, channels)`
- `PixelRange` -> `Range(pixelRange)` and `DistanceMapping`
- `Scale` -> multiplier on the fit-to-bitmap projection scale
- `EdgeColoring` -> `EdgeColoringSimple` or `EdgeColoringInkTrap`
- `MiterLimit` -> used when calculating fit bounds policy only indirectly in this proof; no explicit bound-miter expansion is applied yet

Pinned deterministic defaults:

- overlap support: `false`
- edge-coloring seed: `0`
- angle threshold: `3.0`
- MSDF error correction: `ErrorCorrectionConfig.Default`

Projection policy:

- compute drawable area as `width - 2 * pixelRange` by `height - 2 * pixelRange`
- fit the outline bounds into that drawable area
- multiply the fit scale by `settings.Scale`
- center the outline in the output bitmap

## Output data contract

`GeneratedGlyphDistanceField` returns:

- the input `GlyphKey`
- the input `GlyphMetrics`
- requested width and height
- requested `DistanceFieldKind`
- channel count:
  - `Sdf` -> `1`
  - `Psdf` -> `1`
  - `Msdf` -> `3`
  - `Mtsdf` -> `4`
- flat `ReadOnlyMemory<float>` data with length `width * height * channelCount`

Current proof guarantees:

- same-process deterministic output for the same outline and settings
- finite float data on successful generation
- non-uniform values for visible glyphs

Current proof does **not** promise:

- exact byte identity across platforms
- exact byte identity across different runtimes or CPU architectures

## Diagnostics

Current diagnostic behavior:

- invalid projection or unsupported edge-coloring mode -> `InvalidGenerationSettings`
- empty outline -> `EmptyOutline`
- shape conversion or package failures -> `DistanceFieldGenerationFailed`
- cancellation remains exception-based via `OperationCanceledException`

Failure result policy:

- generator failures return a `GeneratedGlyphDistanceField` with the requested dimensions and channel count
- data is zero-filled on failure paths
- diagnostics carry the error state

## Typography pipeline proof

M8h proves the existing real outline path from M8g can feed the new generator:

- `TypographyGlyphOutlineSource` loads `SpaceMono-Regular.ttf`
- `GlyphGenerationPipeline` remaps the outline onto the requested `GlyphKey`
- `MsdfSharpDistanceFieldGenerator` generates real distance-field data

Covered fixture glyphs:

- `A`
- `a`
- `0`
- `&`
- missing glyph via `U+E000`
- whitespace via `U+0020`

Whitespace policy for M8h:

- Typography still loads space successfully with metrics and no contours
- MSDF generation currently treats empty outlines as an error
- pipeline result for space is therefore unsuccessful and carries `EmptyOutline`

## Determinism notes

Determinism is currently tested for repeated generation inside the same process and runtime.

Why the docs stay narrower than that:

- the package is float-based
- edge-coloring and correction are deterministic in-process with pinned settings
- broader byte-stable guarantees across machines are not yet audited

## Tests

Focused tests now cover:

- line/quadratic/cubic shape conversion
- empty-outline conversion rejection
- deterministic shape conversion
- channel counts for `SDF`, `PSDF`, `MSDF`, and `MTSDF`
- finite and non-uniform output for a real Typography fixture glyph
- deterministic repeated generation
- invalid settings diagnostics
- empty-outline diagnostics
- cancellation propagation
- Typography-to-MSDF pipeline success, determinism, missing-glyph short-circuiting, and whitespace policy

## Deferred issues

- no `MSDF-Sharp.Extensions`
- no SixLabors dependency
- no FreeType/native fallback
- no atlas integration yet
- no `.font-atlas.toml` export integration yet
- no PNG output yet
- no renderer/TextBlock/gallery integration
- no content-hash stability promise for cross-platform output
- no page packing or artifact quantization policy yet

## M8i plan

M8i can now focus on real atlas/output integration using the two real seams already proven:

1. keep `TypographyGlyphOutlineSource`
2. keep `MsdfSharpDistanceFieldGenerator`
3. integrate generated fields into atlas-page assembly and artifact export
4. delay renderer consumption until the CPU-side contracts settle

## M8i follow-up

M8i lands that atlas/output integration.

- real generated fields are now packed into deterministic shelf-packed pages
- `.dfpage` artifacts now store real float/channel data instead of fake text-only placeholders
- shared artifact import validates hashes, dimensions, channels, and payload length
- whitespace remains metrics-only and is skipped from atlas rect export

Still deferred after M8i:

- no renderer/TextBlock/gallery integration
- no PNG output
- no Vulkan/Aurelian dependency

## M8k follow-up

M8k now proves that the generated and packed field data from M8h + M8i is actually sampleable as visible glyph output.

- `.dfpage` artifacts are read back through a CPU reference page reader
- `MSDF` sampling uses median RGB on the CPU just as the eventual renderer path is expected to
- deterministic `.ppm` proof images now exist without adding a PNG/image dependency
- GPU/runtime integration remains deferred
