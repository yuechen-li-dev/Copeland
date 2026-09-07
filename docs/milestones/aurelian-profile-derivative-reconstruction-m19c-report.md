# Aurelian Profile derivative reconstruction M19C

## 1. Outcome

**Outcome A.** Profile MSDF reconstruction now derives coverage width from the
projected field. The Mossward worker no longer carries the broad small-field
ramp, controls remain stable, and the real Mossward and TinyFarm native paths
qualify.

## 2. M19B baseline and remaining blur

M19B excluded snapping, sampler selection, mipmapping, UV precision, and alpha
blending. Neutral Profile threshold `0.5` raised worker IoU from `0.825` to
`0.955`, but its edge transition remained `2.638 px` here versus `0.903 px` for
the direct CPU contour. The remaining seam was the fixed MSDF reconstruction
ramp under minification.

| Path | Current reconstruction | Small-size behavior | M19C action |
| --- | --- | --- | --- |
| CPU Profile | direct contour AA | 0.903 px worker edge | reference |
| glyph | median, glyph threshold, fixed ramp | existing qualified behavior | preserve |
| M19B Profile | median, 0.5 threshold, fixed ramp | correct silhouette, broad edge | retain as diagnostic baseline |
| M19C Profile | median, 0.5 threshold, derivative ramp | 0.975 px worker edge | production Profile owner |

## 3. Derivative support audit and compiler additions

Visual TypeScript had no derivative intrinsic. M19C adds only `Fwidth(f32)`.
It is accepted only in a pixel entry-point call graph; wrong types produce
`COPE-GPU-DERIVATIVE-0001` and vertex use produces
`COPE-GPU-DERIVATIVE-0002`. VD-MIR records the typed intrinsic, HLSL lowering
emits `fwidth`, and DXC/SPIR-V validation is covered by compiler and Profile
shader tests. Scalar support is sufficient for the reconstructed distance, so
no unused vector derivative surface was added.

## 4. Field encoding and reconstruction law

The field is normalized RGB MSDF, reconstructed with median channel logic.
The contour is `0.5`; `sd = median(rgb) - 0.5`. `PixelRange` is the generation
spread in field texels. `fwidth(median(rgb))` is encoded distance per screen
pixel, so M19C uses it as the complete smooth cubic ramp centered on `0.5`.
No extra pixel-range factor is correct here because the derivative is taken
after texture sampling and projection.

## 5. Owner lane

`ProfileMsdf.v.ts` and `Native2DPipelineKind.ProfileMsdf` own Profile coverage.
`MsdfText.v.ts` is restored to its existing glyph law. Mossward and TinyFarm
use a Profile renderer/cache and a separate text renderer/atlas cache while
sharing the existing MSDF submission, atlas, filtering, and blend mechanisms.
This prevents glyph policy leakage without creating a second asset pipeline.

## 6. Objective edge proof

| Asset | CPU width | M19B neutral width | M19C width | Neutral IoU | M19C IoU |
| --- | ---: | ---: | ---: | ---: | ---: |
| worker | 0.903 | 2.638 | 0.975 | 0.955 | 0.968 |
| tree | 0.924 | 1.176 | 0.961 | 0.993 | 0.994 |
| HQ building | 1.038 | 1.325 | 1.065 | 0.992 | 0.995 |
| synthetic square | 1.011 | n/a | 1.284 | n/a | 0.998 |

The worker transition gap falls from `1.735 px` to `0.072 px` without threshold
clipping. Dark/light mattes and amplified differences show no halo. Straight
alpha still feeds the established SrcAlpha/OneMinusSrcAlpha blend path.

## 7. Stress results

Worker scales `0.5, 0.75, 1, 1.5, 2, 4` retain IoU from `0.95` to `1.00` and
near-one-pixel derivative transitions. Sixteen X/Y subpixel combinations from
`0` through `0.75 px` retain IoU `0.961-0.984` and transition width
`0.912-1.070 px`; the fix has no snap dependency. Fields of `64, 96, 128, 192,
256 px` retain IoU `0.97-0.98` and near-one-pixel transitions. Smaller fields
look promising for future atlas pressure, but production sizes are unchanged.

## 8. Pixel range, median, and blend audit

All tested Profile fields use the existing generation spread (`12` field
pixels in the Mossward assets). Derivative reconstruction alone resolves the
blur, so generation spread was not changed. Profiles and text both use median
RGB and straight coverage alpha; their difference is threshold/ramp policy,
not channel interpretation or compositing.

## 9. Glyph non-regression

An initial shared-shader experiment changed the 16 px glyph result despite a
disabled material branch, so it was rejected. The final glyph source contains
no derivative instruction and retains its original three-field material and
fixed coverage law. Existing committed glyph parity remains `0.938` IoU for
16 px “Hello Machina” and `0.856` minimum across its suite. The standalone
glyph runner currently exposes a pre-existing fresh-run CPU/reference drift
(`0.683` for the first 16 px case); no final M19C code is present in that shader,
and its deterministic shader test passes. This is recorded rather than hidden.

## 10. Diagnostic tool upgrade

The M19B tool now emits four-way reconstruction comparisons, individual
before/after/diff images, dark/light mattes, source atlases, a synthetic
control, field/scale/subpixel stress JSON, performance/cache facts, and the
real-consumer proof frames. Its README documents invocation and interpretation.

## 11. Native consumers

Mossward `--launch-smoke` passed four Vulkan frames with its inspector verified;
the frame preserves terrain/fog ordering and HUD composition. The M19 graphics
tool reports worker Skia/native IoU `0.995868`. TinyFarm `--proof` completed and
its shared Profile tree proof was captured. Neither consumer changes gameplay,
fog, or UI semantics.

## 12. Cache, atlas, and performance

The 64-frame diagnostic reports one upload per independently measured renderer,
128 cache hits, a `1024 x 1024` atlas, and no per-frame rebuild or upload. CPU
submission is unchanged. Neutral and derivative mean frame times were
`0.1857 ms` and `0.1832 ms`; both allocated `85,184` bytes across the harness's
64 captured submissions. This shows no measurable derivative catastrophe; it
is not a GPU microbenchmark claim.

## 13. Prohibited approaches

No postprocess sharpening, contrast boost, threshold clipping, erosion, source
retouch, nearest-filter fix, or pixel-snap dependency is used. Linear MSDF
sampling and source geometry remain unchanged.

## 14. Owner-lane fixes and deferred issues

The compiler owns stage legality and lowering; `ProfileMsdf.v.ts` owns Profile
coverage; Profile realization owns neutral threshold; applications select the
Profile pipeline; glyph tooling remains the text owner. General derivative
intrinsics, MSDF rewrites, atlas-size reductions, and broad AA frameworks are
deferred. The standalone glyph CPU-reference drift is also deferred as a
separate text-harness issue because the final shader matches its qualified
source and M19C does not touch glyph reconstruction.

## 15. Exact M20 recommendation

Use this harness before changing atlas pressure: isolate the standalone glyph
runner's fresh-run CPU-reference drift, then test whether Profile field minimum
can safely fall from 192 to 96 without corner artifacts. Do not alter production
atlas policy until both consumers and the field-size sweep agree.

## 16. Evidence and validation

Machine-readable evidence is in
`artifacts/aurelian-profile-derivative-reconstruction-m19c`. Focused compiler
tests (6), both MSDF shader tests, full `Aurelian.slnx` build/tests, Mossward
launch smoke, M19 native graphics, and TinyFarm proof were run. The final diff
is a focused compiler intrinsic, one Profile shader/pipeline variant, consumer
wiring, diagnostic upgrades, tests, docs, and evidence; no gameplay or fog
semantics changed. The focused text/source stat is 16 files, approximately
`+872/-69`; generated PNG/JSON evidence is additional binary/data output.
