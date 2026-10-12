# TRELLIS.2 sparse shape stage on C#/Vulkan

This continues the learned occupancy experiment with the pinned 512 shape SLat
flow and the first resolution of the learned mesh decoder. CPU code owns sparse
coordinates, neighborhoods and subdivision mappings. Learned arithmetic remains
on Vulkan. Dominatus drives named compute stages through the existing coroutine
adapter. There is no CUDA, Torch or Python invocation in the inference path.

## Actual model path

The SLat flow has the same 1536-channel, 30-block transformer architecture as the
occupancy flow, but uses 32-channel features on irregular coordinates. The actual
pot supplies 5,870 occupied coordinates. The graph now accepts explicit XYZ
coordinate tensors for RoPE instead of deriving coordinates from lattice rows.
Its input/output projections use the pinned shape checkpoint, not the occupancy
checkpoint. Context still contains all 1029 qualified DINOv3 tokens.

The existing Euler sampler is reused with the upstream shape settings: 12 steps,
time rescale 3, guidance 7.5, clean-state rescale .5 and guidance interval [.6,1].
This requires 12 conditional and nine unconditional full-model evaluations.
Final latent normalization uses the exact pinned pipeline mean/std vectors;
the pipeline JSON is downloaded/verified alongside the weights.

The actual learned decoder prefix runs `from_latent`, four 1024-channel sparse
ConvNeXt blocks, and `blocks.0.4.to_subdiv`. Sparse convolution weights are
O/kernel-X/kernel-Y/kernel-Z/channel, distinct from the dense occupancy VAE's
O/channel/kernel-X/kernel-Y/kernel-Z. Each sparse convolution gathers a selected
range of CPU-built neighborhoods into GPU kernel-major/channel rows, then uses
streamed matrix multiplication. All relevant upstream FP16 operation boundaries
round explicitly; FP32 accumulation/storage is retained.

`SparseVoxelTopology` preserves input row identity without allocating a dense
resolution-cubed table. Learned subdivision preserves parent row, then child
slot order. Sparse Channel2Spatial uses X as the least-significant child bit,
followed by Y and Z. This differs from dense pixel shuffle and has dedicated
tests. Parent/child mappings and neighbor indices are explicit immutable data.

## Shared engine changes

- `GpuTensorFormat.UInt32` and `UploadUInt32` carry neighbor indices as unsigned
  words. Absent cells use uint.MaxValue. Float uploads/readbacks cannot silently
  reinterpret this representation.
- Coordinate-driven 3D RoPE is a reusable shader/API. Permuting coordinate and
  feature rows together produces exactly permuted outputs.
- `SparseColumns` implements bounded submanifold neighborhood gathering with
  typed unsigned indices and independent outputs. Missing/out-of-range entries
  produce zero, without reading invalid memory.
- Tensor row and non-cooperative matrix inner bounds now admit 8192. Attention
  continues to use bounded query scratch and all keys, including the pot's 5870
  keys. Head normalization uses a guarded 2D workgroup layout above 65535 heads.
- Streamed linear admits K through 32768, covering a 1024-channel 3x3x3 kernel.
  Weight tiles and selected checkpoint reads remain limited to 1048576 values.
  Affine tile sizing also accounts for the appended bias at K=4096.
- Dominatus stage execution remains sequential on one plant. Shape-specific
  agent identities are available. A run started before that cosmetic identity
  change retains the older `trellis.occupancy` identifier in its raw trace;
  checkpoint hashes, stage names and scope identify the actual shape model.

## Qualification and numerical diagnosis

Five new native specimens passed: irregular XYZ RoPE, row permutation,
unsigned-neighbor selected-range/missing-cell gathering, 5870-key attention with
a query tail, and normalization of 65,544 heads across a 2D dispatch. The
attention maximum error was 5.09e-10; gather/permutation/head checks were exact.
Invalid range/representation operations retain named stops. The existing 55
native compute specimens also passed.

The optional independent NumPy oracle replayed all 30 blocks for both first-step
predictions. The strict 1% guided-velocity bound **failed** at 2.316%; latent RMS
difference was 0.001079. This failure is retained, not relabeled as acceptance.

Explicit diagnostic commands separated the predictions from sampler arithmetic:

- Conditional velocity relative RMS error: 0.3937%.
- Unconditional velocity relative RMS error: 0.3418%.
- Shared timestep modulation: bit-identical to the independent oracle.
- Guided velocity relative RMS error: 2.316%, after guidance amplification.
- Reconstructing Euler from the native predictions: 5.31e-7 relative RMS error
  and maximum absolute difference 6.68e-6.

Selected block snapshots show differences accumulating through BF16 boundaries.
An equivalent logistic GELU diagnostic did not eliminate those differences:
positive velocity remained around 0.3995% from that diagnostic reference.
These measurements isolate transformer numerical differences from coordinate
topology, timestep modulation and Euler arithmetic; they do not establish the
exact first arithmetic operation responsible, or production numerical parity.
Diagnostic commands deliberately read selected block outputs; normal sampling
reads only the final velocity per full evaluation.

The first learned decoder stage also passed an independent NumPy check on a
noise probe: 0.0401% feature RMS error, 0.0372% child-logit RMS error and 99.9979%
child-sign agreement. That probe used real weights but untrained noise input;
its output is not a completed generated-object witness. The actual sampled
latent must be checked separately. No reference threshold is relaxed after a
failed check.

## Concrete remaining decoder boundary

The noise probe predicts 46,353 child rows at resolution 64. The current matrix
owner supports 8192 rows. Its next 2048-wide ConvNeXt activation would also
exceed the explicit 67,108,864-element owner budget. Increasing a row constant
alone does not provide bounded execution of the later 64/128/256/512 stages.

The next implementation is paged sparse feature storage, row-tiled learned
operators and channel-to-spatial feature gathering across those pages. CPU
topology/parent/child maps now provide the required identities. Later decoder
stages and flexible dual-grid mesh extraction remain unimplemented. The first
subdivision visualization and OBJ are diagnostic voxel projections, not the
learned final mesh. Texture flow and material decoding follow the actual mesh.

## Final sampled result

The actual full shape sampler completed all 684 named stages and 21 complete
model evaluations in 1,903.5 seconds (31.7 minutes). It produced 5,870 x 32 finite
shape latents, then applied the pinned decoder normalization. Peak owned buffers
were 862,962,864 bytes (about 823 MiB), including staging/parameters; total
driver/process VRAM was not measured. Only final velocities were read back
during sampling (21 reads).

The final sampled latent ran through the actual first decoder resolution in
10.53 seconds, with 291,445,360 owned bytes and two explicit qualification
readbacks. It predicts **25,365 child coordinates at resolution 64**. Independent
NumPy qualification passed on this actual latent: 0.0261% feature RMS error,
0.0202% child-logit RMS error, and **100% child-sign agreement**. The native
preview consumes those learned child selections and shows the hollow pot at the
finer voxel resolution. It uses a fixed diagnostic material.

The next real-path stop is `AUR-TRELLIS-DECODER-002`: 25,365 child feature rows
versus the current 8192-row owner. Channel-to-spatial feature gathering and
bounded execution of subsequent, larger decoder resolutions still require
implementation. No final learned mesh or materials are generated. The first
guided-step 2.316% numerical discrepancy also remains a failed qualification
limit even though decoder-prefix qualification passed.

Release solution and tool builds passed with no warnings/errors. All **1,359
Aurelian tests** across 30 assemblies and **1,511 Copeland TS tests** passed,
without skips. The 55 existing native compute specimens, five new sparse
specimens, representation/range rejections and Dominatus failure/disposal probe
passed. No legacy Zig compiler/build infrastructure changed; frozen Zig tests
were not run.

This ends as meaningful progression: the full learned shape flow and first
learned decoder resolution now execute on the actual input, and the remaining
memory/layout execution boundary is isolated with real child counts.

## Reproduce

The later [compute feedforward and throughput experiment](trellis2-throughput-experiment.md)
adds default FP32 staging and opt-in cooperative affines. Its block/prediction
evidence is separate from the historical full-sampler time reported here.

Run from the repository root; local inputs/weights stay under ignored artifacts.

```powershell
dotnet build tools/Aurelian.GraphicsProof -c Release -m:1
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --trellis2-fetch-shape
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --trellis2-sparse-kernels
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --trellis2-sample-shape
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --trellis2-shape-decoder-stage

# Optional independent qualification, never invoked by the C#/Vulkan path.
python tools/trellis2/occupancy_flow_reference.py `
  --weights artifacts/local/trellis2/shape/weights/slat_flow_img2shape_dit_1_3B_512_bf16.safetensors `
  --features artifacts/local/trellis2/occupancy/dinov3/features.f32 `
  --sample-dir artifacts/local/trellis2/shape/sampled `
  --coordinates artifacts/local/trellis2/shape/sampled/coordinates.json `
  --output artifacts/local/trellis2/shape/flow-step0-reference
python tools/trellis2/shape_decoder_stage_reference.py `
  --weights artifacts/local/trellis2/shape/weights/shape_dec_next_dc_f16c32_fp16.safetensors `
  --latent artifacts/local/trellis2/shape/sampled/decoder-latent.f32 `
  --coordinates artifacts/local/trellis2/shape/sampled/coordinates.json `
  --output artifacts/local/trellis2/shape/decoded-stage
```

`--trellis2-shape-flow-probe` and its `--negative` variant perform separate
selected-block diagnostics. `--probe-latent` is an explicitly unqualified
decoder input; keep it in a separate output directory. The source/model revisions
and measurement paths are recorded in the companion provenance ledger.
Microsoft source remains MIT with `tools/trellis2/TRELLIS-NOTICE.txt`; weights
and generated artifacts are not redistributed. No third-party CUDA extension
or new tool was installed.
