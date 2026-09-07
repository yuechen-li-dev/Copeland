# Aurelian MSDF reconstruction

## Practical law

Aurelian stores a normalized multi-channel signed-distance field in RGB. The
reconstructed distance is the median RGB channel value and the contour is the
neutral value `0.5`. Values below the contour are outside; values above it are
inside. Atlas `PixelRange` records the generation spread in field texels. It is
generation metadata, not an antialiasing width in screen pixels.

| Path | Reconstruction | Small-size behavior | Owner |
| --- | --- | --- | --- |
| CPU Profile | Direct antialiased contour paint | About one-pixel transition | CPU Profile renderer |
| Glyph MSDF | Median RGB, size-compensated threshold, fixed normalized ramp | Qualified for small text | `MsdfText.v.ts` |
| Profile M19B | Median RGB, neutral `0.5`, fixed normalized ramp | Correct silhouette, but a broad minified transition | diagnostic-only baseline |
| Profile M19C | Median RGB, neutral `0.5`, derivative-width ramp | Stable near-one-pixel transition | `ProfileMsdf.v.ts` |

## Profile reconstruction

The Profile pixel shader computes:

```text
distance = median(sample.r, sample.g, sample.b)
width = max(fwidth(distance), 0.000001)
coverage = smoothCubic(distance, 0.5 - width / 2, 0.5 + width / 2)
alpha = tintAlpha * coverage
```

`fwidth(distance)` is in normalized encoded-distance units per screen pixel.
Using that value as the complete coverage ramp therefore targets approximately
one screen pixel of antialiasing regardless of Profile scale or field size. The
pixel range remains important when generating a sufficiently accurate field,
but it does not need to be multiplied into this derivative: the sampled field
already encodes that scale, and its screen derivative measures the final
projection directly.

A fixed normalized ramp is fragile under minification because one encoded
distance interval covers a different number of screen pixels at each scale.
Nearest sampling is not a solution: it discards continuous distance
interpolation and produces unstable, jagged subpixel motion.

## Semantic ownership

Profiles and glyphs deliberately use separate shaders and pipeline kinds.
Profiles use neutral threshold plus screen derivatives. Glyphs retain their
qualified small-screen threshold compensation and fixed ramp. Both reuse the
same atlas texture, linear sampler, median-channel convention, submission
record, cache discipline, and straight-alpha blend convention.

`Fwidth(f32)` is a pixel-stage-only Visual TypeScript intrinsic. The compiler
rejects other types and vertex-stage use, lowers it through VD-MIR to HLSL
`fwidth`, and validates the resulting SPIR-V. Generated HLSL is never patched.

## Diagnostics

Use `tools/Aurelian.NativeSpriteEdgeDiagnosticsM19B` to compare the CPU,
legacy, M19B-neutral, and M19C-derivative realizations. The field-size sweep is
diagnostic only; M19C does not reduce production field or atlas sizes.
