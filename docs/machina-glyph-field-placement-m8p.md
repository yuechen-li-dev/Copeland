# Machina Glyph Field Placement M8p

## Purpose

M8p fixes the standalone `Machina.Fonts` proof-path glyph placement contract by storing explicit generated field plane bounds instead of inferring draw placement from fixed atlas tile sizes and recomputed centered padding.

This milestone stays inside `Machina.Fonts`, local proof tooling, and sample/gallery proof exports.
It does not add `TextBlock`, `Standard.Text`, Vulkan, Aurelian, or production renderer integration.

## Root cause from M8o

M8o showed that contiguous strings overlapped badly even though whitespace advances and pair adjustments were present. The real failure was that the proof renderer treated the generated field tile like the glyph draw box, while atlas metadata did not preserve the actual field plane relative to glyph origin/baseline.

## Metric box vs field box vs atlas box

- metric box: typographic bounds and bearings in glyph/output space
- field box: the full distance-field plane captured during generation, including halo/pixel range
- atlas box: the packed bitmap rect inside a `.dfpage`

M8p makes the field box explicit and keeps atlas packing focused on bitmap storage only.

## Placement metadata contract

`GlyphFieldPlacement` stores:

- `PlaneLeft`
- `PlaneTop`
- `PlaneRight`
- `PlaneBottom`
- `PixelRange`
- `ProjectionScale`

`GeneratedGlyphDistanceField` owns placement metadata at generation time, and `GlyphAtlasEntry` preserves it after packing/import.

## Generation-time computation

`MsdfSharpDistanceFieldGenerator` now computes placement from the exact projection used to rasterize the field:

1. compute the fit scale for the drawable area `(field - 2 * pixelRange)`
2. build the actual projection scale/translation
3. invert bitmap corners back into glyph space
4. convert those bounds into proof render-space plane bounds relative to baseline

This means the stored plane bounds match the generated field exactly instead of approximating leftover centered padding later.

## Atlas/TOML persistence

`GeneratedFieldAtlasPacker` still packs the raw field bitmap dimensions and now copies `GeneratedGlyphDistanceField.Placement` into each `GlyphAtlasEntry`.

`.font-atlas.toml` glyph entries now persist:

- `plane_left`
- `plane_top`
- `plane_right`
- `plane_bottom`
- `pixel_range`
- `projection_scale`

Loader, writer, validator, export/import tests, and artifact roundtrip coverage were updated so placement survives `.font-atlas.toml` + `.dfpage` roundtrips.

## Rendering usage

`CpuDistanceFieldTextRenderer` no longer recomputes centered fit padding from metrics plus a fixed field tile.

It now derives the glyph draw quad from stored placement:

```text
drawX = penX + placement.PlaneLeft
drawY = baselineY + placement.PlaneTop
drawWidth = placement.PlaneRight - placement.PlaneLeft
drawHeight = placement.PlaneBottom - placement.PlaneTop
```

Kerning/pair adjustment still applies before glyph placement, whitespace remains metrics-only, and no string-specific tracking hack was introduced.

## Tests

M8p adds or updates tests for:

- generated-field placement presence, determinism, and projection-corner agreement
- atlas packing placement preservation
- TOML writer/loader/roundtrip placement persistence
- CPU glyph/text renderer use of placement bounds rather than tile size
- contiguous glyph spacing without fixed-tile overlap
- oracle report inclusion of placement fields
- proof pipeline confirmation that draw widths are no longer tied to fixed tile sizes

## Reference oracle results

`.\tools\Export-MachinaFontReferenceComparison.ps1 -OutputDir artifacts\m8p` now produces:

- per-glyph plane bounds in `glyph-placement-report.txt/json`
- `.font-atlas.toml` glyph placement fields
- side-by-side comparisons whose contiguous strings no longer show the heavy tile-width overlap seen in M8o

Observed local result:

- `Machina`, `Hello Machina`, `AV To Ta Wa Yo`, and `A A` no longer exhibit the prior oversized fixed-tile collision pattern
- proof glyph field sizes now vary with stored plane bounds instead of behaving like universal `32x32` draw quads
- remaining visual differences are smaller proof-path quality mismatches rather than the original placement-contract failure

## Deferred issues

- no UI or `TextBlock` integration
- no production renderer or shader integration
- no shaping, fallback, ligatures, bidi, or multiline layout work
- no browser-oracle pixel diff gate
- no change to the current bitmap text renderer outside proof/sample tooling

## Next milestone

The next milestone should evaluate remaining quality differences after the placement contract fix, not re-open guessed spacing compensation.

## M8q follow-up

M8q extends the oracle/reporting path to prove the vertical contract numerically.

- browser `TextMetrics` are now exported to `artifacts/m8q/browser-text-metrics.json`
- glyph placement reports now include run-level baseline/plane/ink metrics and per-glyph `penX`, `baselineY`, `drawWidth`, and `drawHeight`
- current M8q evidence shows the proof renderer is already using the correct baseline-relative plane contract
- new tests prove `BearingY` is not double-applied once `GlyphFieldPlacement` is present

The remaining visual difference after M8q is a small lower-edge ink extent mismatch, not a `PlaneTop`/`PlaneBottom` sign bug.

## M8q.1 follow-up

M8q.1 does not change `GlyphFieldPlacement` semantics.

Instead it fixes the final proof raster placement step:

- plane bounds still define the baseline-relative glyph plane
- the output tile height is rounded first
- baseline position inside that rounded tile is then computed from the plane fraction
- `drawY` is derived from that one baseline invariant

So M8q.1 is a proof-renderer raster rounding fix, not an atlas or placement-contract redesign.
