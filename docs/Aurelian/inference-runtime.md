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

### Queued packed compute

`VulkanComputeMath.ExecuteWithUInt32Upload` copies one packed tensor and dispatches
its consumers in one submission. The default Q4 layer binding uses this path;
`UseSeparateUploads` retains the synchronous reference for measurement. Host bytes
are consumed before return, including when the caller reuses its upload scratch.

`BeginQueuedScope(controller, forecast)` reuses the existing physical submission
ring for ordered resident batches and packed transfers. The forecast authors an
upper bound on submission count, dispatches per batch and upload bytes per slot.
Scopes cannot nest. The controller chooses one to four slots; Dominatus currently
chooses two when its authored transfer budget permits, otherwise one. Its default
limit remains 4 MiB; the measured Gemma workload explicitly supplies 64 MiB.

`Plan` selects physical capacity once. A controller can override
`AdmissionLimit(capacity)` to reduce admission dynamically within that capacity;
the default returns the full selected capacity. The ring harvests real Vulkan
fences before reuse and rejects limits outside `1..capacity`. The initial
[adaptive admission experiment](scheduling-and-subgroup-experiment.md#queue-admission-and-the-control-theory-hypothesis)
does not justify changing Dominatus's existing production policy.

```csharp
using var controller = new DominatusComputeFeedforward(64 * 1024 * 1024);
using var queue = session.Compute.BeginQueuedScope(controller,
    new ComputeStreamForecast(BatchCount: 100, DispatchesPerBatch: 16,
        UploadBytes: 64 * 1024 * 1024));
// Execute and ExecuteWithUInt32Upload enqueue ordered work here.
// Dispose a tensor when its last consumer has been submitted.
// Read, immediate Upload, Flush and scope disposal are completion boundaries.
```

A disposed tensor handle is immediately invalid. Its physical buffer is retired
at the last submitted Vulkan timeline value; descriptors, staging and timing
queries are immutable until slot completion. Host forecasts never authorize
reuse. Scope metrics expose submissions, waits, actual in-flight depth, retired
buffer peaks and separate GPU compute/copy costs. Per-call GPU cost is unavailable
while queued; read it from harvested scope metrics. Combined submission wall time
includes staging and copy work, so it cannot be compared to the older isolated
upload/execute fields without accounting for that change.

Existing streamed linear and tiled attention composite operations drain an outer
scope before using their own bounded ring. They retain their own measurements;
outer scope metrics cover only work submitted through that scope.

Dominatus inspection also compares a compute-only completion model against a
transfer-aware model trained from GPU timestamps. CPU harvest residuals remain
diagnostic and do not train GPU costs. This Smith-style forward-model experiment
has no closed-loop pacing authority: the real Gemma trials did not improve its
host-observed prediction accuracy. Static bounded admission and fences ship as
the scheduling policy. See the measured results in the Gemma document.

Research inspiration: turbo-fieldfare's bounded slot ownership and host/GPU
overlap, reviewed at commit `77e8f9c02ec5345f4d9b633961c938bbafefb757`
([system design](https://github.com/drumih/turbo-fieldfare/blob/77e8f9c02ec5345f4d9b633961c938bbafefb757/docs/SYSTEM_DESIGN.md)).
Its Swift/Metal MoE and unified-memory implementation is Apache-2.0; no code was
copied. Aurelian retains its shared Vulkan owner and discrete-GPU transfer path.
The control experiment follows the
[Smith predictor forward-model idea](https://www.mathworks.com/help/control/ug/control-of-processes-with-long-dead-time-the-smith-predictor.html),
without claiming a qualified classical Smith controller.

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

The first causal-language-model adapter now executes Gemma 4 12B Unified text
generation with a bounded cache and local packed Q4 matrices. See
[Gemma text inference](gemma4-text-inference.md) for the measured path and its
explicit numerical, context and quality limits.

This supports authored dense/sparse graphs over the current kernel pack and
F32/F16/BF16 safetensors. It is not a universal importer or an already-qualified
Llama runtime. The Q4B32 kernel, supported Gemma BPE profile, selected embedding
lookup and bounded causal cache now have witnesses; GGUF, other quantized formats,
other tokenizer profiles and fast general causal generation remain future work.
New models provide explicit contracts/layouts, C# graph code and numerical fixtures
while reusing the resource/scheduling owners.

Extraction does not certify TRELLIS's full upstream sampler or repair its prototype
mesh. Weights stay local and are not distributed. The existing shader-bundle license
boundary remains unchanged; model adapters carry their source attribution and
Apache/MIT notices, while Meta DINO weights retain separate weight/access terms.
