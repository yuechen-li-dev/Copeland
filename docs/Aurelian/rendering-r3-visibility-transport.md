# R3 local-light visibility transport

The exact and sampled compute kernels already write sixteen FP32 words per
pixel. Previously the renderer copied those words into an RGBA32F atlas before
surface resolve. The current resolve reads the same storage buffer using the
raster pixel coordinate. Sample IDs, weights, quotas and reuse diagnostics keep
their representation. Exact masks are read with integer bit operations.
The frame plan declares the producer's compute write and the resolve's fragment
read. Older baked surface shaders retain their texture path. Re-bake a shader
pack to use the direct buffer path.
The Beacon GI launch smoke also completed with the previous surface shader
unchanged in the pack and its required glossy resolve shader supplied.

| Local-ray mode | Logical B/pixel, before → after direct-buffer resolve | 720p MiB | 1080p MiB |
| --- | ---: | ---: | ---: |
| Exact or non-history sampling | 204 → 140 | 179.3 → 123.0 | 403.4 → 276.9 |
| Temporal reservoir | 396 → 332 | 348.0 → 291.8 | 783.1 → 656.5 |
| Spatial or spatiotemporal reservoir | 492 → 428 | 432.4 → 376.2 | 972.9 → 846.4 |

This removes one 64 B/pixel payload, or 56.25 MiB at 720p and 126.56 MiB at
1080p. Totals cover the local-ray owner's pixel buffers. Device allocation
alignment, acceleration, the base renderer's G-buffer and the separate
sampled-tail filter are outside the table. The later
[ReSTIR history change](rendering-r3-restir-history.md) reduces the reservoir
storage further and gives the resulting footprint.

## Native comparison

On an RTX 3070, the Release control at `38a2396d` and the direct-buffer
renderer both passed `--rendering-r3`. The matched 256-shadowed-light
hall uses 24 warm-up and 64 measured frames at each extent:

| Extent | Previous GPU median | Direct buffer GPU median |
| --- | ---: | ---: |
| 640×360 | 2.061 ms | 1.655 ms |
| 1280×720 | 6.529 ms | 5.054 ms |

The sampled fixtures also passed. At 720p, importance sampling measured
4.856 → 3.591 ms and spatiotemporal ReSTIR measured 6.864 → 5.573 ms.
All 128 matching PNG captures decoded to identical RGB bytes across exact,
sampled, ReSTIR and area-light proofs. The comparison is recorded in
`transport-image-parity.json`.
The full-path timings include scene rendering and lighting and can vary with
GPU clocks. The transport allocation reduction is independent of that timing.
R3's historical 11.4 ms measurement describes an earlier checkout.

The sampled-light baseline also has a dedicated [temporal and spatial tail
reconstruction](rendering-r3-area-tail.md). The 18-source fixture previously
recorded an additional .213 ms at 720p for the hybrid filter. On moving
occluders the retained old-shadow projection was 1.1%, under the declared
10% gate. This addresses a measured subset of stochastic lighting. The
broader many-light default still needs filter quality and cost on representative
motion and changing lighting at the same frame budget.

Run `Aurelian.GraphicsProof` with `--rendering-r3`,
`--rendering-light-sampling`, `--rendering-restir`, and `--rendering-area`.
The matched runs for this review are under
`artifacts/local/rendering-review/`.
