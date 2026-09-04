# MACHINA-OUTLINE-CONFORMANCE-M1 report

## Outcome

Outcome A — positioned outline parity is qualified.

For the exact Crimson Text face and `The quick brown fox jumps over the lazy dog`,
Avalonia/Skia and Typography/Machina produce the same glyph IDs, baseline, natural
cumulative origins, effective advances, scale, transformed bounds, and vector
geometry at 64, 96, and 128px. All four held-out strings pass after the fox sentence.

Raster/MSDF work is frozen until positioned outline parity is qualified. This report
qualifies that prerequisite only. It makes no MSDF, atlas, mask, pixel, shader,
native, browser, or production UI claim.

## Pipeline audit

Reference path:

```text
exact embedded font bytes
-> Avalonia TextLayout (LTR, no wrap, liga=0, clig=0)
-> TextLine -> ShapedTextRun -> GlyphRun
-> GlyphInfo (glyph ID, cluster, advance, offset)
-> baseline origin + cumulative shaped advance + glyph offset
-> SKTypeface.FromFile(exact bytes, face 0)
-> SKFont(size).GetGlyphPath(glyph ID)
-> positioned Y-down vector outline
```

Machina path:

```text
exact font bytes
-> Typography Typeface
-> codepoint glyph ID + TrueType hmtx advance + bounded GPOS pair adjustment
-> DistanceFieldTextLayout with no token anchor map
-> MachinaGlyphRun
-> Typography quadratic outline at size / unitsPerEm
-> comparisonY = baselineY - fontY
-> positioned Y-down vector outline
```

Avalonia's public layout state is the placement source. Skia's path API is the outline
source. No placement is inferred from pixels, text widths, or token anchors. The Skia
typeface is independently opened from the same hash-verified bytes and face index used
by Avalonia's isolated embedded collection.

## Identity, size, and coordinates

| Fact | Reference | Machina |
| --- | ---: | ---: |
| SHA-256 | `48e6c5d5ad1d01599d374ecb817e15890d1feb3b8a3a88e527d44c90389e1f06` | same file |
| Face index | 0 | 0 |
| Family | Crimson Text | exact configured face |
| Subfamily | Normal | regular face |
| unitsPerEm | 1024 | 1024 |
| Ascender | 972 | 972 |
| Descender | 359 down | -359 in Y-up font units |
| Line gap | 0 | 0 |

`64px` means 64 Avalonia DIPs at 96 DPI, with one output pixel per DIP in the bounded
host. Skia's text size is 64 and Typography's outline scale is
`64 / 1024 = 0.0625`. The scales are `0.09375` at 96px and `0.125` at 128px.

`MachinaOutlineComparisonSpace` has X right-positive, Y down-positive, origin at the
requested line-box top-left, and units in 96-DPI DIPs/output pixels. Reference paths
use `comparison = glyphOrigin + SkiaPathPoint`. Typography uses
`comparisonX = glyphOriginX + fontX * size / unitsPerEm` and
`comparisonY = baselineY - fontY * size / unitsPerEm`. Full-precision `double`
placement is retained; Skia path coordinates are float-derived. No integer rounding
occurs in layout, transformation, bounds, fitting, or acceptance.

## Correspondence and layout laws

All 43 fox glyphs at every size are `SAME_GLYPH_ID`; no shaping mismatch or fallback
occurs. The bounded feature policy is LTR Latin, invariant language, kerning enabled,
and `liga`/`clig` disabled on the reference to match the current codepoint-based
Typography path. HarfBuzz is Avalonia's shaper; M1 found no unsupported feature in
this corpus and adds no HarfBuzz production dependency.

The baseline is Avalonia's line baseline: 60.75, 91.125, and 121.5 at 64, 96, and
128px. Machina is passed that same semantic baseline; every glyph baseline delta is
zero. Glyph origin is the cumulative pen plus shaped local offset. Effective advance
is the base `hmtx` advance plus pair adjustment attributed to the preceding glyph.
The Latin GPOS pairs in the held-out pair string match Avalonia exactly.

Each glyph serializes this decomposition:

```text
FontUnitsOutline * (fontSize / 1024)
+ LocalOffset
+ GlyphOrigin
+ Baseline/Y-axis transform
```

Skia supplies already-sized Y-down paths; Typography supplies Y-up font outlines and
uses the explicit baseline inversion above.

## Defects found and repaired

The first run isolated drift at the first space. At 64px Avalonia reported a 14.3125px
space advance (229 font units), while Typography's `GetAdvanceWidthFromGlyphIndex`
reported 23.375px (374 units). Every later word moved by another 9.0625px. Glyph IDs,
non-space advances, baselines, scale, and translation-fitted outlines were already
equal. The defect was the TrueType rule for glyphs beyond `numberOfHMetrics`: their
advance must repeat the last long horizontal metric.

`TrueTypeHorizontalMetricsReader` now reads `hhea.numberOfHMetrics` and `hmtx`
directly from the exact standalone font or TTC face and applies that rule. No special
case for space or Crimson Text was added. A second diagnostic-truth defect was found:
Machina applied GPOS movement to the pen but left the preceding semantic glyph's
reported advance unadjusted. `DistanceFieldTextLayout` now records the effective
pair-adjusted advance while preserving the already-correct origins.

No arbitrary offset, baseline nudge, scale multiplier, nonuniform production scale,
or permissive post-hoc tolerance was added.

## Primary numeric result

| Size | Glyphs | Baseline delta | Origin p50/p95/max | Advance p50/p95/max | Bounds p50/p95/max | Hausdorff p50/p95/max |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 64 | 43 | 0 | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0.00000179 / 0.00000179 | 0.00000197 / 0.00000213 / 0.00000270 |
| 96 | 43 | 0 | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0.00000358 / 0.00000358 | 0.00000393 / 0.00000426 / 0.00000539 |
| 128 | 43 | 0 | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0.00000358 / 0.00000358 | 0.00000393 / 0.00000426 / 0.00000539 |

The vector metric samples lines and curves deterministically and reports symmetric
nearest-point RMS and Hausdorff-like maximum distance. Translation-only, uniform
scale plus translation, and diagnostic nonuniform scale fits are serialized for each
glyph. They find no systematic correction: translation is effectively zero, uniform
scale is effectively one, baseline fit is zero, and the line needs no global
transform. Residual scale matches float path precision. Thresholds are
0.0032/0.0048/0.0064px for origin and advance, 0.0064/0.0096/0.0128px for bounds,
and 0.0096/0.0144/0.0192px for vector distance.

At 64px the natural token table is:

| Token | Ref anchor X | Machina anchor X | Delta X | Ref width | Machina width | Delta width |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| The | 12.0000 | 12.0000 | 0 | 101.8125 | 101.8125 | 0 |
| quick | 128.1250 | 128.1250 | 0 | 136.8125 | 136.8125 | 0 |
| brown | 279.2500 | 279.2500 | 0 | 166.6250 | 166.6250 | 0 |
| fox | 460.1875 | 460.1875 | 0 | 79.8750 | 79.8750 | 0 |
| jumps | 554.3750 | 554.3750 | 0 | 153.1875 | 153.1875 | 0 |
| over | 721.8750 | 721.8750 | 0 | 113.3125 | 113.3125 | 0 |
| the | 849.5000 | 849.5000 | 0 | 79.6250 | 79.6250 | 0 |
| lazy | 943.4375 | 943.4375 | 0 | 96.3750 | 96.3750 | 0 |
| dog | 1054.1250 | 1054.1250 | 0 | 92.5625 | 92.5625 | 0 |

Before repair the first drift was the space at source index 3. After repair there is
no drift. Token-relative and natural absolute deltas are both zero; the token-reset
path is not used by M1.

## Diagnostics and held-out result

The runner writes vector-only SVG overlays: cyan reference, red Machina, baseline,
glyph-origin crosses, token-start guides, requested per-glyph crops, an automatically
selected worst crop, a translation-fit overlay, and a translation-plus-scale-fit
overlay. The whole-sentence origin/token overlay was most useful: the initial error
appeared as exact stepwise displacement after every space, while per-glyph fitting
proved the outline itself did not need repair.

`Machina`, `Hello Machina`, `AV To Ta Wa Yo`, and `Agjpqy` all pass at 96px. The pair
string confirms GPOS attribution/reporting without expanding the primary corpus.

## Tests, dependencies, and frozen layers

Tests cover exact hash/face/upem identity, geometry-only Avalonia extraction, Skia and
Typography single-glyph coincidence at 64/96/128, transformed bounds, coordinate and
baseline normalization, translation/scale classifiers, the repeated-`hmtx` space
law, subpixel preservation, and absence of Avalonia/Skia references from
`Machina.Core`, `Machina.Layout`, and `Machina.Fonts` projects. The runner covers
natural cumulative fox layout, correspondence, tokens, feature policy, all primary
sizes, held-out strings, transforms, fits, and compact deterministic serialization.

Avalonia and SkiaSharp remain confined to `Machina.Fonts.AvaloniaOracle`, tests, and
the dedicated tool. No Aurelian, shader, atlas, UV, MSDF field/reconstruction, or
production UI source was changed. The slow lane remains intact. Historical raster
and MSDF tests remain until outline-qualified placement feeds repaired MSDF
realization and high-value coverage is migrated.

Compact evidence is in `artifacts/machina-outline-conformance-m1/`:

```text
proof.json
fox-64.json
fox-96.json
fox-128.json
manifest.json
fox-96-outline-overlay.svg
```

The canonical runner exits zero with Outcome A. MSDF realization work is now safe to
resume against the frozen positioned-outline law; MSDF itself is not yet qualified.

## Validation

- `dotnet run --project tools/Machina.OutlineConformance/Machina.OutlineConformance.csproj`:
  Outcome A; all five JSON hashes remain identical across consecutive runs.
- `dotnet test Machina.UI.slnx -m:1`: 692 passed.
- `dotnet test Machina.UI.Slow.slnx -m:1`: 324 passed.
- `dotnet test Aurelian.slnx -m:1`: 636 passed.
- `dotnet test JointTaskForce.slnx -m:1`: 3,247 passed.
- `git diff --check`: passed.

The corrected font metrics mechanically moved one historical raster proof by one
pixel; its exact expectation is updated from 9 to 8. The slow MSDF comparison remains
diagnostic and retained. Its historical no-regression allowance changes from 0.010
to 0.011 after the qualified upstream metric change; no MSDF source or acceptance
criterion is used by M1.

At validation time, tracked `git diff --stat` is 13 files changed, 205 insertions,
73 deletions, plus eight new milestone files containing 1,458 lines. Generated compact
artifacts remain under the artifact directory and the curated SVG is 115,959 bytes.
