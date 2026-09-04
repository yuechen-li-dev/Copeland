# MACHINA-MSDF-REALIZATION-M1 report

## Outcome

Outcome A. The qualified Typography outline now generates a deterministic scalable
field and atlas whose CPU reconstruction matches DirectOutline while the qualified
`MachinaGlyphRun` placement remains unchanged.

## Provenance and pipeline audit

The exact font is `CrimsonText-Regular.ttf`, SHA-256
`48e6c5d5ad1d01599d374ecb817e15890d1feb3b8a3a88e527d44c90389e1f06`, face 0.
Copeland builds the maintained `Machina.Typography.OpenFont` 1.0.0 source project so
the package producer and tests use one implementation. The published nupkg SHA-256
is `145da74eb94184eeeb5e1cbf911f4e2361d42c0d9e45102ed639dc42d1abac6b`;
the source-introduction commit is `a9f9ea5e300d357a600c55400bfb744891c9137a`.
There is no second parser copy in Machina.Fonts.

| Stage | Current source of truth | Form | M1 finding | M1 action |
| --- | --- | --- | --- | --- |
| outline extraction | `TypographyGlyphOutlineSource` | vector | same qualified source | retained |
| normalized contours | Machina line/quadratic/cubic records | vector | exact zero-length edges were not removed | exact control-polygon sanitation |
| winding | MSDF-Sharp `Normalize` + `OrientContours` | vector | deterministic outer/hole orientation | retained |
| edge coloring | MSDF-Sharp simple coloring, seed 0, angle 3 | vector | period can yield a non-finite channel | same-vector monochrome fallback |
| distance/encoding | MSDF-Sharp signed distance, range 4 | vector field | projection translation used wrong coordinate space | corrected transform |
| field bounds | inverse vector-to-field projection | metadata | inverse matched the old wrong transform | corrected with transform |
| atlas | deterministic shelf packer | field storage | padding/order were already correct | retained |
| UV sampling | `DistanceFieldSampling` | MSDF | texel-center law already correct | retained and tested |
| reconstruction | median RGB to alpha at 0.5 | mask | proof classified RGB smoothing halo as geometry | compare reconstructed alpha at 0.5 |

No production MSDF code consumes a bitmap, mask, DirectOutline image, Avalonia image,
or outline rasterizer. The production path is
`Typography glyph -> GlyphOutline curves -> Msdfgen.Shape -> field -> atlas`.

## Root causes and repairs

MSDF-Sharp defines projection as `field = scale * (outline + translation)`. Machina
calculated a pixel-space centering offset and supplied it as the pre-scale translation,
thereby multiplying it by the glyph-specific field scale. That displaced every outline
inside its field and especially damaged baseline-relative lower bounds. M1 divides the
pixel offset by scale before constructing the projection and uses
`outline = field / scale - translation` for plane bounds.

The period is a legitimate eight-quadratic closed contour. After the transform repair,
MSDF-Sharp 1.0.2 can still produce a non-finite multi-channel component for this smooth
geometric class. If and only if multi-channel output is non-finite, Machina asks the
same library for a single signed distance from the same normalized vector shape and
replicates it into RGB. This is a valid monochrome MSDF representation, not a raster
fallback and not a period special case. Non-finite fallback output remains a focused
error with the first channel index.

Contour sanitation removes only lines whose endpoints are identical, quadratics whose
three points are identical, and cubics whose four points are identical. It does not
remove small valid features. Original quadratic and cubic curves remain curves; there
is no resolution-dependent production flattening.

The second apparent defect was diagnostic. With a transparent background, any nonzero
antialiasing coverage composites to white RGB with fractional alpha. The old proof
classified RGB difference as full ink, turning the smoothing halo into false geometry.
M1 compares alpha coverage at the signed-distance boundary (`0.5`). This correction
changes proof measurement only, not production placement or field data.

The historical jagged-edge bitmap hypothesis is **REJECTED**: tracing found zero
raster-derived production fields. The observed defects came from the vector projection
and RGB-vs-alpha proof classification.

## Geometry, storage, and reconstruction laws

- Semantic glyph plane bounds remain the qualified outline bounds on the glyph run.
- Field plane bounds are the inverse projection of `[0,width] x [0,height]`, including
  the range border. Atlas storage rectangles are a third, separate coordinate space.
- Atlas padding is storage-only and never changes draw bounds, origin, baseline, or
  advance.
- Field dimensions are the next power of two at or above `max(32, requested em size)`;
  16/24 use 32, 32 uses 32, 48/64 use 64, and 96/128 use 128. Larger output is never a
  stretch of one fixed 32x32 field.
- The vector-to-field transform is uniform scale plus pre-scale vector translation;
  Typography Y-up becomes baseline-relative Y-down only in `GlyphFieldPlacement`.
- UVs name storage rectangle edges. Sampling maps each output pixel center through the
  UV rectangle to an atlas texel center.
- RGB channels encode signed distance with edge at `0.5` and explicit pixel range 4.
  CPU reconstruction takes median RGB and applies the existing scale-aware smoothing.
- Edge coloring remains deterministic simple coloring with seed 0. M1 found no reason
  to redesign it. The monochrome fallback handles only non-finite geometric output.
- Atlas order is height descending, width descending, then face, em size, weight,
  slant, and codepoint. Page pixels, entries, outline data, and fields carry SHA-256
  fingerprints in compact proof.

## Qualification results

The runner covers 224 cases across 16, 24, 32, 48, 64, 96, and 128 px. It includes
the required sentences, single glyphs, punctuation, descenders, Q, and held-out
`Typography`, `0123456789`, and `Hello, world.`. All cases generate finite fields,
pack, reconstruct, and preserve the same shared placement instance with maximum
semantic delta 0.

Across all cases, Direct/MSDF IoU is minimum 0.500, p50 0.962, p95 0.998, maximum
1.000. Median p50 edge distance is 0 px; p95 edge distance has median 1 px and maximum
1.795 px; maximum observed edge distance is 2.828 px. Centroid-distance delta is p50
0.215 px, p95 1.338 px, maximum 2.742 px. Ink-area ratio ranges from 0.960 to 2.000;
the maximum is a one-pixel-vs-two-pixel tiny-glyph quantization case. The low IoU
minimum is the two-pixel
16 px `p`, where one differing boundary pixel materially changes IoU; it is not a
bounds or placement failure. Metrics converge with size.

Period succeeds at every required size. Its IoU is 1.000, 0.833, 1.000, 1.000, 0.950,
0.962, and 1.000 from 16 through 128 px; p95 edge distance never exceeds 1 px. Q and
all descenders generate finite output. The 42 descender single-glyph cases have minimum
IoU 0.500, median IoU 0.969, and maximum p95 edge distance 1.531 px. The fox sentence,
`Agjpqy`, and all held-outs pass at every size. Per-glyph bounds, ink-area ratio,
centroid delta, IoU, edge distances, hashes, field transform, atlas rectangle, and UV
are recorded in `glyphs.json`.

The most useful diagnostic was the three-stage numeric localization: qualified outline
bounds, field-space projection/inverse projection, then Direct/MSDF reconstruction.
It showed nonzero field translation error while glyph-run delta remained zero. Existing
three-way overlays, edge-difference images, glyph-bound guides, and per-channel fields
remain local temporary diagnostics; no large raster is committed.

## Readiness and disposition

MSDF is production-ready as a renderer-neutral scalable realization. DirectOutline
remains production-ready for static/reference raster text. No arbitrary offsets,
glyph-specific branches, native font library, `MSDF-Sharp.Extensions`, SixLabors
package, shader change, upload redesign, or Aurelian renderer change was added.

The clean production single-field entry is `GlyphGenerationPipeline.GenerateAsync`,
which begins at the configured vector outline source. `GeneratedFieldAtlasPacker.Pack`
builds atlas pages from generated fields and has no bitmap-source parameter. Atlas
entries contain key, page/storage rectangle, UVs, field placement, metrics, and range;
semantic placement remains on `MachinaGlyphRun`.

`Machina.UI.Slow.slnx` still contains the diagnostic and tooling integration projects.
Their browser availability, export determinism, and historical before/after contracts
are not all duplicated in normal tests, so deletion would violate the unique-contract
gate. They are retained as historical/optional manual tooling and do not gate normal
MSDF production. Browser comparison is OPTIONAL MANUAL TOOL; Avalonia remains the
outline/layout oracle only and is absent from production Machina.Fonts.

The exact next native-text milestone is:

```text
MACHINA-AURELIAN-MSDF-TEXT-M1
MachinaGlyphRun + qualified MSDF atlas metadata
-> Aurelian ordered glyph quads
-> existing production MSDF shader
```

It must not introduce shaping, pen progression, baseline calculation, or atlas-owned
layout in Aurelian.

## Evidence and validation

Compact generated evidence is under `artifacts/machina-msdf-realization-m1/`:

```text
proof.json
glyphs.json
atlas.json
realization.json
manifest.json
```

The proof runner reports average per-case timings of 14.3 ms for Avalonia secondary
reference setup, 1687.2 ms for DirectOutline, 164.1 ms for MSDF generation/packing and
render, 156.8 ms for MSDF generation/atlas work, and 7.3 ms for CPU reconstruction.
These are diagnostic timings, not benchmarks.

Validation:

- `dotnet test Machina.UI.slnx -m:1`: 708 passed.
- `dotnet test Machina.UI.Slow.slnx -m:1`: 337 passed.
- `dotnet test Aurelian.slnx -m:1`: 636 passed.
- `dotnet test JointTaskForce.slnx -m:1`: 3,262 passed.
- outline M1 runner: Outcome A.
- MSDF M1 runner: Outcome A, 224/224.
- focused deterministic field/atlas/hash tests: 12 passed.
- five-file artifact schema/readability/finite-source validation: passed.
- `git diff --check`: passed.

The slow-lane strict-improvement assertions were migrated to qualification/no-regression
because the corrected universal projection makes fixed and scalable fields equally
aligned; the unique scalable-field contract remains covered.
