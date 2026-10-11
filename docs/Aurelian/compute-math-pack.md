# GPU compute math foundation

The built-in ComputeMath family ports the useful core of Oct Prometheus into
real Visual TypeScript sources. The current pack has two deliberately distinct
matrix paths: portable tiled **FP32 SGEMM** and optional hardware **FP16-input,
FP32-accumulator cooperative matmul**. The latter is explicit because it changes
input precision. No model weights, model loaders or external runtime are added.

| Operation | Contract |
| --- | --- |
| `Multiply` | Row-major A[M,K] × B[K,N], optional transposed B[N,K], alpha/beta, masked tails; K ≤ 4096 |
| `MultiplyCooperative` | KHR subgroup 16×16×16 tuple; packed FP16 A/B, FP32 C; M/N/K multiples of 16; K ≤ 4096 |
| `ReduceRows` | FP32 sum, maximum and max-subtracted stable softmax; width ≤ 1024 |
| `Normalize` | RMSNorm or two-pass centered LayerNorm, gamma/beta and positive epsilon; width ≤ 1024 |
| `Activate` | Stable SiLU, tanh-approximate GELU, SiLU gate, residual addition and channel bias |
| `RotatePositions` | Half-split or interleaved RoPE with explicit position offset and theta |
| `Fft` | Batched complex32 radix-2; power-of-two lengths 1–65536; forward unnormalized, inverse normalized once |
| `Convolve` | Single-image NHWC cross-correlation, symmetric zero padding and stride; explicit im2col scratch then SGEMM |
| `Attention` | QKᵀ/√D, optional causal mask, stable softmax and PV; ≤ 1024 keys, explicit score/probability scratch |

Convolution uses the conventional ML cross-correlation ordering, without
flipping the kernel. Weights are [kernelY*kernelX*inputChannels,outputChannels];
output is [outputHeight*outputWidth,outputChannels]. This establishes reusable
filtering and neural-network arithmetic before more advanced fire/smoke work.
It is not yet a fluid solver, spectral effects graph or pretrained inference API.

## Ordinary C# workflow

```csharp
using Aurelian.Graphics.Vulkan.ComputeMath;

var assets = new Aurelian.Games.GameAssets();
using var math = new VulkanComputeMath(plant, assets.ComputeShader);
using var input = math.CreateTensor(tokens, channels);
using var weights = math.CreateTensor(channels, hiddenChannels);
using var projected = math.CreateTensor(tokens, hiddenChannels);
using var activated = math.CreateTensor(tokens, hiddenChannels);

math.Upload(input, inputValues);
math.Upload(weights, weightValues);
var batch = math.CreateBatch();
batch.Multiply(input, weights, projected);
batch.Activate(projected, activated, TensorActivation.Silu);
var measurement = math.Execute(batch); // one command list, one submission/wait
var result = math.Read(activated);      // explicit optional readback
```

All tensors use device-local buffers. Upload uses the existing staging uploader;
readback is a separate explicit copy. No operation downloads intermediates or
constructs a CPU replacement result. Caller-owned tensors can be reused in
many batches. The owner caches pipelines, descriptor sets and 64-byte parameter
buffers up to the declared dispatch budget (default/max 512). K tiles, FFT
stages, reductions and transformer operations form one bounded plan submitted
on the existing plant queue. Compute barriers connect stages. GPU timing reuses
the engine timestamp mechanism and excludes upload, compilation, submission
overhead and readback. The owner is synchronous and used from one application
thread; it does not add a policy scheduler, queue, thread or background service.

Tensors permit 1–4096 rows, 1–65536 columns and at most 1,048,576 logical
elements. Each operation applies tighter shape and aliasing checks. FFT stores
interleaved real/imaginary FP32 words and uses two distinct output/scratch
allocations. Readonly inputs must be uploaded or produced before execution;
allocations are not implicitly initialized. SGEMM beta=0 avoids reading a
previous output; beta≠0 requires an initialized C. Batch operations reject
invalid shapes, ownership, disposed tensors and budget overflow with
`AUR-COMPUTE-MATH-001`. Failed operation admission does not append partial
dispatches. Vulkan/resource failures use `002`.

## Hardware cooperative path

Request negotiation when creating the plant:

```csharp
var created = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
    new VulkanPlantOptions(EnableCooperativeMatrices: true));
// Inspect created.Plant.Facts.CooperativeMatrixEnabled / Diagnostic.
// Unsupported capability preserves ordinary device creation and FP32 kernels.
```

Admission requires Vulkan 1.3, `VK_KHR_cooperative_matrix`, the exact subgroup
16×16×16 FP16/FP32 non-saturating tuple, shaderFloat16, Vulkan memory model,
computeFullSubgroups and a default 32-lane compute subgroup. The pipeline
requires full subgroups. Merely having an NVIDIA GPU is not admission evidence.
`CreateTensor(..., GpuTensorFormat.PackedFloat16)` plus `Upload` explicitly rounds
to half precision and packs low lane first. Values exceeding finite half range
are rejected. `MultiplyCooperative` rejects unsupported plants and tails;
there is no silent fallback or precision conversion inside `Multiply`.

The compiler-owned `CooperativeMatMul` intrinsic lowers the audited opaque
SPIR-V matrix operations through a real HLSL intrinsic source. It requires actual
groupId/groupIndex builtins and workgroup-uniform dimensions. Its backend uses
the same DXC/SPIR-V validation path, with Vulkan memory model and 16-bit types.
The default game host does not request this optional feature; an application
that wants this path must configure its plant explicitly.

## Visual TypeScript workgroups

```typescript
@compute
@numthreads(64, 1, 1)
function Main(@builtin(groupId) group: uint3,
    @builtin(groupIndex) lane: u32,
    @workgroup readwrite Shared: WorkgroupArray<f32, 64>,
    @binding(0) readwrite Output: StorageBuffer<f32>): void {
    Shared[lane] = Convert<f32>(lane);
    WorkgroupBarrier();
    if (lane == 0) {
        Output[group.x] = Shared[63];
    }
}
```

Shared storage is neither an inline value array nor a descriptor buffer.
It admits f32/u32 words, fixed extents and a portable 16 KiB total budget.
Builtins now include groupId/groupThreadId:uint3 and groupIndex:u32.
`WorkgroupBarrier()` is statement-only. The conservative synchronization
profile admits waits on the entry's straight-line spine, including statically
expanded blocks. A wait inside a helper, runtime branch/match/flow, or after an
earlier conditional return is rejected. Mask tail work around unconditional
waits; the compiler does not guess uniformity for arbitrary control flow.
Dynamic shared indices remain the author's responsibility; literal bounds are
checked. `Exp`, `Sin`, `Cos` and `Pow` now have typed compute intrinsic lowering.
No while loops or dynamic nested flows were added.

## Provenance and reproduction

See [the source hash ledger](compute-math-provenance.json) and
`Assets/Licenses/Prometheus-NOTICE.txt`. The port reads Oct but has no Oct build
dependency. No other repository's source was edited. The compiler intrinsic stays
in the GPL compiler boundary; the existing built-in bundle remains AGPL.

```powershell
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet build Aurelian.slnx -c Release -m:1
dotnet test Aurelian.slnx -c Release --no-build -m:1
dotnet test tests/Copeland/Copeland.TS.Tests -c Release -m:1
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --compute-math --output artifacts/local/compute-math
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --compute-math --disable-cooperative --output artifacts/local/compute-math/ordinary-device
```

The native specimen compares GPU outputs with independent CPU references:
SGEMM tails and alpha/beta, short/odd/1024-wide rows and large logits, centered
normalization, extreme activations, both RoPE layouts, batched FFT versus DFT,
65536-point round trips, padded/strided convolution, causal/unmasked attention,
and a random-weight transformer-style chain. It checks no intermediate readback,
feature-disabled device creation and named rejected ownership/shape/budget cases.
The transformer specimen is a numerical composition witness, not a loaded model.
Native qualification is on the local RTX 3070; other GPUs and browser execution
are not qualified by this pass. Numerical contracts assume finite, well-conditioned
operands; FP32 overflow and precision limits still apply to chains.

Local qualification on 2026-10-11: the Release solution build passed with no
warnings or errors on the final incremental build. All 1,309 Aurelian tests and
1,504 Copeland TS tests passed without skips, including 23 new workgroup/math
compiler cases. The native pack passed 54 numerical specimens on an RTX 3070;
the feature-disabled route and existing GPU particle proof also passed. Both
NuGet packages retained their new source/notice boundaries: all 11 ComputeMath
sources matched, and the compiler supplied its exact cooperative intrinsic
source and provenance without embedding built-in Visual TypeScript.

The 65536-point FFT round trip had maximum absolute error 2.09e-6. The
256×256×256 cooperative matmul had maximum absolute error 1.91e-5 against a CPU
reference using the same half-rounded inputs and measured 0.030 ms GPU in its
qualified dispatch. The separate FP32 256³ specimen averaged 0.108 ms over ten
warm runs, with maximum reference error 3.82e-6 in its checked run. The 15-step
resident transformer-style chain matched its FP32 CPU reference and measured
0.055 ms GPU. These small local measurements exclude compilation, transfers,
submission and readback; the FP16 and FP32 paths have different precision
contracts, and no full-model throughput claim follows from them. Warm timing
records carry a null reference-error field because they do not reread results.

The [TRELLIS.2 learned-module experiment](trellis2-vulkan-experiment.md) adds
bounded safetensors row ingress, `StreamingLinearPlan`, `LinearStreamed` and a
twelfth ComputeMath source, `StreamingLinear.v.ts`. It qualifies real checkpoint
timestep/shared-conditioning modules with explicit output-row weight reuse.
Full-model inference remains unqualified; wider reductions, online attention,
activation views and sparse 3D kernels are the next seams. FFT-based field filters or Gaussian density
filtering can use this pack; integrating them into a fire solver is separate work.
