# Machina Distance Field Atlas Packing M8i

## Purpose

M8i proves that real generated distance-field data can become deterministic atlas assets inside standalone `Machina.Fonts`.

## Scope

Included:

- deterministic generated-field atlas packing,
- real page-buffer assembly from `GeneratedGlyphDistanceField`,
- `.dfpage` artifact export,
- `.font-atlas.toml` export/import/validation roundtrip,
- Typography + `MSDF-Sharp.Core` integration proof.

Still excluded:

- renderer integration,
- `TextBlock` or gallery integration,
- PNG encoding/decoding,
- Vulkan or Aurelian work,
- native dependencies.

## Whitespace / metrics-only policy

Whitespace and other empty-outline glyphs are treated as metrics-only.

- Typography outline extraction can succeed for whitespace with metrics and empty contours.
- distance-field generation still reports `EmptyOutline`.
- M8i does not invent zero-sized atlas entries.
- metrics-only glyphs are skipped during packing and are not serialized into atlas page rect entries.

Full serialized metrics-only glyph support is deferred until a deliberate model is needed.

## Generated field packer

New API:

```csharp
public sealed class GeneratedFieldAtlasPacker
{
    public GeneratedFieldAtlasPackResult Pack(
        IReadOnlyList<GeneratedGlyphDistanceField> fields,
        GeneratedFieldAtlasPackOptions options);
}
```

The pack result contains:

- immutable `FontAtlasSnapshot`,
- concrete packed page buffers with float data,
- diagnostics for metrics-only skips and hard packing failures.

## Packing algorithm

M8i uses deterministic shelf packing.

Sort order:

1. height descending
2. width descending
3. face ordinal
4. em size
5. weight
6. slant
7. codepoint

Placement policy:

- pack left-to-right within the current shelf,
- start a new shelf when the glyph no longer fits the row,
- start a new page when the glyph no longer fits the page,
- reject a glyph that cannot fit an empty page,
- keep padding as separation only,
- keep `GlyphAtlasEntry` rects and UVs on the actual copied field region, not the padding.

## Page data placement

Each generated field copies its real float data into a `float[]` page buffer sized as:

`pageWidth * pageHeight * channelCount`

Policy:

- page background is zero-filled,
- field data copies row-by-row into the page buffer,
- padding does not receive copied field pixels,
- all fields in one pack call must share one distance-field kind/channel count.

## DF page artifact format

M8i adds deterministic `.dfpage` artifacts.

These are not images and not PNG files.

Format shape:

- ASCII header,
- fixed `format=1`,
- distance-field kind,
- page index,
- width,
- height,
- channel count,
- `data=float32-le`,
- sorted glyph codepoint list,
- `---DATA---` marker,
- raw little-endian `float32` payload.

Content hashes are SHA-256 of the exact file bytes.

## Export flow

```text
TypographyGlyphOutlineSource
  -> GlyphGenerationPipeline
  -> MsdfSharpDistanceFieldGenerator
  -> GeneratedFieldAtlasPacker
  -> DistanceFieldAtlasArtifactExporter
  -> .dfpage + .font-atlas.toml
```

`DistanceFieldAtlasArtifactExporter`:

- writes `.dfpage` files first,
- computes content hashes,
- writes canonical TOML page metadata and glyph rect/UV/metric entries,
- preserves standalone `Machina.Fonts` boundaries.

## Import / validation flow

`FontAtlasArtifactImporter` now validates `.dfpage` artifacts through the shared page validator.

Checked conditions:

- missing page file,
- content hash mismatch,
- invalid header/format,
- page index mismatch,
- width/height mismatch,
- channel count mismatch,
- payload length mismatch.

Successful import still roundtrips through `FontAtlasSnapshot`.

## Typography + MSDF integration proof

M8i proves this real managed path:

```text
SpaceMono-Regular.ttf
  -> Typography outlines
  -> MSDF-Sharp.Core float generation
  -> deterministic page packing
  -> .dfpage + .font-atlas.toml export
  -> import validation
  -> equivalent snapshot
```

Primary integration coverage uses `A`, `a`, `0`, `&`, and space.

Space is intentionally excluded from atlas entries by the metrics-only policy.

## Tests

M8i adds focused tests for:

- deterministic packing,
- sort order,
- UV computation,
- float-copy placement,
- multi-page growth,
- mixed-channel rejection,
- oversize-glyph rejection,
- `.dfpage` export,
- content hashes,
- import/validation failure modes,
- snapshot roundtrip,
- real Typography + MSDF pipeline determinism,
- whitespace exclusion.

## Deferred issues

- no renderer integration,
- no `TextBlock` or gallery consumption,
- no PNG output,
- no metrics-only serialized glyph entry type,
- no cross-platform float-byte stability promise beyond current managed proof scope,
- no GPU or Vulkan dependency.

## M8j plan

M8j can build on this by adding a CPU-side inspection or debug rendering path for packed distance-field pages without changing the current renderer stack or requiring Vulkan.
