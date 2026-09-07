# OBLIVION-NOTEBOOK-NATIVE-SPRITE-EDGE-DIAGNOSTICS-M19B

## Outcome

Outcome B with a bounded correction. A dedicated native Profile edge-diagnostics command now renders the real Mossward worker and tree control against dark and light mattes, compares CPU and Vulkan output at actual world scales, enlarges the panels with nearest-neighbor inspection zoom, exports atlas/field metadata, and records alpha-edge and silhouette metrics. The evidence found and fixed a major coverage-eroding policy mismatch, then isolated the remaining softness to small-field MSDF minification/reconstruction without changing the asset, adding a postprocess, or altering glyph rendering.

## Reproduction

```powershell
dotnet run --project tools/Aurelian.NativeSpriteEdgeDiagnosticsM19B -c Release -- --asset worker --asset tree --zoom 8
```

`--asset` is repeatable and accepts any Mossward `.profile.tsx` asset. `--zoom` accepts `1`, `2`, `4`, `8`, or `16`. Every inspection sheet uses the panel order CPU reference, former native policy, fixed native policy, and amplified CPU/former-native difference. Both dark and light matte sheets are produced. The enlarged pixels are nearest-neighbor copies of the 96 by 96 realization, so inspection does not add filtering.

## Diagnosis

The prior M19 comparison rendered the worker near 150 by 200 pixels. Mossward renders the authored worker at scale `0.9`, approximately 44 pixels high, where its boots, sash, face, hat, shaft, and tool head are separate small MSDF fields. At this real scale the former native path had:

- worker silhouette IoU `0.825` against the CPU reference;
- `162` opaque native pixels versus `444` CPU pixels;
- estimated alpha transition width `2.594` pixels versus `0.903` pixels on CPU.

The tree control remained much closer: IoU `0.969` and transition width `1.132` versus `0.924` pixels on CPU. Rounding the presentation origin improved worker IoU by only `0.011` and changed native transition width by `-0.014` pixels, ruling out pixel snapping as the cause.

The sampler is linear as required for MSDF, mipmapping is disabled, atlas UVs are derived from exact integer regions, and the renderer uses the qualified straight-alpha blend. The defect was the glyph-oriented small-screen threshold compensation being reused for Profile compositions. Applying its raised threshold independently to every tiny paint field eroded opaque coverage and made the assembled worker look translucent and soft.

## Fix and proof

`ProfileNativeRealizationCache` now gives Profile assets an explicit neutral reconstruction threshold of `0.5`. The shared shader and glyph adapter retain their existing size policy. A bounded reconstruction-options overload remains available to the diagnostic tool, so the former policy and candidate thresholds can be reproduced without another renderer.

With the neutral Profile threshold:

- worker silhouette IoU rises from `0.825` to `0.955`;
- tree silhouette IoU rises from `0.969` to `0.993`;
- the real four-frame Mossward Vulkan launch smoke exits successfully.

This correction restores the silhouette but does not collapse the worker's measured partial-coverage band: it remains about `2.57` pixels versus `0.90` on direct CPU contours. The remaining defect is therefore not threshold, placement, atlas crop, alpha mode, sampler selection, or mip selection. It is specifically the small-field MSDF reconstruction/minification seam. The next bounded experiment should replace the fixed reconstruction-width estimate with a screen-derivative-based width (or an equivalent CPU-calculated per-quad screen width) and evaluate it in this same tool before changing the shared shader.

The evidence directory is `artifacts/oblivion-notebook-native-sprite-edge-diagnostics-m19b`. `edge-diagnostics.json` is the machine-readable authority. `worker-dark-8x.png` and `worker-light-8x.png` are the primary isolated proofs; the corresponding tree sheets are the control. `strategy-before.png` and `strategy-after.png` retain the real native launch frames.

## Ownership

Reusable measurement and immutable atlas-field description live in `Aurelian.Profile.Graphics`, beside Profile compilation and realization. The executable owns Vulkan/Skia realization, contact-sheet construction, CLI selection, and evidence export. Mossward continues to submit ordinary `ProfileNativeInstance` values; no simulation or asset semantics moved into tooling, and no second rendering architecture was introduced.
