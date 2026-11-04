# R3: raster admission across motion snapshots

Continuing `NativeInstanceSet3D` snapshots now retain their bindless material table,
mesh/index atlas, visibility targets, culling pipelines, command buffers and HZB/LOD
state. Motion changes the frame data rather than re-admitting the same topology.

The predecessor contract owns eligibility: ordered identity, mesh and material must
match the renderer's previous snapshot. Changed topology, material, ordering or
bindless/visibility mode still retires and rebuilds the affected resources.
`RasterAdmissionMode = NativeRasterAdmissionMode3D.Rebuild` retains the original
re-admission behavior as a control. Automatic retention is the default.

The retained owner still visits moving instances to pack current, previous and
normal transforms. It refreshes the source records of the existing bindless draw
runs, streams 160-byte visibility instance records and recomputes cluster spheres,
cones and prepared-LOD error scales from cached local geometry. Candidate and
instance transports use mapped buffers for both frame slots; writes occur after
slot retirement. Explicit host-to-compute/graphics dependencies make the streams
available to their consumers. Immutable geometry and material metadata are not
uploaded again. Unchanged static snapshots keep their zero-visit fast path.

The native proof compares 96 moving capture pairs against full re-admission:
exact and spatiotemporal reservoir lighting, each with HZB disabled/enabled, for 24
frames. All output must be byte-identical. Ordinary frustum/cone ranks also compare
against current CPU bounds, and counters require one initial topology admission
followed by motion reuse. Reusing history with HZB must match the cold-history
control's final visible result.

The 720p comparison uses the same nine-instance hybrid scene, 256 shadowed lights,
CDF importance, TLAS updates, 16 warm-up frames and 40 measured frames. Capture is
disabled. The final combined run measured:

| Raster admission | Prepare/submit ms | GPU frame ms | Wall frames/s |
|---|---:|---:|---:|
| Rebuild per changed snapshot | 14.390 | 5.881 | 43.5 |
| Retain compatible topology | 0.699 | 5.786 | 173.7 |

These are elapsed CPU-side phases, not CPU utilization. Ordinary frame-slot waits
are 8.003 ms and 5.033 ms respectively. The complete throughput gain is about 4x.
Wall
throughput includes frame-slot waiting and GPU drain. Moving transforms still cost
O(instance/cluster count) CPU packing and mapped uploads; this is not an entirely
allocation-free renderer or GPU-authored scene admission.

Run the combined regression and qualification with:

```powershell
tools/aurelian-area-lights/Qualify.ps1 -Output artifacts/local/rendering-area/final
```

Its `raster-motion/` folder contains the native comparison evidence. The TLAS
microbenchmark explicitly fixes raster admission to `Rebuild` in both TLAS modes,
preserving its original isolated comparison. The raster microbenchmark fixes TLAS
updates in both raster modes. This prevents a later improvement from being
misattributed to the earlier stage.

See [area-light sampling and tail reconstruction](rendering-r3-area-tail.md) for
the subsequent lighting experiment and final qualification ledger.
