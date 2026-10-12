# TRELLIS.2 learned occupancy on C#/Vulkan

This milestone follows the qualified DINOv3 encoder. It runs the complete
1.3B sparse-structure flow, the upstream 12-step Euler/CFG schedule, and the
learned dense occupancy decoder. The output is a 64-cubed occupancy volume,
32-cubed pooled sparse coordinates and neighborhood rows, and a Vulkan-rendered
voxel preview. Shape-detail, mesh and material synthesis remain later stages.

## Owners and execution

- `Trellis2FlowModel` selects all 30 distinct learned blocks and computes shared
  timestep modulation once per complete model evaluation. It uses the existing
  `Trellis2BlockGraph` and bounded streamed checkpoint weights.
- `Trellis2EulerSampler` follows sigma_min 1e-5, 12 steps, time rescale 5,
  guidance 7.5, clean-state guidance rescale .7 and interval [.6,1]. This produces
  12 conditional and 10 unconditional evaluations. C# handles the inexpensive
  4096x8 final velocities, CFG statistics and Euler updates; transformer
  activations stay on Vulkan.
- `Trellis2ComputeSchedule` uses `AurelianAgentRuntime` and a Dominatus
  coroutine. The existing host tick resumes each named synchronous compute
  stage. Blackboard/inspection hold stage names, completed counts, costs and
  errors. This is one sequential Vulkan plant, without GPU overlap or multi-GPU
  qualification. GPU buffers are not rewindable blackboard values.
- Dominatus maps iterator exceptions to a failed node. The adapter retains the
  original exception, terminates the host loop, and disposes the stage iterator.
  The injected failure specimen verifies propagation, disposal and inspection.
- `SafeTensorFile` admits rank-five O/channel/kernel-X/Y/Z weights, retaining its
  one-million-element selected-read limit. `StreamingLinearPlan` now admits K
  through 16384, enough for 512-channel 3x3x3 convolution with bounded tiles.
- `Trellis2OccupancyDecoder` runs all 74 learned tensors: FP32 input convolution,
  FP16-boundary residual torso, two convolution/pixel-shuffle upsamplers and an
  FP32 output sequence. Channel LayerNorm uses epsilon 1e-5. Storage remains
  FP32; binary16 operation boundaries explicitly round to nearest/even,
  including subnormal and overflow behavior. Native FP16 acceleration is future
  optimization, not claimed here.
- Shared `ComputeMathBatch` volume operations implement voxel-major padded
  im2col, channel normalization, binary16 rounding, flat range copying and 3D
  pixel shuffle. Learned convolutions use streamed GPU matrix multiplication.
- `SparseVoxelGrid` owns deterministic CPU threshold/max-pool and 27-neighbor
  topology. Coordinates are XYZ sorted; missing neighbors are -1. The preview
  exports exposed voxel faces with a fixed diagnostic material. Its OBJ is an
  occupancy visualization, not the TRELLIS learned final mesh.
  Raw topology retains upstream XYZ/Z-up. The renderer/OBJ alone convert to
  Aurelian Y-up as (X,Z,-Y), matching the upstream `render_utils` camera convention.

## Dispatch reduction

The shared FP32 SGEMM and streamed linear kernels now expand eight K16 tiles
statically inside each dispatch, retaining unconditional workgroup barriers.
K128 grouping reduces command/submission overhead. Tiled attention also batches
multiple query tiles within the existing dispatch budget while reusing the same
scratch. This changes floating-point summation grouping; independent numerical
checks, rather than bitwise parity with the earlier grouping, qualify it.

The full-lattice learned-block probe fell from 8.89 seconds to about 3.8 seconds.
The complete DINO encoder took 12.15 seconds with the new kernels, with 83,248,992
bytes of owned-buffer high-water allocation. Its independent all-layer oracle
reported maximum error 1.96e-5 and relative RMS error 1.15e-6. These are individual
local probes, not a general throughput guarantee.

## Evidence boundaries

Local artifacts live under `artifacts/local/trellis2/occupancy` and remain ignored.
The provenance ledger records hashes, pinned source revisions, checkpoint
revisions, final measurements and qualification paths.

The native volume specimens check padding/corners, multiple channels, selected
voxel ranges, K128 and output tails at decoder-width K, all eight shuffle child
positions, 2D channel-normalization dispatch tails, guarded range copying and
binary16 boundary bits. Invalid shape/range operations retain named stops.

The optional NumPy oracles never run from the inference executable and import
neither Torch nor CUDA:

- `occupancy_flow_reference.py` replays all 30 blocks for both first-step
  predictions, CFG-rescale and Euler. Its declared velocity-relative-RMS bound
  is 1%. The local result was 0.736%, with latent RMS difference 0.000207. Other
  denoising steps are not implicitly covered by this one-step comparison.
- `occupancy_decoder_reference.py` independently indexes the full dense VAE and
  preserves half-rounding boundaries. Its predeclared bounds are less than 1%
  relative logit RMS error and greater than 99.5% occupancy sign agreement.

An early noisy latent decoded to an empty volume in both implementations. That
probe is retained as rejected generation evidence; matching an empty reference
is useful for debugging but is not an acceptable generated-object witness.

## Final local result and remaining numerical limit

The actual transparent flowerpot input now produces a recognizable hollow pot
through the learned pipeline: 24,960 occupied cells at 64 cubed, pooled to 5,870
at 32 cubed. The complete sampler executed 716 named stages and 22 full model
evaluations in 1,544.2 seconds. Its owned-buffer high-water allocation was
604,311,232 bytes (about 576 MiB), including staging/parameters; this is not a
measurement of total process/driver VRAM. Only final velocity tensors were read
back (22 reads). The latest decoder rerun took 5.38 seconds, with 284,040,448
owned bytes and one final logit readback. The earlier concurrent decoder run
took 8.51 seconds and produced byte-identical logits.

The independent full decoder had 0.0137% relative logit RMS error and **100%
occupancy sign agreement**. The source/source-view basis conversion was checked
against the pinned upstream Z-up camera convention. Three native renders and an
exposed-face OBJ consume the actual output; the fixed preview material does not
claim texture synthesis.

The additional final-step full-flow oracle **failed** its predeclared 1% velocity
relative-RMS bound: it measured 1.130%, with final latent RMS difference 0.00410.
Keep that failed evidence. Replacing the final Vulkan Euler prediction with the
independent CPU prediction, then decoding both with the same Vulkan decoder,
changed **zero cells at both 64 cubed and 32 cubed**. Shape robustness does not
override the failed strict numerical check. The late-step discrepancy has not
yet been isolated to individual BF16 operation boundaries; that comparison is
the concrete numerical qualification follow-up. No full 12-step independent
golden replay or production numerical equivalence is claimed.

Release solution/tool builds passed without warnings/errors. All **1,353
Aurelian tests** in 30 assemblies and **1,511 Copeland TS tests** passed, with no
skips. The 55 existing native compute specimens passed; the volume pack adds
seven numerical specimens and six named invalid-operation stops. The Dominatus
failure/disposal probe passed. An initial solution build encountered Windows
locks from the running sampler; the final build passed after it exited. Frozen
Zig tests were not run because no legacy compiler/build infrastructure changed.

This is meaningful progression toward full TRELLIS.2: the occupancy stage now
works through its real C#/Vulkan path, while the final-step strict numerical
limit and the remaining shape/mesh/material stages stay explicitly unqualified.

## Reproduce

Run from the repository root. Supply the three pinned DINOv3 files locally; the
inference command does not access account credentials or download tools.

```powershell
dotnet build tools/Aurelian.GraphicsProof -c Release -m:1
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --trellis2-fetch-occupancy

# Complete image -> DINO -> learned flow -> occupancy decoder -> sparse preview.
# This command invokes only C#/Vulkan, with CPU topology and sampler arithmetic.
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --trellis2-occupancy --encoder-weights-dir C:/Users/yuech/Downloads `
  --image artifacts/local/trellis2/occupancy/input/terracotta-pot.png `
  --output artifacts/local/trellis2/occupancy/end-to-end

# Stages can also be run separately with hash-matched feature evidence.
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --trellis2-volume-kernels
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --trellis2-sampler-math
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --trellis2-sample-structure
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --trellis2-decode-structure

# Optional offline numerical qualification, separate from inference.
python tools/trellis2/occupancy_flow_reference.py `
  --weights artifacts/local/trellis2/occupancy/weights/ss_flow_img_dit_1_3B_64_bf16.safetensors `
  --features artifacts/local/trellis2/occupancy/dinov3/features.f32 `
  --sample-dir artifacts/local/trellis2/occupancy/sampled `
  --output artifacts/local/trellis2/occupancy/flow-step0-reference
python tools/trellis2/occupancy_decoder_reference.py `
  --weights artifacts/local/trellis2/occupancy/weights/ss_dec_conv3d_16l8_fp16.safetensors `
  --latent artifacts/local/trellis2/occupancy/sampled/latent.f32 `
  --output artifacts/local/trellis2/occupancy/decoded
```

`--probe-latent` is an explicitly unqualified decoder probe. Use a separate
output directory; it does not attest completed sampling. Microsoft source is
MIT, with attribution retained in `tools/trellis2/TRELLIS-NOTICE.txt`. Meta
DINOv3 weights retain their separate terms. No weights, generated inputs or
inference outputs are embedded in engine packages or committed.

## Next isolated stage

The pinned pipeline still needs `slat_flow_img2shape_dit_1_3B_512_bf16` and
`shape_dec_next_dc_f16c32_fp16`, followed by the shape-conditioned texture flow
and texture decoder. CPU sparse maps are now available, but learned sparse
operators must be implemented and measured separately. Occupancy generation
does not qualify complete TRELLIS.2 textured asset generation.
