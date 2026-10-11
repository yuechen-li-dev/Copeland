# TRELLIS.2 learned block and DINOv3 ingress on Vulkan

This records the earlier bounded block/patch milestone. The
[complete DINO image encoder and full-lattice integration](trellis2-image-encoder-experiment.md)
provide the latest qualification and remaining generation blockers.

Status: **meaningful progression**, not image-to-3D generation. A complete real
TRELLIS sparse-structure transformer block runs in C#/Vulkan on the local 8 GiB
RTX 3070. The supplied DINOv3 checkpoint exactly matches Meta's published digest;
its real patch embedding also runs through the same runtime. These new fetch,
loading and inference paths use no Python, Torch, CUDA or external process.
Independent C# numerical references run only for qualification, outside
inference measurements. No new tools were installed.

## Learned block

`Trellis2BlockGraph` evaluates actual block 0 checkpoint tensors:

```text
sinusoidal timestep features -> learned timestep MLP/shared AdaLN
  -> per-block learned modulation
  -> LayerNorm/shift/scale -> Q/K normalization, 3D RoPE, self attention
  -> gated residual
  -> learned LayerNorm -> cross attention -> residual
  -> LayerNorm/shift/scale -> 1536 -> 8192 -> GELU(tanh) -> 1536 FFN
  -> gated residual
```

All 27 selected tensors are actual weights: **118,856,704 bytes** (BF16).
Affine weights stream by output rows. Conditioning is FP32; torso tensor
boundaries round to BF16 with round-to-nearest-even, using FP32 storage and
accumulation. BF16 storage compression is future work. Per-head normalization
matches upstream L2 normalization with a length clamp at 1e-12, rather than
ordinary RMSNorm with a variance epsilon. RoPE uses 21 frequencies per axis
and leaves the last complex pair unchanged.

The independent C# reference implements double-accumulating affine, centered
LayerNorm, head normalization, attention and rotary equations. This is not a
PyTorch/CUDA equivalence certificate. Inputs and conditioning are deterministic
synthetic tokens, not DINO image features. Two complete block cases passed:

| Spatial tokens | Timestep | Context tokens | Relative RMS error | Maximum absolute error | Peak owned bytes | Wall ms |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 16 | 500 | 32 | 0.001179 | 4 | 6,599,168 | 2329.09 |
| 64 | 125 | 32 | 0.0001282 | 1 | 12,988,928 | 1978.50 |

Large outputs and residual cancellation make one fixed absolute tolerance
inappropriate for this BF16 block. Acceptance requires relative RMS <=0.005
and every element's absolute error <=`0.125 + 0.02 * abs(reference)`.
The initial small absolute-only check failed; final raw arrays and scale-aware
limits are retained. Primitive checks use much tighter independent limits.
Two samples do not qualify all timesteps/inputs or error across 30 blocks.

The graph performs **zero intermediate readbacks** and one final output read.
Wall timings include checkpoint reads, conversion, allocation, upload,
submission, pipeline creation and final readback; exclude prior shader
compilation, fixture preparation, CPU reference and initial activation upload.
These are single exploratory runs, not throughput benchmarks. GPU clocks/load
and caching can make the larger sample faster.

Memory uses actual Vulkan buffer allocation requirements, including staging and
parameter slots; excludes driver/pipeline/query allocations, other plant users
and CPU arrays. It is **not total process VRAM**. No full-model 8 GiB fit claim
follows from these small block samples.

## Reusable ownership and native checks

- `SafeTensorFile`: bounded F32/F16/BF16 ingress, positive rank 1–4 shapes,
  exact contiguous byte ranges, duplicate/type rejection and finite selected
  values. `ReadSlices` flattens inner axes in stored C order, without guessing
  a permutation. Each read stays <=1,048,576 values. Readers are synchronous;
  use one per thread.
- `LinearStreamed`: explicit output-row schedules, input width <=8192,
  one reusable weight buffer and resident activations. K=8192 uses the full
  512-dispatch budget. Upload and compute finish before buffer reuse. A failed
  provider releases scratch; partial outputs remain invalid until rerun.
- `AttentionTiled`: noncausal 128-wide token-major heads, <=4096 keys,
  one query tile/head at a time, FP32 probabilities and BF16 output. Scratch
  is O(queryTileRows * keys), rather than all query rows and heads. This
  materializes query-tile scores; it is **not online key softmax** or
  FlashAttention. Transfer/compute overlap is not implemented.
- Wide reductions/normalization: <=8192 channels. New checked tensor operations
  provide gather/scatter, BF16 rounding, AdaLN, packed gated residual, head
  normalization and XYZ RoPE with identity padding.
- Explicit tensor budget: the default 1,048,576 values remains; larger owners
  must opt in and allocations check device storage-buffer limits. Activation
  and tensor-map dispatches cover a second dimension beyond 65535 workgroups.
  Raising a budget does not qualify arbitrary shapes for every operation.
- Compiler-owned scalar `Bitcast<f32/u32>` checks opposite 32-bit scalar types
  and lowers to reinterpretation for BF16 shader rounding.

Native checks passed for 1536/8192-wide LayerNorm (maximum error <=3.58e-7),
stable softmax (<=9.32e-10), zero/nonzero head normalization, XYZ rotary
positions, BF16 halfway rounding, 16 queries over all 4096 keys (1.91e-6), and
4,202,496-element activation/rounding across the workgroup boundary. Invalid
shapes, aliases, coordinates and default-budget overflow reject before execution.
The existing 54-specimen compute pack and three learned conditioning streaming
schedules passed again.

Final Release solution and proof-tool builds passed with zero warnings/errors.
All **1,342 Aurelian tests** across 30 assemblies and **1,511 Copeland TS tests**
passed without skips. New checks cover bitcast lowering/invalid operands,
outer-axis reads, the 8192-input schedule and built-in shader compilation.

Sources remain real built-in `.v.ts` compiled through the existing binder,
DXC and SPIR-V validation. Model-specific graphs, downloaders and references
remain research tools. No parallel model framework, service or background
scheduler was introduced.

## Local DINOv3 files

The user-supplied model is **1,212,559,808 bytes**, SHA-256
`dcb2e45127cccbf1601e5f42fef165eea275c8e5213197e8dcf3f48822718179`.
It exactly matches Meta's published LFS digest at revision
`ea8dc2863c51be0a264bab82070e3e8836b02d51` of
`facebook/dinov3-vitl16-pretrain-lvd1689m`. Both JSON files match the published
Git blob identities. The mirror location was not supplied/audited; byte
identity with the vendor artifact was verified through public metadata.

All **415 F32 tensors** match the expected ViT-L/16 inventory and shapes:
24 layers, 1024 channels, 16 heads, a 4096-wide MLP, CLS plus four register
tokens. Rank-3 tokens and a rank-2 transformer projection sample decode through
the reader. Rank-4 OIHW patch weights load by selected output-channel slices.

The real 16x16/stride-16 RGB patch projection passed on a synthetic normalized
224x224 fixture: **196x1024 outputs**, maximum error **2.86e-6**,
**2,210,816 owned bytes**, **91.57 ms wall**, no intermediate readbacks.
This qualifies patch embedding, **not the complete encoder** or image resize.
The files stay at their supplied location; weights are not committed/embedded.
Meta's model terms are separate from Microsoft's MIT license.

## Remaining exact gaps

The HF access request was submitted but still pending at the last browser check.
Local weight availability now removes that blocker. Encoder work remains:
64-wide FP32 attention, prefix-token-aware 2D rotary positions, layer scale and
exact GELU. The existing 128-wide/BF16 attention helper cannot silently replace
those contracts. Standalone DINO config defaults to 224; TRELLIS feature-extractor
resize/resolution must come from its source rather than those defaults.

The full 4096-token learned block and all 30 blocks are unqualified. Input/output
projections and flow sampling still need their execution graph. Dense
sparse-structure decoding, sparse coordinates/neighborhoods, submanifold
convolution, upsampling, shape/material VAEs and O-Voxel export remain absent.
The current safetensors rank-4 bound cannot yet read rank-5 dense 3D convolution
weights. Dense 2D im2col is not a substitute for sparse 3D math.
Evidence keeps full generation/encoder qualification explicitly false.

## Reproduce and provenance

The new .NET fetcher validates the pinned revision, exact HTTP 206 ranges,
header digest and per-tensor/subset hashes, then atomically writes a local
standalone subset. It never executes downloaded model code. The old optional
timestep fixture generator uses offline Python/NumPy; the new paths do not.

```powershell
dotnet build tools/Aurelian.GraphicsProof -c Release -m:1
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --trellis2-fetch-block
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --trellis2-block
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --trellis2-block --tokens 64 --timestep 125 --output artifacts/local/trellis2/block/qualification64
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --dinov3-patch --weights-dir C:/Users/yuech/Downloads
```

Evidence is under ignored `artifacts/local/trellis2/block/qualification/`,
`block/qualification64/` and `dinov3/qualification/`. Each proof starts rejected
and accepts only after its numerical checks. See the
[block provenance ledger](trellis2-block-provenance.json), original
[conditioning milestone](trellis2-vulkan-experiment.md) and
[Microsoft notice](../../tools/trellis2/TRELLIS-NOTICE.txt).

Primary sources: Microsoft's pinned
[modulated block](https://github.com/microsoft/TRELLIS.2/blob/75fbf0183001ed9876c8dbb35de6b68552ee08bd/trellis2/modules/transformer/modulated.py),
[attention](https://github.com/microsoft/TRELLIS.2/blob/75fbf0183001ed9876c8dbb35de6b68552ee08bd/trellis2/modules/attention/modules.py),
[rotary positions](https://github.com/microsoft/TRELLIS.2/blob/75fbf0183001ed9876c8dbb35de6b68552ee08bd/trellis2/modules/attention/rope.py),
and Meta's [model](https://huggingface.co/facebook/dinov3-vitl16-pretrain-lvd1689m)
and [public metadata](https://huggingface.co/api/models/facebook/dinov3-vitl16-pretrain-lvd1689m?blobs=true).
