# TRELLIS.2 real-image conditioning and full lattice integration

Status: **meaningful progression**. Complete image conditioning now works through
the real C#/Vulkan path. Occupancy generation and image-to-mesh are not complete.
The original next milestone was image -> DINO -> generated occupancy; this turn
removes the complete DINO blocker and isolates the remaining full flow/sampler
and dense decoder work. No tools or packages were installed.

## Real image -> complete DINO features

The built-in image generator produced a transparent 1254x1254 terracotta pot.
Its original is preserved; the project-bound copy and exact prompt are local:

- `artifacts/local/trellis2/occupancy/input/terracotta-pot.png`
- `artifacts/local/trellis2/occupancy/input/imagegen-prompt.txt`

`Trellis2ImageInput` implements transparent-image preprocessing in C#: bound
the image to 1024 pixels, alpha-aware Lanczos-3, foreground threshold >204,
tight square crop, black alpha composite, Lanczos-3 to 512x512, and ImageNet
RGB normalization. An opaque image requires an explicit authored mask; no
background-removal network or implicit guessed mask is used. Inputs above
4096x4096 and empty/degenerate masks produce clear errors.

`DinoV3Encoder` evaluates the supplied, digest-verified Meta ViT-L/16 checkpoint:

```text
1024 RGB patches -> learned 16x16 patch projection
  -> prepend CLS and four register tokens
  -> 24 learned transformer layers
     learned LayerNorm -> Q/K/V -> patch-only half-split 2D RoPE
       -> 16-head noncausal attention -> output projection
       -> learned channel scale -> residual
     learned LayerNorm -> 1024/4096/1024 MLP with erf GELU
       -> learned channel scale -> residual
  -> plain non-affine LayerNorm -> 1029x1024 features
```

Two easy-to-miss upstream contracts are preserved: RoPE leaves all five prefix
tokens unchanged, and TRELLIS applies plain final LayerNorm rather than DINO's
learned `norm` parameters. It retains prefix tokens in the conditioning tensor.
Inference uses FP32 storage/accumulation. `GeluErf` evaluates an error-function
approximation, distinct from TRELLIS's existing tanh GELU. It is not a claim of
mathematically exact erf or CUDA bit parity.

The checkpoint is read in bounded output-row tiles; all model arithmetic stays
on the existing Vulkan plant. There are no Python, Torch, CUDA or external-process
calls in the C# path. It reads back only the final features. Peak owned Vulkan
allocation was **83,251,232 bytes**. The final image run took **19.78 seconds**;
an earlier run with slightly different image-composite quantization took 24.48
seconds. These are exploratory single runs, not controlled throughput benchmarks.

An optional offline NumPy/Pillow oracle independently evaluates all 24 layers,
all 1029 tokens and all 1024 channels. It repacks the saved RGB input independently,
uses BLAS projections, double normalization reductions and Python `math.erf`
rather than the shader polynomial. It verifies the checkpoint digest again.
This is not a PyTorch/CUDA equivalence certificate. Existing NumPy/Pillow were
used only for this validation, never invoked by inference.

| Check | Result | Admission limit |
| --- | ---: | ---: |
| Maximum full-feature absolute error | 2.6703e-5 | 0.002 |
| Relative RMS feature error | 1.1825e-6 | 0.0002 |
| Independent normalized patch-layout error | 0 | 0 |
| Upstream Pillow preprocessing max byte error | 1 | 1 |
| Upstream Pillow preprocessing mean byte error | 7.3751e-5 | 0.001 |

Preprocessing is not byte-identical to Pillow: **58 of 786,432 channel values**
differ by one byte. Feature parity above compares the same actual runtime RGB
input, while the separate preprocessing check measures the Pillow difference.
One generated image does not qualify arbitrary images or background removal.

Runtime evidence intentionally records `IndependentReferenceQualified=false`;
the separate `reference-evidence.json` is the authority for oracle qualification.
The integration probe requires its accepted flag and matching feature SHA-256,
so an unqualified or altered feature artifact is rejected.

## Full-size learned block with actual image conditioning

`Trellis2BlockScaleProof` executes real checkpoint block 0 over the full 16-cubed
spatial lattice, with all 1029 real DINO conditioning tokens. Spatial input is
explicitly synthetic `BF16(sin(index*0.03)*0.4)`, not projected diffusion noise.
Outputs are finite; there are zero intermediate readbacks and one final read.
This probe establishes full-size integration and memory/performance behavior,
not generation or independent numerical parity of the entire full lattice.
The existing smaller learned-block C# reference still passes.

| Query tile rows | Block wall seconds | Self-attention submissions | Peak owned bytes |
| ---: | ---: | ---: | ---: |
| 16 | 20.63 | 3084 | 599,965,696 |
| 64 | 8.89 | 780 | 599,965,696 |

The revised schedule produced **bit-for-bit identical** output across all
6,291,456 values. Both self and cross attention now use up to 64 query rows.
The 2.32x observed wall improvement comes from this scheduling change, not a
different model or reduced token count. These single runs do not establish a
statistical benchmark or full-model throughput. Peak figures are actual owned
buffers including staging/parameters, excluding opaque driver/pipeline memory,
other plant users and host arrays. They are not total process VRAM.

## Reusable math capabilities and validation

- `AttentionTiled` now admits 64- and 128-wide heads, non-divisible query tails,
  and explicit FP32 output versus its preserved default BF16 output boundary.
  The score scratch remains proportional to query-tile rows times keys.
- `Scatter` copies FP32 values without BF16 rounding; `ScatterRounded` preserves
  the old behavior. Both reject invalid regions and aliasing.
- `Rope2D` handles square patch grids, explicit prefix tokens and per-head
  half-split Y/X pairs. Coordinates and aliasing are checked before dispatch.
- `ScaledResidual` handles learned channel scales using explicit branch/scale
  scratch, including negative/zero scales and in-place residual output.
- `TensorActivation.GeluErf` appends a distinct operation without changing
  existing activation enum values. The native proof's former catch-all bias
  reference was replaced with explicit cases and an unsupported-case error.

Native checks passed for 17 queries over 1029 keys with 64-wide FP32 heads
(2.60e-8 maximum error), two-head prefix-aware 2D RoPE (3.58e-7), scaled residual
(2.69e-7), and erf GELU over [-16,16] against independent double quadrature
(4.77e-7). Existing BF16/wide-reduction/full-key checks and the learned block 0
reference passed. The compute pack now passes **55 numerical specimens**,
including all pre-existing specimens and the new erf activation.

Release solution and tool builds passed with zero warnings/errors. All **1,342
Aurelian tests** across 30 assemblies and **1,511 Copeland TS tests** passed
without skips. No changes to the frozen Zig compiler or its test infrastructure
were made; the legacy Zig lanes were not run.

## Concrete next blocker: full occupancy generation

1. The local 27-tensor subset contains only block 0 and shared conditioning.
   `input_layer`, `out_layer` and blocks 1 through 29 must be admitted from the
   **2,584,426,920-byte** full sparse-structure flow checkpoint. The graph must
   select each block and compute shared modulation once per model evaluation.
2. Implement the actual 12-step `FlowEulerGuidanceIntervalSampler`: sigma_min
   1e-5, guidance 7.5, guidance rescale .7, interval [.6,1] and rescale_t 5.
   A complete Euler step must run the entire learned flow, not recycle block 0.
3. Implement the **dense** sparse-structure decoder, whose public config is
   `SparseStructureDecoder`, channels [512,128,32], latent channels 8,
   two residual blocks per resolution and two middle blocks. Its checkpoint is
   **147,591,972 bytes**. Actual source requires rank-5 Conv3D weights,
   channel LayerNorm, SiLU, residuals, FP16 torso boundaries and 3D pixel shuffle.
   The current rank-1..4 tensor reader and 2D im2col path do not cover these.
4. Threshold the decoded 64-cubed grid, max-pool occupancy to the 32-cubed 512-path
   structure, and build deterministic coordinates/neighborhood tables in C#.
   That is where sparse topology becomes relevant. Preview/export must consume
   the model's actual decoded output.

Topology maps, neighbor construction and scheduling are reasonable CPU work.
Learned sparse convolutions and matrix arithmetic are not inherently pointless
on the GPU; choose their placement with measurements once actual sparse shapes
are available. There is no occupancy preview, completed denoising schedule,
decoded shape, material synthesis or GLB generation qualified in this turn.

## Reproduce

Run from the repository root with the three user-supplied Meta files in Downloads:

```powershell
dotnet build tools/Aurelian.GraphicsProof -c Release -m:1
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --dinov3-encode --weights-dir C:/Users/yuech/Downloads `
  --image artifacts/local/trellis2/occupancy/input/terracotta-pot.png `
  --output artifacts/local/trellis2/occupancy/dinov3

# Optional offline oracle; the encoder above has no Python dependency.
python tools/trellis2/dinov3_reference.py `
  --weights C:/Users/yuech/Downloads/model.safetensors `
  --image artifacts/local/trellis2/occupancy/input/terracotta-pot.png `
  --output artifacts/local/trellis2/occupancy/dinov3

dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --trellis2-block-scale --weights-dir artifacts/local/trellis2/block/input `
  --features-dir artifacts/local/trellis2/occupancy/dinov3 `
  --output artifacts/local/trellis2/occupancy/block-scale-query64
```

The source revisions and primary references are retained in
`trellis2-image-encoder-provenance.json`. Microsoft source is MIT;
Transformers reference source is Apache-2.0. Meta weights retain their own
DINOv3 license/access terms. No checkpoint, generated image or local inference
artifact is committed or embedded in Aurelian packages. See
`tools/trellis2/TRELLIS-NOTICE.txt` and `Transformers-APACHE-2.0.txt`.
