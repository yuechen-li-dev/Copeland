# Aurelian kernel comparison

This is a diagnostic inference experiment and a friction test for improving
Aurelian. It is **not an official KernelBench reproduction or score**. It asks
how much advantage survives strong PyTorch controls and where direct shader or
runtime changes improve the same workloads. Results can reject the motivating
hypothesis. The suite does not test training, autograd, distributed execution,
model coverage, or overall replacement of the PyTorch ecosystem.

## Run

From the repository root on Windows, with Python 3.10 and .NET 10:

```powershell
./tools/kernel-comparison/Run-Comparison.ps1 -Install
```

The script installs PyTorch 2.11.0+cu128, NumPy 2.2.6 and the community
`triton-windows` 3.6.0.post26 in an ignored repository-local virtual environment.
No system Python packages, services or scheduled jobs are changed. The versions
are experimental constants, not a claim to cover all current PyTorch versions.
The Windows Triton port is an explicit limitation, although it keeps all primary
measurements on the same Windows driver and GPU. WSL Vulkan on this machine
uses Mesa Dozen over Direct3D12 and is deliberately excluded from the primary
comparison. Reproduce on native Linux before generalizing across operating
systems or treating Windows compiler failures as intrinsic PyTorch failures.

The runner refuses to overwrite any run directory. Use a new `-Output` for an
independent experiment. It generates the fixtures once, builds the native tool,
and runs three independent processes per backend, alternating backend order.
Do not run other GPU work or builds during qualification. The desktop is still
active; these are workstation measurements, not isolated laboratory results.

Full regression validation is separate from performance timing:

```powershell
dotnet build Aurelian.slnx -c Release -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test Aurelian.slnx -c Release --no-build -m:1 -nr:false
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --compute-math --output artifacts/local/kernel-comparison/native-regression
```

Then run the matched single-operation replay follow-up:

```powershell
artifacts/local/kernel-comparison/venv/Scripts/python.exe tools/kernel-comparison/replay_latency.py artifacts/local/kernel-comparison
```

This reuses the frozen source/fixtures, runs three new independent rounds and
captures one CUDA operation per graph. It compares GPU-completed graph replay
with prepared Vulkan, removing the ordinary Python-call path from both sides.
It preserves a separate protocol, checks, samples and tables under
`replay-latency/`. Vulkan additionally retrieves a timestamp query. Both runners
must complete before drawing conclusions about overall submission latency.
See the [qualified local report](../../docs/Aurelian/kernel-comparison-experiment.md).

## Frozen inputs and arithmetic

The manifest fixes five families: bias-plus-SiLU, row softmax, RMSNorm, GEMM,
and GEMM-plus-bias-plus-SiLU. There are three timed shapes per family and one
awkward shape used only for correctness. These are custom diagnostic workloads
chosen before timing, not selected wins from the full KernelBench dataset.
Bias-plus-SiLU omits residual addition from the initial proposal to keep the
first fusion within the existing resource-binding contract.

Four deterministic distributions cover normal, uniform, shifted and alternating
scales. NumPy evaluates independent FP64 references, then serializes FP32 inputs
and expected outputs. Both backends verify every file SHA256 before execution.
The manifest hash binds every result to the same cases, values and tolerances.
Correctness checks change values at the same resident GPU addresses; CUDA graph
replay is also checked after changes. No timing includes reference evaluation,
upload or readback. The awkward shapes are excluded from timing but visible to
the author; they are not a blinded evaluation.

The primary lane is strict FP32: PyTorch TF32 is disabled and Triton uses IEEE
dot products. Aurelian does not enable cooperative matrices in this lane.
FP16/BF16 inputs and TF32 are different arithmetic contracts and must not be
silently substituted to improve this score. This experiment does not measure
the best mixed-precision throughput available on the hardware.

## Implementations and attribution

- `existing-unfused`: existing Visual TypeScript kernels and dispatch plan;
  bias and SiLU are separate dispatches.
- `sol-fused-specialized`: agent-authored bias/SiLU fusion; row kernels reuse
  the existing algorithm with shape and operation specialization; GEMM revises
  the existing tiled kernel's lane layout for coalesced accesses. The large GEMM
  uses a new resident 64x64 register tile and one bounded full-K dispatch.
  The pre-change portable shader is frozen as `Assets/SgemmOriginal.v.ts`.
  Emitted source,
  HLSL, validated SPIR-V and disassembly are retained per case. The GEMM
  epilogue is fused bias/SiLU after GEMM, not a single GEMM/activation kernel.
- `sol-prepared-blocking`: the same optimized shaders, recorded once through
  `VulkanComputeMath.Prepare`, with a blocking completion wait.
- `sol-prepared`: the same prepared commands with an explicit polling budget
  of 100 microseconds before blocking. This trades CPU work for wait latency;
  the ordinary prepared API defaults to blocking. The polling budget is capped
  at 1 ms and counts against the total wait timeout.
- `torch-eager`: explicit PyTorch formulas. GEMM uses `@` and therefore the
  installed PyTorch library implementation rather than a handwritten naive
  CUDA matrix multiply.
- `torch-native`: idiomatic RMSNorm and `addmm` where applicable; otherwise the
  same expression as eager. This guards against an artificially weak formula.
- `torch-compile-default`: full-graph Inductor, fixed shapes, default mode.
- `torch-compile-max-autotune`: full-graph Inductor with
  `max-autotune-no-cudagraphs`. Outer graph replay is measured separately.
- `sol-triton`: agent-authored fused pointwise/reduction kernels and a
  conventional blocked GEMM with fused bias/SiLU. This is a useful language
  control, not an expert-tuned Triton upper bound.

This is one engineering session, not a controlled model leaderboard. Existing
Aurelian code is reused; generation attempts, tokens and profiling opportunities
are not matched across languages. Do not infer relative LLM intelligence or a
model-wide success rate from it. Runtime and shader improvements are permitted;
the input suite and numerical contracts remain fixed. Retain exploratory runs
when optimizing and distinguish them from final repeated qualification.

## Timing and diagnostics

Eight warmups precede forty samples. Within each backend, variants alternate or
rotate order. A 250 ms repeated-work preconditioning phase follows compilation
and correctness checks. Raw samples are retained; summaries use medians, p10/p90 and
separate per-run medians, never the fastest sample.

Single-call wall latency measures a resident operation through GPU completion.
Immediate Vulkan includes descriptor updates, command recording, submission,
waiting and timestamp retrieval; prepared replay excludes repeated recording
and descriptor updates. CUDA includes Python invocation and synchronization.
No API-independent end-to-end equivalence is asserted.

GPU execution is measured separately with native timestamps/events. The primary
GPU comparison records sixteen complete operations in one Vulkan command list
or one captured CUDA graph and divides elapsed GPU time by sixteen. CUDA timing
event nodes are recorded inside the graph, matching Vulkan's in-command-list
timestamp placement and avoiding host gaps around graph launch. This limits
host starvation. It still includes each backend's barriers and dispatch costs.
It is not pure ALU instruction time, nor a claim that CUDA graphs and Vulkan
command lists have identical scheduling semantics. Warm resident inputs are
used consistently; no deliberate L2 flush occurs.

Cold costs have different boundaries and are descriptive: Vulkan records source
binding/HLSL/DXC/SPIR-V validation, excluding Vulkan pipeline creation; Python
records first invocation, including compilation/JIT and first execution. Disk
caches may already be warm. Do not compare these numbers as equivalent compiler
latencies or include them in steady-state speedups.

CUDA medium-size profiles retain observed CUDA event names. Event coverage was
incomplete in the exploratory Windows run; these profiles do not qualify
complete kernel launch counts. Inductor's
generated Python/Triton sources remain in the local cache with hashes in the
evidence. This makes fusion claims inspectable. Vulkan allocator telemetry is
owned-buffer accounting, not total process VRAM and not comparable to PyTorch's
caching allocator totals. No cross-backend memory-efficiency claim follows.

`summary.json`, `RESULTS.md`, backend JSON, shader artifacts and logs are under
`artifacts/local/kernel-comparison/`. `summarize.py` rejects incomplete/rejected
runs, changed source hashes, mismatched manifests, missing/duplicate sample
coordinates, missing correctness checks and invalid numerical measurements.
Seven synthetic unit tests exercise that integrity gate; they are not GPU
performance evidence. Run them with:

```powershell
python -m unittest discover -s tools/kernel-comparison -p test_summary.py -v
```

The runner records source hashes, before/after GPU state and temporary
`nvidia-smi` telemetry at 100 ms intervals, stopping the sampler in a finally
block. GPU samples use Windows local timestamps and measurements also retain
UTC timestamps. Clocks are unmanaged. A manual clock-lock attempt on the local
RTX 3070 was denied; the test changed no clock settings. The fixtures generated
with NumPy 1.24.2 were reproduced byte-for-byte with the pinned 2.2.6 environment.

The report includes every timed case, including regressions.
The meaningful questions are which gains survive graph replay and compilation,
which shader/runtime changes improve Aurelian, and what concrete bottleneck
remains. A speedup over eager alone does not establish optimizer harm.

Primary methodology references:
[KernelBench timing implementation](https://github.com/ScalingIntelligence/KernelBench/blob/main/src/kernelbench/timing.py),
[PyTorch compilation modes](https://docs.pytorch.org/docs/stable/generated/torch.compile.html),
[Triton Windows compatibility](https://github.com/triton-lang/triton-windows/tree/readme).
