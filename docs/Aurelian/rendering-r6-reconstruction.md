# R6 temporal reconstruction and dynamic resolution

The Vulkan renderer supports native, two-thirds and half raster scales. The
scaled path reconstructs the display target with motion, depth, current
neighborhood and guarded history. The proof uses a doubled-resolution
reference and a cold-render control. It records linear-display RMSE, ghost
energy on vacated pixels, and the portion of that energy in excess of both
the reference and the cold image.

The saved RTX 3070 proof records zero **excess history** ghost energy for the
six measured scaled temporal cases, including a moving thin object and
particles. That gate measures a defined area after motion. Spatial error,
smearing on still-covered pixels and other trajectories remain separate
questions. Reset at an odd display extent, 513×289, matches a fresh capture.
Learned history confidence remains a research idea. The existing closed-form
policy and motion fixtures provide the controls for any learned candidate.

The dynamic-resolution policy uses Dominatus utility, hysteresis and minimum
commitment. It tests actual render scales using completed GPU timing and
recovers detail when load drops. At 1920×1080 output, the RTX 3070 replayed a
Beacon sequence with runtime GI and reconstructed glossy reflections in its
heavy phases.

| Heavy repeat | Median GPU ms | p95 GPU ms | Settled raster scale |
| --- | ---: | ---: | --- |
| Fixed native | 13.582 | 15.264 | Native |
| Fixed two-thirds | 8.681 | 8.879 | Two-thirds |
| Fixed half | 6.139 | 6.151 | Half |
| Adaptive, 11.23 ms budget | 9.168 | 9.803 | Two-thirds |
| Adaptive, 7.42 ms budget | 6.142 | 6.151 | Half |

This comparison keeps the lighting mode across scale transitions. It covers the
specified workload changes and GPU. The shipping profile starts at native
resolution.

Run `Aurelian.GraphicsProof --rendering-r6` for the reference and the ghost
metrics, or `--rendering-r6-dynamic` for the Beacon swapchain proof. Saved
evidence is under `artifacts/local/rendering-r6/final` and
`artifacts/local/rendering-r6-dynamic/final-dynamic`.
