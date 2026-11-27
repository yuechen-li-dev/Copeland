# R3 sampled-light denoising

The dedicated denoiser filters the stochastic direct-light output from surface
resolve in linear HDR. It uses the same tail and compose passes as area-light
reconstruction. Exact lights, environment, emission and other surface lighting
remain in the head. The renderer composes the head with the filtered tail
before temporal resolve. The opt-in path caps the final TAA history weight
at 0.5, so reconstructed current radiance can respond faster to changes in
visibility. An authored lower history weight still applies.

The experiments compared one spatial pass, two spatial passes and the existing
temporal tail filter with TAA alone. One pass limits the footprint to three by
three pixels. It uses current normals, material, receiver plane and radiance
for edge weights. A visibility difference strengthens rejection across a
radiance discontinuity. When a cardinal neighbor crosses a geometry boundary
or a strong full-visibility-to-zero-visibility boundary, the center sample
passes through.
A fresh spatial renderer allocates two RGBA16F targets for filtering and
composition, with no denoising history images. It adds no rays.

The geometry-guided filter follows the edge-stopping idea in the original
à-trous work. SVGF explores spatiotemporal variance with a separate
accumulation history. [Dammertz et al.](https://holger.dammertz.org/publications/dammertz10hpg_bibtex.html)
[Schied et al.](https://research.nvidia.com/labs/rtr/publication/schied2017spatiotemporal/)

## Enable

Set `DenoiseSampledLighting = true` on `LocalLightSampling3D` with a sampled
local-light mode and the retained ray-shadow path. The renderer needs
`LocalTailSpatial3D.v.ts` and `LocalTailCompose3D.v.ts` through
`SurfaceLightingPrograms`, and a surface resolve with the stochastic tail
output. The denoiser shares those attachments with `ReconstructTail`.
The two options are mutually exclusive and require opaque surfaces without
subsurface diffusion. The default sampler remains exact.

## Qualification

The Vulkan proof runs the exact-light renderer as a current-frame reference at
the same raster jitter and tone mapping. Each sampled arm uses the same ray
budget. The proof includes TAA alone at the default weight and at the
denoiser's 0.5 weight limit. It hashes the full GPU sample packet across
the measured frames to check that the input is identical. The cold arms
retain that jitter and turn off color history blending.

The proof uses the R6 vacated-pixel mask and both of its luminance metrics.
Pixels enter the mask when the exact reference darkens by at least 0.07.
Ghost energy measures positive residual against the current exact reference.
Excess-history ghost energy measures positive residual above both the exact
reference and that arm's matched cold image. The changing-shadow test also
applies the same mask and formula to pixels that remain on the floor across
both frames. Display error and frame-to-frame residual error use linearized RGB.

### RTX 3070 results

Each test uses 12 warm-up and 24 measured frames at 192×128 with a budget of
two rays per allocated pixel. The hall has 256 punctual lights. The
changing-shadow sequence uses one strong key and 64 dim fill lights and
toggles a caster's ray visibility while its raster geometry stays in place.

| Sequence | Linear-display RMSE, stock TAA → denoised | Frame-to-frame error reduction | R6 excess-history ghost energy, stock TAA → denoised |
| --- | ---: | ---: | ---: |
| Static importance | 0.04063 → 0.03431 | 15.7% | 0.213262 → 0.186719 |
| Camera pan, spatiotemporal ReSTIR | 0.02521 → 0.02410 | 6.0% | 0.011134 → 0.008419 |
| Moving receiver, importance | 0.04038 → 0.03397 | 15.5% | 0.135398 → 0.117119 |
| Changing shadow, importance | 0.01212 → 0.00792 | 32.2% | 0.069099 → 0.045215 |
| Changing shadow, 901 stable floor pixel observations | | | 0.040307 → 0.027301 |

Both R6 ghost metrics went down versus stock TAA. At the same 0.5 history
cap, the spatial pass cut frame-to-frame error by 0.6–14.5%. The largest
increase in excess-history ghost energy in that ablation was 0.000202 on
the moving receiver. The unmodified spatial filter and legacy temporal tail
control both increased changing-shadow ghost energy at stock TAA settings.

Mean LDR-FLIP over three saved frames per sequence fell on the static,
moving-receiver and changing-shadow fixtures. The camera-pan mean rose
from 0.04569 to 0.05012 against stock TAA. The camera-pan control with a
0.5 TAA cap scored 0.05176.

At 720p, the median full GPU frame was 8.18 and 8.51 ms for the two stock
runs and 8.26 and 8.30 ms with denoising on. A repeat with the same run order
measured 8.13–8.14 ms stock and 8.26–8.28 ms denoised, about 0.13 ms more
per frame. The repeat matched the quality rows and saved images byte for byte.
The two color targets add 16 logical bytes per pixel. The Vulkan proof
also verifies the exact-light head and deterministic camera-cut reset.
The native captures, timestamp samples and FLIP maps are in
`artifacts/local/light-denoising/boundary/`. The final native replay is in
`artifacts/local/light-denoising/verified-light-denoising/`.

```powershell
$env:VK_LAYER_VALIDATE_SYNC = '1'
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --rendering-light-denoising --output artifacts/local/light-denoising/current
& artifacts/local/rendering-metrics-tools/venv/Scripts/python.exe `
  tools/aurelian-rendering-reference/evaluate_metrics.py `
  artifacts/local/light-denoising/current/metrics-manifest.json
```
