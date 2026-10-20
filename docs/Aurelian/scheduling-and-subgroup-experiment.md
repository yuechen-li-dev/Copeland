# Vulkan scheduling, subgroup and operating-state experiment

This experiment separates three costs that were previously easy to conflate:
GPU kernel execution, host submission/completion, and the GPU operating state
selected under a particular workload history. It uses the same local RTX 3070
and driver 610.88 as the earlier [profiling experiment](vulkan-profiling-experiment.md).

The large elementwise Vulkan/CUDA gap persists in a copy-only shader. However,
the identical Vulkan shader and measured command buffer reach roughly **81 us**
after sustained Vulkan-only work, compared with about **116 us** in the initial
alternating-candidate experiment. No CUDA context, configured clock change,
compiler change, or shader change is needed for that transition. This removes
the evidence for interpreting this particular gap as inherently slower SPIR-V.
It does not explain every workload or the separate host synchronization floor.

Results and provenance are retained in [the main ledger](scheduling-research-results.json)
and [the operating-state ledger](scheduling-clock-results.json). Raw reports,
generated HLSL/SPIR-V/PTX, validation logs and telemetry remain under
`artifacts/local/scheduling-research/qualified2/` and
`artifacts/local/scheduling-clock/qualified5/`.

## Controlled shader experiments

Three fresh processes per backend alternate backend and candidate order. Each
coordinate has 40 timed samples after ten warmup passes. Vulkan validation is
disabled for performance; a separate complete validation-enabled run qualifies
the same candidates. Frozen inputs cover four distributions and the existing
FP64-derived oracle. Outputs are poisoned and every independent output is read
and checked before timing.
SiLU and reductions use that existing oracle; copy/bias/exponential ladder
references preserve FP32 input addition and evaluate the exponential on the CPU.

Both backends test copy, bias addition, `exp(-abs(x+bias))`, and stable bias/SiLU
with 32, 64, 128 and 256 threads per group/block. Each group covers 1,024 scalar
elements. Separate aligned `float4` candidates cover the same elements with
contiguous vector accesses. Shapes are explicitly specialized research controls;
the `ts-baseline` retains the prior canonical Visual TypeScript compilation path.

CUDA uses NVRTC and driver launches. PyTorch supplies storage, events and graph
capture only. Fast `__expf` and library `expf` are separate variants, with FMA
contraction disabled for CUDA. The numerical contract is FP32 within the frozen
tolerances, not bitwise identity or identical machine instructions.

GPU values below are medians of three process medians, in microseconds per
dispatch within a 16-dispatch batch. Vulkan barriers are included. These first
tables describe the **unmanaged operating states reached by this protocol**.

| Large 1024 x 4096 elementwise operation | Vulkan, 256 threads | CUDA C, 256 threads |
|---|---:|---:|
| Copy | 116.29 | 84.03 |
| Stable bias/SiLU, hoisted exponential | 116.47 | 83.39 |
| Vector copy | 115.28 | 82.97 |
| Vector bias/SiLU | 115.45 | 83.39 |

Removing the arithmetic does not remove the gap. Vectorization does not close
it either. Smaller groups are not a general remedy: they change both work per
thread and available parallelism. This points toward the memory path and
operating state before blaming the exponential implementation.

## Explicit subgroup work decomposition

The runtime queries and admits compute arithmetic subgroups of size 32 for
these local candidates. Vulkan workgroups remain the launch container; shader
code explicitly divides the work among subgroups, while hardware schedules them.

The one-warp candidate uses no shared storage or workgroup barriers. Hybrid
four/eight-warp candidates reduce within each warp, write one partial per warp,
then perform one small merge. Disassembled SPIR-V confirms zero workgroup
variables/barriers for `wave32`, one variable/barrier for hybrid RMSNorm, and
two variables/barriers for hybrid softmax.

| Operation | Existing shader | One warp | Four warps | Eight warps |
|---|---:|---:|---:|---:|
| RMSNorm 128 x 1024 | 22.57 | 39.00 | 16.11 | **13.08** |
| Softmax 1024 x 4096 | **143.10** | 295.07 | 222.62 | 167.14 |

The eight-warp RMSNorm candidate reduces measured time by **42.0%**. Removing
all workgroup coordination is counterproductive here, and the softmax
alternatives all lose. These remain explicit HLSL research candidates in
`SchedulingResearch.cs`, not newly selected production defaults or portable
subgroup guarantees. Earlier profiling measured different absolute RMSNorm
times under different power-state interventions; those are separate controls.

## Independent dispatch barriers

`PreparedComputeOptions.ElideIndependentBarriers` is an explicit runtime option.
Preparation rejects inter-dispatch read/write or write/write allocation hazards.
The initial dependency and final global dependency remain, including between
replays. The default remains the previous conservative barrier policy.

The ablation uses the **same sixteen distinct output allocations** with and
without interior barriers. All sixteen outputs are poisoned and checked across
all four distributions. Native negative controls reject RAW, WAR and WAW plans.

| Bias/SiLU, sixteen independent dispatches | Full barriers | Elided interior barriers |
|---|---:|---:|
| 4 x 128 | 2.73 | **0.52** |
| 1024 x 4096 | 116.21 | **112.27** |

The small result is amortized throughput with independent dispatches allowed to
overlap, not 0.52-us single-kernel latency. CUDA's captured single-stream graph
retains sequential kernel dependencies, so its independent-output measurement
is not a matched concurrent-graph comparison. The causal claim here is the
within-Vulkan barrier ablation, not a CUDA speedup claim.

## Queue admission and the control-theory hypothesis

The existing physical ring now supports `IComputeStreamController.AdmissionLimit`.
It can reduce admission within the capacity chosen by `Plan`; every reuse still
requires a real Vulkan fence. Existing controllers retain their selected depth
through the default implementation.

The first experimental controller uses GPU-service-time EMA and an authored
50-us host-delay estimate, requesting
`clamp(1 + ceil((50 us + preparation) / GPU service), 1, capacity)` outstanding
submissions. This is a bounded feedforward heuristic, not a qualified Smith
predictor or a controller with established stability margins.

Each scope executes 128 ordered submissions. Preparation is either zero,
25 us, or 100 us, then steps to one quarter halfway through the scope. Twelve
samples per process alternate fixed depths 1/2/4 and adaptive admission. Wall
time includes ring allocation, command preparation, submission and final drain;
output readback/checking is outside that timer.

| No injected preparation, wall us/submission | Fixed 1 | Fixed 2 | Fixed 4 | Adaptive |
|---|---:|---:|---:|---:|
| Small bias/SiLU | 87.09 | 38.60 | **30.95** | 30.98 |
| Large bias/SiLU | 204.44 | 168.38 | **163.18** | 163.46 |

Queueing clearly helps. The predictive rule does not consistently beat the best
fixed depth; at 100-us preparation on the large case it loses to fixed depth 2
(184.59 versus 171.76 us/submission). Small jobs keep the adaptive limit at four,
so that case is mostly a fixed-depth control. This rejects the first heuristic,
not control-theoretic scheduling in general.

## Operating-state counterfactual

The driver denied clock locking. A second tool therefore changes only the
**prior Vulkan workload**, then measures the same prepared 16-dispatch replay:

- three seconds of GPU idle;
- one second replaying 16-dispatch batches;
- one second replaying 512-dispatch batches.

Each fresh process repeats the modes three times with alternating order for
copy and bias/SiLU. No CUDA context is created. A scoped NVIDIA telemetry process
polls every 20 ms, and UTC measurement windows are retained for alignment.

Long-batch conditioning reaches roughly 81 us for both copy and SiLU, with
P0 / **7,201-MHz memory** observed during those windows. The original Vulkan
comparison snapshots showed 5,001 MHz; CUDA showed 7,001 MHz. The memory-clock
ratio predicts approximately `116.47 * 5001 / 7201 = 80.89 us`, close to the
observed conditioned Vulkan result.

Across three fresh processes and three within-process repetitions, long-batch
conditioned SiLU has a median of **80.98 us** (window medians 80.91-81.02 us).
Copy has a median of **81.07 us** (80.88-81.11 us). All eighteen long-conditioned
measurement windows have 7,201-MHz memory in their matched telemetry samples.

Telemetry is sampled, can lag transitions, and is not a per-dispatch clock
measurement. Short-batch conditioning sometimes reaches the faster state too;
idle measurements vary substantially. The intervention also warms caches.
The robust conclusion is that workload history and operating state can close
this particular execution gap without replacing the Vulkan shader/compiler.
A precise partition between clock policy and other warm-state effects needs a
locked-clock or equivalent operating-point control. The long conditioning work
is excluded from the timed replay and is not a proposed production speedup.

NVIDIA documents performance states and clock telemetry in its
[management interface reference](https://docs.nvidia.com/deploy/nvidia-smi/index.html).
Subgroup admission follows the [Vulkan subgroup contract](https://docs.vulkan.org/guide/latest/subgroups.html);
independent writes and final visibility follow the
[Vulkan synchronization examples](https://docs.vulkan.org/guide/latest/synchronization_examples.html).

## Reproduce and remaining boundaries

Use fresh output directories and the original frozen fixture environment:

```powershell
./tools/kernel-comparison/Run-Research.ps1 -Output artifacts/local/research-new
./tools/kernel-comparison/Run-ClockResearch.ps1 `
    -Shaders artifacts/local/research-new/run1/vulkan/shaders `
    -Output artifacts/local/clocks-new
```

The runners build Release tools, qualify native validation separately, retain
sources/binaries/artifact hashes, reject incomplete numerical/timing grids, and
repeat in fresh processes. Telemetry helpers terminate when their probe exits.
No service, persistent power configuration, or cross-repository source edit is added.

Regression commands use the repository's existing Naga installation:

```powershell
$env:AURELIAN_NAGA = (Resolve-Path artifacts/local/webgpu-tools/bin/naga.exe).Path
dotnet build Aurelian.slnx -c Release -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test Aurelian.slnx -c Release --no-build -m:1 -nr:false -p:UseSharedCompilation=false
python -m unittest discover -s tools/kernel-comparison -p 'test_*py'
dotnet build tools/Aurelian.GemmaProof -c Release -m:1 -nr:false -p:UseSharedCompilation=false
dotnet tools/Aurelian.GemmaProof/bin/Release/net10.0/Aurelian.GemmaProof.dll --kernels
dotnet tools/Aurelian.GemmaProof/bin/Release/net10.0/Aurelian.GemmaProof.dll --scheduling
```

As in the earlier profiling report, rebuild the benchmark projects directly
after solution-wide checks to restore their measured Release dependency set.

Final qualification on this checkout:

- 29,760 performance GPU timing samples, 864 queued-scope timing samples and
  4,592 numerical output checks across the qualified research runs and their
  separate validation processes;
- complete Vulkan validation runs with no validation warnings/errors, including
  native rejection of independent-barrier RAW/WAR/WAW hazards;
- full Aurelian build succeeds with 14 existing third-party font warnings;
  all **1,497 .NET tests across 33 assemblies** pass with Naga 27.0.0 configured;
- all **23 Python integrity tests** pass, plus the 78-measurement native compute
  proof, 21 Gemma kernel checks and native scheduling/lifetime proof;
- benchmark projects rebuilt after solution checks, and every measured DLL hash
  matches the restored Release output.

The initial unconfigured .NET run failed its missing-Naga prerequisite; the
configured full rerun is the qualification above. Early exploratory/failed
output directories are excluded from both accepted research ledgers.

The next useful controls are matched operating points without conditioning
overhead, a CUDA graph with independent concurrent nodes, and a controller
whose plant/error model beats these fixed-depth controls. The separate roughly
50-us Vulkan host submission/completion floor from the earlier direct C++
experiment remains an independent driver/OS scheduling question. This work
does not establish a general PTX/SPIR-V ranking or a general conclusion about
PyTorch's usefulness.
