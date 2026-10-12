# TRELLIS.2 attention tile throughput pass

The actual pot-sized transformer block is faster with 256-row query tiles while
preserving every output bit. Learned precision, sampling steps and model weights
are unchanged. This is a scheduling/occupancy pass, not mesh cleanup.

## Change and bounded ownership

`AttentionTiled` now accepts explicit query tiles up to 256 rows. TRELLIS's
self/cross-attention graph uses the compute owner's preferred 256-row tile;
the generic API retains its conservative 16-row default. Key/value gathers
join the first query batch instead of requiring a separate submission per head.
The batch forecast accounts for those two gather dispatches.

Existing FP32 SGEMM, row softmax and BF16 output-boundary shaders are reused.
Each query's reduction order stays the same. No new shader, precision mode,
sampling approximation, host feature arithmetic or intermediate readback is used.
The existing Dominatus feedforward controller and Vulkan submission ring still
own lookahead and physical completion. Scratch remains proportional to query
tile rows times keys, with the original explicit allocation/device range checks.

## Alternating warmed native measurements

RTX 3070, 5,870 actual pot coordinates, 12 heads of width 128, actual pinned shape
weights and qualified DINO conditioning. Warm both paths, then alternate three
64/256-row runs with the same staged FP32 affines and controller. The median
excludes explicit final-output qualification readback.

| Measure | 64 rows | 256 rows |
|---|---:|---:|
| Full first transformer block | 1,956.29 ms | 1,427.88 ms |
| Self-attention submissions | 120 | 36 |
| Self-attention dispatches | 55,224 | 13,824 |
| Cross-attention submissions | 36 | 12 |
| Cross-attention dispatches | 14,376 | 3,612 |
| Self-attention scratch | 11,289,120 bytes | 29,714,976 bytes |
| Cross-attention scratch | 2,071,856 bytes | 5,626,160 bytes |

The block improves **1.370x (27.0% less time)** and outputs are byte-identical
in every alternating trial. Peak owned allocation high water is 858,716,400 bytes;
the larger attention scratch does not exceed the existing FFN allocation peak.
Scratch figures include full/tail query scratch and gathered key/value rows,
not just the score matrices. The older pre-change self-attention path also had
12 separate key-gather submissions, giving 132 rather than this comparison's 120.

This microbenchmark controls both configurations within one process. Historical
full-sampler times include different concurrent work and are not a controlled
speedup measurement.

A final independent repetition after the complete sampler run measured
**2,033.56 -> 1,416.77 ms (1.435x, 30.3% less time)**, again byte-identical in
all three alternating trials. Its evidence is in
`artifacts/local/trellis2/shape/attention-throughput-final/evidence.json` and
explicitly records both controller selection and query tile rows per trial.
The table above retains the first repetition rather than selecting its fastest
individual trial.

## Validation and reproduction

The complete Aurelian solution builds without warnings/errors and all **1,369**
tests pass. Native sparse checks include a 257-query/5,870-key head with a
256-row tile plus one-row tail: every output bit matches the 64-row path.
Selected boundary queries also agree with a separate double-precision CPU
attention oracle (maximum absolute error **6.13e-10**). Native tests verify exact
scratch accounting, no hidden readbacks and named rejection of 257-row tiles.

```powershell
dotnet build tools/Aurelian.GraphicsProof/Aurelian.GraphicsProof.csproj -c Release -m:1
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll --trellis2-throughput --compare-attention-tiles --output artifacts/local/trellis2/shape/attention-throughput
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll --trellis2-sample-material --output artifacts/local/trellis2/material/sampled-attention
```

The sampler uses the same 12 steps, noise seed, shape conditioning and checkpoint
as the earlier complete material run. Its output directory is separate so the
original trajectory remains available for byte-level comparison. New sampler
evidence records `AttentionQueryTileRows = 256` explicitly.

The optimized complete material sampler finished all twelve predictions and 396
named stages in **509.01 s (8.48 minutes)**. All **17** compared files are
byte-identical to the previous run: initial noise, shape conditioning, coordinate
rows, every one of the twelve Euler latents, the final latent and the decoder
latent. Provenance checks also match checkpoint, DINO, shape, seed and sampler
settings. There are still exactly twelve final-velocity readbacks. Peak owned
GPU buffers are unchanged at **859,467,120 bytes**.

The previous run took 763.03 s (12.72 minutes), but overlapped mesh generation,
offline oracle and regression work. The 509-second result is a measured complete
run; the historical ratio is not a controlled estimate of optimization speedup.
`artifacts/local/trellis2/material/sampled-attention/comparison-evidence.json`
retains all 17 file hashes and explicitly separates those two timing claims.
Since decoder input is identical and no decoder or asset code changed, the
existing mesh/material oracle and asset evidence still apply to these bytes.

Prior upstream numerical-parity limits remain unchanged. Byte equality against
our earlier sampler demonstrates that this scheduling change adds no new error;
it does not retroactively qualify that sampler against upstream Torch/CUDA.
The full mesh and material artifacts are still prototype assets requiring cleanup.
