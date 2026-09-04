# MACHINA-TEXT-CONFORMANCE-M0 report

## Result

Outcome B: token layout converged through the explicit shared token-anchor contract,
while MSDF retains a bounded realization defect and remains experimental. Avalonia,
DirectOutline, and MSDF use one exact font and DirectOutline/MSDF report identical
semantic placements. Production does not depend on Avalonia.

The authoritative numeric result is
`artifacts/machina-text-conformance-m0/proof.json`; this report records architectural
decisions and disposition rather than duplicating every case.

The final matrix contains 84 cases: the seven canonical strings, five targeted
isolation strings, and three held-out strings at 16/24/32/48/64, plus nine
single-glyph isolation cases at 32px. Eighty-three complete end to end; the isolated
period records `MSDF_FIELD`.
Token-anchor delta, internal relative-origin delta, baseline delta, and glyph
plane-width/height delta are all p50/p95/max `0/0/0 px`. Direct and MSDF semantic
placement maximum delta is `0 px`. The fox stress line keeps all nine word anchors at
`0 px` delta through the last token, so error does not accumulate by token index.
All 83 successful MSDF cases preserve reference identity for the Direct-created
`DistanceFieldTextLayoutResult`; the failed period exits during field generation
before realization can consume a placement.

Direct-vs-MSDF raster evidence does not meet production acceptance. Median mask IoU
is approximately `0.352`; punctuation can reach `0.000`, p95 edge distance reaches
approximately `10.38 px`, and maximum edge distance reaches approximately `18.60 px`.
The selected production gate is minimum IoU `0.30` and maximum p95 edge distance
`4 px`, evaluated separately from placement. Average measured times per case are
approximately `14.8 ms` for Avalonia layout/raster, `0.13 ms` for Machina layout,
`1.23 s` for the supersampled DirectOutline raster, `154.6 ms` for MSDF
generation/packing, and `6.52 ms` for MSDF render/export. These are diagnostic
timings, not optimized production benchmarks.

## Historical audit

| Existing subsystem | Current role | Keep? | Repair? | Superseded? |
| --- | --- | --- | --- | --- |
| `Machina.Fonts` records/generation | Font, outline, metrics, atlas, MSDF ownership | Yes | Shared glyph-run metadata added | No |
| `Machina.Fonts.ReferenceRendering` | Direct and CPU-MSDF proof realizers | Yes | Both expose one shared placement result | Direct-as-external-oracle wording only |
| `Machina.Fonts.Tooling` | Optional visual diagnostics and large exports | Yes | None in M0 | Browser-primary qualification |
| `TypographyGlyphOutlineSource` | Exact-font outline, metric, and pair adjustment source | Yes | None | No |
| `DirectOutlineTextBoxLayouter` | Explicit-newline text-in-rect proof | Yes | Current glyph run is the lower placement seam | No |
| `DirectOutlineStaticTextRenderer` and bridge | Internal outline/raster truth | Yes | Exposes the shared layout and accepts token anchors | External layout authority claim |
| `DistanceFieldTextLayout` | Shared Direct/MSDF pen placement | Yes | Token IDs, spans, planes, anchor reset, kerning-aware token widths | Independent renderer layout |
| `GlyphFieldPlacement` and atlas packing | MSDF plane reconstruction and storage | Yes | Reconfirmed | Atlas-owned placement |
| M8/M9 browser diagnostics | Historical/optional visual comparison | Historical only | None | Canonical qualification role |
| M9f scalable field and texel-center fixes | Current MSDF law | Yes | Reconfirmed by normal tests | No |
| Gallery font proofs | Opt-in integration evidence | Yes | None | Canonical text conformance role |

## Oracle and comparison policy

The isolated `Machina.Fonts.AvaloniaOracle` project uses Avalonia 11.3.1 with the
Skia/HarfBuzz backend. It embeds the existing Crimson Text fixture and validates the
requested file against the embedded bytes by SHA-256 before layout. Public
`TextLayout`, `TextLine`, `ShapedTextRun`, `GlyphRun`, and `GlyphInfo` data provide
line metrics, glyph IDs, clusters, origins, advances, offsets, and glyph metrics.
`RenderTargetBitmap` provides the reference raster.

The coordinate law is 96-DPI device-independent pixels, X right, Y down, baseline
measured from the content top, and glyph plane bounds relative to that baseline.
No intermediate integer rounding is performed. M0 disables `liga` and `clig` in the
reference so both Latin paths use the same feature policy.

Tokenization is stable word/punctuation/whitespace grouping. The first visible shaped
glyph anchors each non-whitespace token. Spaces contribute a measured gap but have no
anchor. Internal glyph deltas are token-relative. The long fox sentence exports every
anchor by token index, making a drift cascade visible independently of line bounds.

## Defects and fixes

1. Avalonia's default headless drawing implementation exposed stub `Default` metrics.
   The oracle now explicitly uses Skia and disables headless stub drawing.
2. Direct and MSDF placement had no renderer-neutral token/source representation.
   `MachinaGlyphRun` now carries lines, tokens, glyphs, baselines, advances, spans,
   token IDs, and plane bounds. Direct exposes its immutable layout result and MSDF
   consumes that same instance rather than recomputing placement.
3. Whole-line advancement allowed prior-token error to contaminate later placement.
   `DistanceFieldTextLayout` now accepts absolute token origins and resets the local
   pen/pair predecessor at each supplied anchor.
4. Token width initially summed raw glyph advances and omitted pair adjustment.
   It now derives width from first origin through final glyph advance.
5. Default Avalonia ligatures made codepoint-local comparison ambiguous. `liga` and
   `clig` are disabled consistently for the bounded M0 Latin policy.
6. Held-out capital `Q` produced non-finite values in MSDF-Sharp generation. It is a
   concrete diagnostic discovery. The required isolated period reproduces the same
   non-finite-field class and is retained as the machine-readable failing case; no
   threshold or fixture-specific offset was added to conceal either result.
7. The initial conformance runner omitted the qualified Typography/MSDF Y inversion,
   making every reconstructed glyph visibly upside down. The runner now supplies
   `FlipY: true`; the regenerated raw and overlay images are upright.
8. The CPU text renderer rounded every glyph draw rectangle before reconstruction,
   discarding fractional origins and the `30.375`/`60.75` reference baselines. It now
   samples each destination pixel against the unrounded semantic plane and quantizes
   only at the final pixel. A normal test preserves this subpixel handoff.

The 32px and 64px diagnostic bundles use the Avalonia PNG, DirectOutline and MSDF PPM masks,
Avalonia/Direct and Direct/MSDF two-channel overlays, a three-channel overlay
(Avalonia red, Direct green, MSDF blue), an absolute edge-difference view, white
baseline, yellow token-anchor guides, cyan plane boxes, and magenta glyph origins.
The overlays and decomposed numeric records were useful; browser screenshots were
not used.

No arbitrary offsets, scale nudges, or screenshot-derived constants were added.
Tolerances scale from em size: anchor `em/64`, internal origin `0.04em`, and token
width `0.06em`.

## Slow-lane disposition

| Slow-lane item | Unique coverage? | Replacement | Disposition |
| --- | --- | --- | --- |
| `Machina.Fonts.Diagnostics.Tests` | Historical browser/reference workflows | Normal Avalonia oracle, token/glyph JSON, existing normal shape tests | Retain for history while unresolved exports remain |
| `Machina.Fonts.Tooling.Tests` | Large layered exports and M9f before/after images | Normal unit tests cover tokenizer, anchors, shared placement, atlas/UV/scale | Retain only as optional export regression lane |
| `tools/font-reference` browser harness | Optional browser comparison | Avalonia is canonical | Historical/secondary; not required by normal conformance |
| M8/M9 PNG bundles and reports | Audit trail | Compact M0 JSON plus local raster bundle | Historical; not current authority |

The slow solution is not deleted because its large export workflows still have unique
historical coverage. Standard M0 conformance has no browser or Playwright dependency.

## Production disposition

DirectOutline is qualified as Machina's internal static outline/raster truth and as a
consumer of shared placement. MSDF is not production-ready: semantic placement is
exactly shared, but mask IoU/edge-distance evidence remains below the selected
realization threshold on several sizes/descenders, and capital `Q` can generate
non-finite field values. The Aurelian quad renderer is structurally ready to accept a
future `MachinaGlyphRun` adapter; native handoff is deferred until MSDF realization is
green and a Machina-owned production token-anchor provider is selected.

The exact next text milestone is `MACHINA-MSDF-REALIZATION-M1`: isolate and repair
the capital-`Q` non-finite field plus low-IoU descender reconstruction, without
changing the shared token placement law or adding Avalonia to production.

## Validation and artifact budget

The final solution matrix is green:

- `dotnet test Machina.UI.slnx -m:1`: 685 tests.
- `dotnet test Machina.UI.Slow.slnx -m:1`: 316 tests.
- `dotnet test Aurelian.slnx -m:1`: 636 tests.
- `dotnet test JointTaskForce.slnx -m:1`: 3,240 tests.
- `dotnet run --project tools/Machina.TextConformance/Machina.TextConformance.csproj`:
  84 cases executed and all five JSON artifacts written; exit 1 intentionally
  reports Outcome B because `32-u002e` fails `MSDF_FIELD`.
- `git diff --check`: passed.

The repository evidence bundle is five JSON files totaling approximately 1.25 MB. Full
Avalonia rasters, masks, overlays, edge difference, guides, and bounds views remain
in the manifest-recorded local temp directory. Avalonia-vs-Direct pixel overlap is
kept diagnostic rather than gated because backend antialiasing differs; their
decomposed anchor, baseline, internal-origin, and plane-size deltas are all zero.
MSDF-vs-Avalonia is likewise secondary and visually represented by the three-way
overlay; the gated realization metric is Direct-vs-MSDF IoU and edge distance.

The tracked `git diff --stat` is `14 files changed, 335 insertions(+), 11 deletions(-)`.
There are also 13 new untracked milestone files, including the five required JSON
artifacts, the oracle/runner, tests, shared glyph run, and current documentation.
