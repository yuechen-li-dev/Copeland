# R7 eye adaptation, ocular glare and low-light vision

## Exposure

The GPU luminance meter and adaptation step operate on scene-linear light. The
RTX 3070 step fixture agreed with independently evaluated exponential curves
within .000006 EV and reproduced the same adaptation at 30 and 120 Hz within
.000001 EV. The saved Beacon route spans 4.40 EV. The median meter GPU time
was .034 ms at 960×540. The proof also exercises reset, changes of raster
scale and queued frames.

## Glare

The ocular pass uses angular sampling of Spencer's eye-scattering profile with
bounded energy. The point test measures cumulative radial response and total
RGB energy relative to the original impulse. The equal-energy comparison
normalizes the legacy and ocular profiles before measuring their shape.

| Fixture | Legacy energy | Ocular energy | Equal-energy profile MAE, legacy → ocular |
| --- | ---: | ---: | ---: |
| 321×181, 50° | 1.153 | .981 | .0245 → .0032 |
| 640×360, 38° | 1.142 | .975 | .0702 → .0054 |
| 640×360, 72° | 1.142 | .987 | .0426 → .0036 |

The proof includes fractional-pixel offsets, a clipped corner impulse, a
uniform field and a real Beacon route. Lost energy outside the visible field
is expected. The glare setting selects the ocular pass in place of bloom.

## Rod–cone color

At fixed exposure the cyan-to-red luminance ratio changed from 1.013 to 1.300
in the mesopic fixture and from 1.026 to 1.735 in the scotopic fixture.
Chroma retention was .596 and .356 respectively. The daylight control is
byte-identical. The renderer uses the adapted luminance state for the Purkinje
shift, while the display retains authored exposure controls.

Wet Night has seven matched captures at 1280×800, including the authored grade,
fixed-exposure controls and different rod–cone strengths. The controls show
the effect under the same exposure, with both authored and neutral ambient
light. The authored grade and the physiological response express different
look decisions. The images establish the shift and its effect on the rain
fixture. They provide no perceptual preference score. Beacon's separate
bright-to-dark route tests warning and guide cue contrast and hue.

Run `Aurelian.GraphicsProof` with `--rendering-r7-eye`, `--rendering-r7-glare`
or `--rendering-r7-low-light`. The saved evidence and PNGs are under
`artifacts/local/rendering-r7-eye/experiment-3`, `rendering-r7-glare/comparison`
and `rendering-r7-low-light/prototype-6`. The optics remain explicit settings
for ordinary play.

The review reran both glare and low-light on Vulkan. On the current Beacon
glare fixture, ocular optics cost .239 ms and legacy bloom cost .135 ms.
The adapted low-light route ended with warning and guide contrast of .3390
and .5786. Those reruns are under `artifacts/local/rendering-review/`.
