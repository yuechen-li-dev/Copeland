# R5 hardware and assisted reflections

Sharp hardware ray queries give the baseline for opaque retained visibility
surfaces. The trace reconstructs the secondary material and records hit identity
and distance. A moved mirror target matched fresh history after a TLAS refit.
On the saved RTX 3070 Beacon sequence, adding sharp reflections to runtime GI
increased median GPU frame time from 7.82 to 8.96 ms at 720p and from 15.45 to
17.62 ms at 1080p.

## GGX and reconstruction

The glossy branch traces GGX samples and reconstructs from motion, material,
normal and secondary hit guides. Half-resolution mode rotates one ray through
each 2×2 group. At roughness .24, HDR RMSE against the accumulated glossy
reference was .456 for one raw sample, .039 with full-resolution reconstruction
and .084 with half-resolution reconstruction. The independent 15-probe integral
used 8,192 samples per probe and recorded maximum RGB distance .0668. Camera
motion and changing secondary hits have separate reset controls.

## Probe and screen assistance

| Experiment | Matched control and result | Use |
| --- | --- | --- |
| Probe guidance | Bright emissive card, full-resolution GGX RMSE .1170 → .0676. At 720p, Beacon with GI moved from 4.81 to 4.96 ms per frame. | Guide confident runtime-cache proposals. Near, curved, partial-field and field-off fixtures match hardware fallback. |
| Screen bounds | Saved 720p layered-fixture trace medians are .738 → .645 ms. Beacon with GI moves from .634 to .645 ms for the trace. | Keep opt-in for deep overlap. Verify screen candidates with hardware and retry on a miss. |

Screen-assisted camera motion exercised 25,066 candidates and 12,139 fallbacks.
The off-screen refit case matches the hardware path in hit identity and distance.
These controls cover bounded scenes. Reflection quality and cost vary with
receiver roughness, screen coverage, secondary visibility and GI activity.

After building `Aurelian.slnx`, run the graphics proof with
`--rendering-r5a`, `--rendering-r5b`, `--rendering-r5c`, or
`--rendering-r5d`. The historical RTX 3070 evidence is under
`artifacts/local/rendering-r5a/final`, `rendering-r5b/final`,
`rendering-r5c/qualified` and `rendering-r5d/qualified`. Those directories
include the sequence metrics, images and full-frame timing breakdowns.
