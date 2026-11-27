# R3 compact ReSTIR history

Each history or candidate plane stores 56 bytes per pixel, down from 96.
The record holds four pairs of packed metadata and FP32 reservoir weight,
followed by FP32 world position and normal. Each metadata word holds an
8-bit light index and a 14-bit integer sample mass. Slot zero also stores the
quota. The admitted ray budget gives each pixel at least one slot, so zero
stored quota marks background. The largest supported spatial merge
accumulates at most nine contributions of mass 1,024, which fits in 14 bits.
Reuse checks need the validity flag and FP32 geometry, so the old depth lane
can be dropped.

Temporal and reprojected sampling allocate two ping-pong history planes.
Spatiotemporal sampling adds an immutable candidate plane. Spatial-only
sampling allocates only that candidate plane when started fresh. The spatial
pass uses the geometry already loaded for each donor and writes each final
reservoir once when temporal reuse needs it. A renderer that switches away
from temporal reuse keeps its allocated planes for later use and resets their
validity on each switch.

Applications that supply their own SPIR-V should build both
`SampledLocalLights3D.v.ts` and `RestirSpatial3D.v.ts` from the same revision
as the renderer, since the compute shaders and host share the reservoir layout.

## Native Vulkan comparison

Both builds use the [direct visibility-buffer transport](rendering-r3-visibility-transport.md).
The control retains the 96-byte reservoir. The Vulkan proof runs the same
256-light hall with two frames in flight, 24 warm-up frames and 48 timed
frames per row. It alternates three and four ray slots per pixel, uses eight
spatial neighbors where applicable and admits history mass up to 1,024.
Timing covers the full GPU frame. Allocation counts the device memory
assigned by Vulkan to the history and candidate planes of a fresh renderer.

On an RTX 3070, the runs were ordered original, compact, compact, original.
Each GPU time in the table is the midpoint of that layout's two run medians.

| Extent | Mode | Vulkan allocation, MiB | GPU frame, ms |
| --- | --- | ---: | ---: |
| 1280×720 | Temporal | 168.75 → 98.44 | 7.85 → 5.51 |
| 1280×720 | Reprojected | 168.75 → 98.44 | 7.44 → 5.39 |
| 1280×720 | Spatial only | 253.13 → 49.22 | 13.19 → 8.99 |
| 1280×720 | Spatiotemporal | 253.13 → 147.66 | 13.98 → 9.29 |
| 1920×1080 | Temporal | 379.69 → 221.48 | 16.42 → 11.13 |
| 1920×1080 | Reprojected | 379.69 → 221.48 | 16.32 → 11.13 |
| 1920×1080 | Spatial only | 569.53 → 110.74 | 24.90 → 17.44 |
| 1920×1080 | Spatiotemporal | 569.53 → 332.23 | 24.80 → 17.69 |

Both compact runs improved median and p95 for every row in this series.
An earlier smoke comparison had smaller timing differences, with 720p
reprojection within 0.08 ms of the original. The allocation savings of
70.31–458.79 MiB apply to the measured sizes regardless of GPU timing.
The full timestamp samples, allocations and hashes are in each arm's
`evidence.json` under `artifacts/local/restir-history/`.

## Image and motion checks

At both resolutions, all four modes matched the final image, linear HDR and
light-sample packet bit for bit. Query, reuse and rejection counts also matched.

The 24-frame static, camera-pan and moving-receiver sequences matched all
18 quality rows, including linear HDR error, mean energy, frame-to-frame
residual error, and reuse and rejection counts. All 31 PNGs and reference
HDR files from the motion proof matched byte for byte. Spatiotemporal reuse
still rejected all nine identified disocclusions in the moving-receiver
sequence. The jittered TAA, camera-cut and variable-budget checks also passed.
Across the ReSTIR, sampled-light and area-light proofs, all 141 PNG and raw HDR
files matched byte for byte. The motion, sampled-light and area-light runs
completed with Vulkan synchronization validation enabled.
The storage proof also passed with synchronization validation. It switches
from spatial-only to spatiotemporal and back with frames pending, verifying
the allocation transition, history reset, resumption of temporal reuse and
the original spatial sample sequence.

## Reproduce

Run the proofs from a Release build:

```powershell
$proof = 'tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll'
dotnet $proof --rendering-restir-history --output artifacts/local/restir-history/current
$env:VK_LAYER_VALIDATE_SYNC = '1'
dotnet $proof --rendering-restir --output artifacts/local/restir-history/current-motion
dotnet $proof --rendering-light-sampling --output artifacts/local/restir-history/current-sampling
dotnet $proof --rendering-area --output artifacts/local/restir-history/current-area
```
