# Aurelian inference foundation

New applications can run explicit C# model graphs without referencing a game or
the graphics proof executable. The engine owns device resources, compute kernels,
checked weight bindings and scheduling. Python/NumPy remains an offline oracle;
inference does not invoke Python, Torch or CUDA.

## Ownership

| Owner | Responsibility |
|---|---|
| `Aurelian.Graphics.Vulkan.ComputeMath` | GPU tensors, kernels, allocation/dispatch limits, attention tiling, physical submission rings and Vulkan completion |
| `Aurelian.Assets.Tensors` | Format-neutral `ICheckpointWeights` metadata/read contract and the implemented `SafeTensorFile` provider |
| `Aurelian.Shaders.Builtins` | Engine source catalog and shared `BuiltinComputeShaderCache`, used by games and inference |
| `Aurelian.Inference` | Headless sessions, authored checkpoint contracts, layer bindings, learned linear operations and named stage contracts |
| `Aurelian.Inference.Dominatus` | Reusable stage execution, inspection and bounded feedforward/shadow completion control |
| `Aurelian.Inference.Trellis2` | Pinned DINO encoder, TRELLIS flow and dense/sparse decoder graphs |
| Proof tools | Image preparation, pinned downloads, sampling artifacts, preview/export and independent numerical oracles |

The existing Vulkan plant remains the queue/allocation owner. This extraction
adds no parallel GPU backend, shader compiler or state-machine engine. Core
inference does not depend on Dominatus; its adapter is optional. The model library
has no reference back to games or the proof tool. `Aurelian.Inference.slnx` builds
the foundation and standalone proof without the game/Aetheris integration solution.

## Write an application

Reference `Aurelian.Inference`, optionally `Aurelian.Inference.Dominatus` and a
specific model adapter. A learned projection uses ordinary C#:

```csharp
using Aurelian.Assets.Tensors;
using Aurelian.Inference;
using Aurelian.Inference.Dominatus;

using var session = VulkanInferenceSession.Create(new()
{
    ApplicationName = "My model",
    MaximumTensorElements = 4_194_304,
});
using var weights = new SafeTensorFile("model.safetensors");
using var controller = new DominatusComputeFeedforward();
session.Compute.StreamController = controller;
CheckpointContract.Validate(weights,
    new ModelTensorRequirement("projection.weight", [128, 64]));

var layers = new VulkanModelLayers(session.Compute, weights);
using var input = session.Compute.CreateTensor(16, 64);
session.Compute.Upload(input, new float[16 * 64]);
using var output = layers.Linear(input, "projection.weight",
    rounding: TensorRounding.BFloat16);
float[] result = session.Compute.Read(output); // Explicit output boundary.
```

Bias is an optional explicit tensor name. Weight order is output/input; higher
rank outer slices require `flattenInnerAxes: true` and preserve stored order.
`CheckpointLinearLayer` exposes the same bounded plans/loaders for convolution
im2col. DINO and TRELLIS dense/convolution operators now share this binding code.
No architecture or channel layout is guessed from filenames or dimensions.

A model can yield `ComputeStage` actions from a C# iterator and execute them with
`DominatusComputeSchedule`, or supply another `IComputeSchedule` implementation.
Typed measurements and optional `Observe`/`Log` callbacks expose costs. Libraries
do not print to the console; applications own log format and artifact paths.
The complete small example is `tools/Aurelian.InferenceProof/Program.cs`.

## Budgets, precision and lifetimes

`Create` owns a plant; `Attach` borrows an existing one. Both own their compute
resources. Returned layer tensors belong to callers and support ordinary `using`.
A layer failure releases its output, including observer exceptions after native
execution. Session options author tensor/dispatch budgets, validation, attention
tile preference and cooperative-linear policy. Existing device buffer limits and
shape checks remain authoritative.

Selected checkpoint reads contain at most 1,048,576 values; partial weight tiles
are padded explicitly. Providers return exactly the selected finite FP32 values
in stored order. Execution serializes reads; the safetensors file is not a
concurrent shared-stream reader. Format-specific offsets stay in safetensors
metadata, separate from the generic checkpoint descriptor.

`TensorRounding.Float32`, `Float16` and `BFloat16` author model operation boundaries.
Cooperative FP16/F32 arithmetic is a separate opt-in policy with the existing
audited device-tuple/fallback checks. It is not selected silently to accelerate
an unqualified model.

Dominatus retains its bounded depth-one/two policy and advisory shadow predictions.
Only physical Vulkan completion releases slots. Inspection is bounded. Schedules
can run again, clear prior measurements, preserve original failures and dispose
model iterators on failure/cancellation. Cancellation is checked between named
stages and does not preempt a GPU batch. The default named-stage budget is 4,096;
longer jobs can author their limit explicitly.

## Qualified native paths

The standalone two-layer MLP runs seven 3-channel input rows through a bias-free
5-channel projection, SiLU and a biased 2-channel head. F32/F16/BF16 weights and
rounding paths pass independent CPU comparisons with maximum absolute error
**2.98e-8**. Each model reads only its final output. The actual dependency manifest
is checked for absence of game/TRELLIS libraries. Native failures restore allocation
bytes and leave the owner reusable. Disposing a borrowed session leaves its plant
usable by the original session.

The extracted TRELLIS full positive prediction and seven block snapshots match
their previous Vulkan artifacts byte for byte. Sparse decoder prefix features,
child logits and parent/slot mappings match byte for byte. Complete DINO features
match the existing `dinov3-k128` artifact; the older pre-K128 artifact differs for
the previously documented kernel change. Dense occupancy output also matches.
The complete six-channel material decoder was rerun through GLB compilation,
the normal importer and Vulkan rendering: features, voxel attributes, coordinates,
GLB bytes and the rendered preview all match the pre-refactor artifacts exactly.

```powershell
dotnet build Aurelian.Inference.slnx -c Release -m:1
dotnet test Aurelian.Inference.slnx -c Release -m:1 --no-build
dotnet run --project tools/Aurelian.InferenceProof -c Release -- artifacts/local/inference-foundation
```

All nine focused tests pass, covering contracts, optional bias, explicit flattening,
partial tiles, broken provider payloads, schedule reuse, iterator cleanup,
cancellation, stage budgets and controller disposal. The final full Aurelian build
has zero warnings/errors; all **1,378 tests in 31 assemblies** pass with no skips.
Native streamed/sparse kernel regressions also pass, including controller failure
cleanup and restored memory budgets. Evidence is retained under ignored
`artifacts/local/` directories.

## Current scope

This supports authored dense/sparse graphs over the current kernel pack and
F32/F16/BF16 safetensors. It is not a universal importer or an already-qualified
Llama runtime. GGUF/quantized kernels, tokenizers, embedding lookup, persistent KV
caches and optimized causal generation still need bounded implementations and
their own witnesses. New models provide explicit contracts/layouts, C# graph code
and numerical fixtures while reusing the resource/scheduling owners.

Extraction does not certify TRELLIS's full upstream sampler or repair its prototype
mesh. Weights stay local and are not distributed. The existing shader-bundle license
boundary remains unchanged; model adapters carry their source attribution and
Apache/MIT notices, while Meta DINO weights retain separate weight/access terms.
