# TRELLIS.2 on Aurelian Vulkan: learned conditioning experiment

This records the original conditioning milestone and its initial limits. The
subsequent [learned-block and local DINOv3 qualification](trellis2-block-experiment.md)
removes several blockers listed below; use that document for current scope.
The [complete real-image DINO encoder and full-lattice probe](trellis2-image-encoder-experiment.md)
are the latest qualification. They remove the image-encoder blocker; occupancy
generation remains incomplete.

Status: **meaningful progression**, not image-to-3D generation. A real learned
subgraph from Microsoft's checkpoint now executes on the existing Aurelian
Vulkan plant on the local 8 GiB RTX 3070. No generated mesh, image encoder,
denoising step or decoder is qualified by this experiment.

## What runs

The experiment loads six actual checkpoint tensors and evaluates:

```text
16 upstream sinusoidal timestep feature rows [16,256]
  -> learned affine 256 -> 1536
  -> SiLU
  -> learned affine 1536 -> 1536
  -> SiLU
  -> learned shared AdaLN affine 1536 -> 9216
```

These are the complete timestep MLP and shared modulation network in
`SparseStructureFlowModel`. The 9216 values per timestep supply six 1536-channel
conditioning vectors to the denoiser blocks. Per-block learned offsets and the
denoiser itself are outside this experiment. Outputs are checked **before** the
upstream final BF16 cast. Saved BF16 weights expand exactly into FP32; this path
does not approximate them as FP16 or quantize them. It uses ordinary FP32 Vulkan
compute and does not require cooperative-matrix support.

The reference fixture supplies upstream-style FP32 sinusoidal features at 16
timesteps spanning 0–1000, including fractional inputs. NumPy independently
evaluates the learned affine/SiLU sequence in FP64. This is numerical validation
of the defined subgraph, not comparison with an installed PyTorch/CUDA pipeline
or qualification of GPU sinusoidal feature extraction.

## Reusable ownership

- `Aurelian.Assets.Tensors.SafeTensorFile` owns local checkpoint ingress. It
  validates a bounded header, positive rank 1–4 shapes, exact byte lengths,
  contiguous nonoverlapping offsets and floating encodings F32/F16/BF16. It
  rejects duplicate keys, unsupported dtypes and nonfinite selected values with
  `AUR-TENSOR-001`. Reads select rows rather than materializing the checkpoint;
  each read is bounded to 1,048,576 values. This is deliberately a floating
  tensor subset reader, not universal safetensors support. Use one reader per
  thread; its stream-position reads are synchronous.
- `StreamingLinearPlan` owns an explicit, immutable output-row schedule.
  `VulkanComputeMath.LinearStreamed` uses one reusable device-local weight tile,
  with bias appended to each row, while input/output activations remain resident.
  `StreamingLinear.v.ts` is a real built-in shader compiled through Visual TS,
  DXC and SPIR-V validation. All K slices finish before the next weight upload.
  The plan uses the existing plant queue and buffer uploader; it adds no service,
  controller, background worker or dependency on upstream Python at runtime.
- Model-specific graph selection, downloading and the independent oracle stay
  in the research tools. Aurelian receives reusable checkpoint ingress and
  streamed affine execution, not a parallel model framework.

```csharp
var plan = new StreamingLinearPlan(inputWidth: 1536, outputWidth: 9216, tileRows: 256);
var execution = math.LinearStreamed(input, output, plan, tile =>
{
    // Return [plan.TileRows, plan.InputWidth + 1] FP32 values:
    // checkpoint output rows followed by bias; pad unused final rows with zero.
    return LoadSelectedWeightRows(tile);
});
```

Limits at this initial milestone: input width <=4096, one weight tile <=1,048,576 values,
full input/output tensors within the existing tensor shape limits, and K slices
within the owner's dispatch budget. Shape/alias/budget errors reject before
upload. A provider failure releases the temporary GPU weight buffer; outputs
may contain completed tiles and must be treated as invalid until a successful
rerun. This API is synchronous and intentionally does not promise transfer/
compute overlap. It does not retain all checkpoint weights on CPU or GPU.

## Local measurements, 2026-10-11

Selected checkpoint weights: **33,841,152 bytes**, 16,920,576 BF16 values.
Fully expanded FP32 weights would occupy **67,682,304 bytes**; that is a byte
accounting comparison, not a measured full-residency baseline. All three
schedules evaluate the entire defined subgraph and preserve intermediate
activations on the GPU. Two final readbacks check both learned module outputs.

| Output rows per weight tile | Weight uploads | Dispatches | Peak owned allocation bytes | Wall ms | GPU ms |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 16 | 768 | 66,050 | 1,595,392 | 1547.72 | 362.03 |
| 64 | 192 | 16,514 | 1,792,512 | 530.83 | 45.27 |
| 256 | 48 | 4,130 | 4,153,344 | 317.31 | 25.81 |

Maximum absolute errors versus the FP64 reference: timestep MLP **4.77e-6**,
shared conditioning **3.82e-6**, across all three schedules. The 256-row schedule
is a reasonable starting point for this GPU: it trades a few MB for fewer
uploads and submissions. These are single-run exploratory timings, not steady
state throughput claims. The earlier run measured 288 ms for the same 256-row
schedule; variance and pipeline creation costs matter.

Memory numbers come from the existing allocator telemetry, using actual Vulkan
buffer memory requirements. They include live tensor buffers, parameter slots,
upload staging and final readback staging. They exclude shader/pipeline/query
objects, opaque driver allocations, other plant users and CPU arrays. They are
**not total process VRAM**. For 16-row tiles the pre-readback peak is 1,202,304
bytes; the final conditioning readback sets the larger reported peak. GPU
timestamps exclude file reads, host conversion, transfer commands, compilation,
submission overhead and readback; wall timing includes file ingress, pipeline
creation, allocation/transfers/submission and readback, but excludes the prior
Visual TS/DXC compilation and initial input upload.

The result supports bounded weight streaming for this subgraph. It does not
prove the complete 24 GB upstream workload fits into 8 GB, or that the whole
model will run fast. Smaller allocations increase submissions and can lose much
more time than they save memory. Future scheduling must cover activations and
attention workspaces as well as weights.

## Initial audit and next exact blocker (historical)

Upstream advertises Linux/CUDA and at least 24 GB; its pipeline already has a
`low_vram` mode that moves entire models between CPU and GPU. Our opportunity is
finer-grained residency and reuse, not discovering offloading for the first time.
See the [upstream implementation](https://github.com/microsoft/TRELLIS.2/blob/75fbf0183001ed9876c8dbb35de6b68552ee08bd/trellis2/pipelines/trellis2_image_to_3d.py).

The pinned sparse-structure configuration requires a 16³ grid (**4096 tokens**),
1536 channels, 30 blocks, 12 heads of width 128, an 8192-wide FFN, Q/K RMS
normalization, 3D RoPE, shared AdaLN and cross-attention to image features.
The proof records actual named admission failures for:

1. 1536-channel LayerNorm: current normalization allows <=1024 channels.
2. 4096-key attention even with only 16 query rows: current attention allows
   <=1024 keys.
3. A full 4096×4096 score allocation: exceeds the current tensor element bound.
   Across 12 heads, just FP32 scores would occupy 768 MiB; a separately
   materialized probability matrix doubles this workspace. A streamed online
   softmax attention kernel is a better target than raising the allocation cap.
4. The 8192-input FFN output projection: current streamed linear K is <=4096.

Next milestone: **one complete real sparse-structure transformer block**, with
token/weight tiling, wider normalization, online attention, 3D RoPE, AdaLN and
the correct upstream activation/dtype contracts. Its 4096×1536 activations also
exceed the current per-tensor bound, so explicit views/strides and lifetimes
need to accompany the kernel work. Raising caps alone does not solve it.

After that: complete the flow sampler and dense sparse-structure decoder;
implement sparse-coordinate/neighborhood maps, sparse submanifold convolution,
upsampling and shape/texture VAEs; then O-Voxel mesh/material export into the
existing GLB asset path. Upstream's sparse convolution calls `flex_gemm` CUDA
operations, so our current 2D dense im2col is insufficient. Relevant
[source](https://github.com/microsoft/TRELLIS.2/blob/75fbf0183001ed9876c8dbb35de6b68552ee08bd/trellis2/modules/sparse/conv/conv_flex_gemm.py).

Image conditioning is a separate dependency: the pinned pipeline names
`facebook/dinov3-vitl16-pretrain-lvd1689m` and optional `briaai/RMBG-2.0` background
removal. The DINO model's API reports manual access gating; no credentials or
license acceptance were used in this turn. No weights for these dependencies
were fetched. RMBG has separate noncommercial/commercial terms, and should not
be assumed covered by Microsoft's MIT license. An already cut-out input image
can bypass upstream background removal. An image encoder still needs to produce
the correct conditioning features; swapping arbitrary encoders is not qualified.

## Reproduce and evidence

No new tools were installed. Fetching uses Python's standard library and the
existing NumPy. The fetch script pins model/source revisions, validates exact
HTTP 206 byte ranges, writes a local six-tensor safetensors subset and records
individual weight hashes. It never loads a pickle or executes upstream code.
This downloads approximately 34 MB of weights plus small source/config/header
files, rather than the multi-GB checkpoint collection.

```powershell
python tools/trellis2/fetch_timestep.py --output artifacts/local/trellis2/input
dotnet build tools/Aurelian.GraphicsProof -c Release -m:1
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --trellis2-timestep
```

Output: `artifacts/local/trellis2/qualification/evidence.json`, final FP32 tensor
results and `artifacts/local/trellis2/native.log`. Evidence starts rejected and
only becomes accepted after the reference checks and failure cleanup checks.
It retains `FullGenerationQualified: false` and the exact rejected denoiser
contracts. Fixture hashes are checked before running.

Release solution build: zero warnings/errors. **1,334 Aurelian tests passed**,
including 15 new ingress cases, 9 schedule cases and the new shader's real
catalog compilation. The existing 54-specimen native compute pack also passed,
including cooperative matrices. Native tail qualification checks odd K/output
dimensions, partial final tiles, multi-row batches, provider failure cleanup
and shape rejection. No compiler semantics changed in this pass.

Weights, downloaded upstream files, oracle results and logs remain under ignored
`artifacts/local/`. No checkpoint is embedded in runtime packages.
The small [provenance ledger](trellis2-provenance.json) retains exact
revisions, selected offsets and hashes. Microsoft's model card declares MIT;
the reference adapter preserves its [notice](../../tools/trellis2/TRELLIS-NOTICE.txt).
See the [model card](https://huggingface.co/microsoft/TRELLIS.2-4B) and
[source license](https://github.com/microsoft/TRELLIS.2/blob/75fbf0183001ed9876c8dbb35de6b68552ee08bd/LICENSE).
