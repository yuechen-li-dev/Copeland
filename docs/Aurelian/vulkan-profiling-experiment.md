# Vulkan profiling: submission, compiler choice and parameter placement

Native Windows profiling on the RTX 3070 found a concrete Aurelian improvement:
upload immutable prepared-batch parameters to device-local storage once, rather
than keep them in `CpuToGpu` allocations. In Vulkan-only runs this reduced small
bias/SiLU GPU time by **14.1%**, medium RMSNorm by **5.7%**, and awkward GEMM by
**7.0%**. Large workloads barely changed.

The synchronous submission floor also reproduces without Aurelian or .NET.
An empty command buffer takes about **50 µs** through the existing submitter
and **53 µs** in a separate direct C++ Vulkan program. Changing DXC to glslang
or adding SPIR-V optimization produced no consistent material GPU improvement.
These results do not establish a general SPIR-V versus PTX performance rule.

The [checked-in ledger](vulkan-profiling-results.json) retains all six cases,
compiler variants, per-process medians, CPU phases, clock snapshots and raw
report hashes. This is a custom diagnostic experiment, not a KernelBench score.

## What changed in the runtime

`VulkanComputeMath.Prepare(batch)` now uses device-local immutable parameter
blocks by default. Preparation uploads the existing 64-byte ABI through the
existing uploader, command pool and timeline fence. Replays perform no parameter
transfers. The owner, descriptor layout, shaders and synchronization authority
remain the existing compute-math path. Host-visible parameter storage is an
explicit `PreparedComputeOptions` control.

GPU timestamp instrumentation is now optional. In the small bias/SiLU case,
ordinary Vulkan-only single replay measured **63.45 µs** with instrumentation
and **59.35 µs** without it. Timestamp-free execution reports a null GPU time.
`ExecuteProfiled()` separately measures CPU validation, submit preparation,
queue submission, completion wait and query retrieval. Ordinary execution does
not collect CPU phase timings.

Device-local preparation adds initial allocation/upload work. These are warm
resident replay improvements; cold preparation is not claimed to improve.

## Where the time goes

All values are microseconds, reported as the median of three independent process
medians. Vulkan validation is disabled for performance runs.

| Empty submission and completion | Wall | Queue submit | Completion wait |
|---|---:|---:|---:|
| Aurelian timeline, blocking, Vulkan only | 50.00 | — | — |
| Direct C# Vulkan binary fence | 49.20 | — | — |
| Direct C++ Vulkan timeline | 52.90 | 7.70 | 45.10 |
| Direct C++ Vulkan binary fence | 52.80 | 7.90 | 44.40 |
| Direct C++ Vulkan queue idle | 61.70 | 6.30 | 55.40 |
| CUDA driver empty-kernel launch and context sync | 11.80 | — | — |

The C++ control contains no Aurelian, Silk.NET, managed runtime, shaders,
descriptors or timestamp queries. Its GPU and queue family match the native
benchmark. The CUDA control executes an empty kernel, whereas the Vulkan
command buffer contains no commands; this diagnoses a floor rather than
identical GPU instructions.

For a normal small Vulkan replay, the separately instrumented CPU medians were
0.1 µs tensor validation, 0.2 µs submission preparation, 6.8 µs queue submission,
57.0 µs completion wait and 0.3 µs query retrieval. Phase samples are separate
executions from ordinary wall samples, so their medians need not sum to the
ordinary wall median.

The evidence makes managed tensor validation an unlikely primary explanation
for the synchronous floor. It does not separate driver CPU work, Windows
scheduling, firmware scheduling and GPU completion within the wait interval.
Batching work before a host wait can amortize this floor; a language rewrite
alone has not demonstrated a solution to it.

## Parameter placement and compiler controls

GPU time covers a prepared 16-operation batch, including compute barriers,
divided by 16. The first table uses Vulkan alone, without the CUDA refresh
intervention.

| Case | Host-visible parameters | Device-local parameters | GPU time reduction |
|---|---:|---:|---:|
| bias/SiLU 4×128 | 3.674 | 3.157 | 14.1% |
| bias/SiLU 1024×4096 | 116.758 | 116.435 | 0.3% |
| softmax 1024×4096 | 100.723 | 100.678 | <0.1% |
| RMSNorm 128×1024 | 10.064 | 9.490 | 5.7% |
| GEMM M=512, N=512, K=1024 | 176.252 | 175.952 | 0.2% |
| GEMM M=17, N=67, K=131 | 19.360 | 18.001 | 7.0% |

The next table uses the explicit CUDA power-state refresh control and
device-local parameters. The first four columns receive identical HLSL.
The last column separately specializes scalar parameters before the canonical
Visual TypeScript binder; it is a shader ablation, not a compiler swap.

| Case | DXC | DXC + spirv-opt | glslang | DXC O0 | Static parameters |
|---|---:|---:|---:|---:|---:|
| bias/SiLU 4×128 | 1.66 | 1.67 | 1.67 | 1.67 | 1.50 |
| bias/SiLU 1024×4096 | 116.06 | 116.39 | 115.48 | 116.47 | 115.10 |
| softmax 1024×4096 | 103.71 | 103.61 | 104.21 | 105.16 | 104.20 |
| RMSNorm 128×1024 | 9.67 | 9.75 | 9.69 | 9.73 | 9.04 |
| GEMM 512×512×1024 | 178.10 | 178.12 | 179.34 | 177.77 | 161.26 |
| GEMM 17×67×131 | 18.01 | 18.17 | 17.95 | 17.94 | 16.60 |

Compiler-only alternatives are within about 1.5% of default DXC in this control.
The original HLSL and the common portability adaptation are both preserved:
glslang cannot overload readonly/writable StructuredBuffer parameter helpers,
so the two unused writable overloads are removed for every compiler variant.
Every module passes `spirv-val`; all compiler/case/plan combinations pass the
same four numerical distributions. Different SPIR-V sizes and hashes do not
establish different, or identical, driver-generated machine code.

Scalar specialization shows additional headroom in GEMM and small kernels.
The generic runtime does not silently specialize dimensions or math parameters.
The stable SiLU exponential-hoisting experiment also remains an explicit
diagnostic variant; it did not provide a large general improvement.

## CUDA comparison and the clock confound

NVRTC-compiled CUDA C implements the same stable bias/SiLU formula and the same
256-thread, four-elements-per-thread mapping. PyTorch supplies memory, event and
graph services, not the timed tensor operation. `expf`, `__expf` and a hoisted
exponential variant are separately checked. CUDA event nodes sit inside a
16-operation graph, and both single/batched graphs are checked after changing
inputs and poisoning output.

The small device-local Vulkan kernel measured **1.66 µs** in the refresh control
versus **1.79 µs** for CUDA C `expf`. The large case measured **116.06 µs** versus
**81.22 µs**. Parameter transport and driver compilation differ, and clocks
are not locked, so these numbers do not isolate intermediate instruction format.

Vulkan-only snapshots traversed P8/P3/P0, with memory clocks including 405,
5,001 and 7,201 MHz. Creating a CUDA context initially selected P2 at 1,755 MHz
SM and 7,001 MHz memory. An idle context subsequently lost that state. The
qualified intervention therefore completes a one-thread CUDA no-op before each
compiler sample group, outside all native timing regions, with no overlapping
CUDA work and no specimen-data accesses. It still does **not** guarantee fixed
SM clocks throughout long native work.

The intervention also changes first-replay wall behavior: the small timestamped
first replay measured 135.2 µs, while the following separately profiled replay's
phase medians were approximately 51 µs in total. Consequently the refresh mode
is a diagnostic intervention, not a clean end-to-end latency ranking. Use the
Vulkan-only wall controls for the instrumentation comparison above.

No CUDA dependency was introduced into the production Vulkan runtime, and no
clock or machine power setting was changed. A live desktop and unmanaged clocks
remain material limitations of the cross-API comparison.

## Qualification and remaining boundary

The main experiment accepted **132,300 timing samples and 4,272 numerical
checks**, across three rounds with process/backend order controls. The separate
C++ floor experiment adds **4,500 samples**. Integrity gates enforce complete
coordinate grids, fixture/source/artifact hashes, positive finite timings,
identical compiler inputs and changed-input graph correctness.

Final validation passed:

- Release `Aurelian.slnx` build: zero errors; the final incremental build also
  had zero warnings. The initial dependency rebuild emitted 14 existing OpenFont
  warnings.
- Full Aurelian .NET suite: **1,497 passed in 33 assemblies**, zero failures
  or skips.
- Native compute-math proof: **78 checks passed** with Khronos validation,
  including changed-input timestamp-free replay and the device-local default.
- Python artifact-integrity suite: **16 tests passed**.
- Source and binary hashes still match every measured native process;
  SPIR-V/PTX and native-control CSV hashes pass verification.

The initial full suite exposed an existing stale TinyFarm input fixture:
it pressed Space for use-selected, while the current profile binds that action
to K and Space to dodge. The fixture now uses K; production game bindings were
not changed. Its focused recheck and the full rerun passed.

Postprocessing was tightened after measurement to explicitly reject non-finite
empty-submission times. The original summary script is archived under
`qualified/postprocess-snapshots/`, matching the measurement protocol's recorded
hash. The final summary records the updated postprocessor hash. Measurement
code, binaries and timing data did not change.

After solution-wide validation, the harness was rebuilt directly in Release;
every DLL hash again matches the measured processes. The solution build can
copy Debug-configured compiler dependencies into the harness output, so the
profiling runner deliberately builds the project directly. The recorded
performance runs used the pinned Release dependency set.

Nsight Systems capture was attempted and stopped before application launch:
`Failed to register Vulkan extension JSON file. This operation requires registry writing permissions.`
The custom phase profiler and controls therefore provide the qualified evidence
here. The remaining large pointwise gap is not isolated at GPU instruction or
hardware-counter level. The wait interval's driver/scheduler breakdown is also
unresolved. The GPU intervals also include Vulkan compute barriers, so the
remaining gap is not a measurement of shader instructions alone.
The [Nsight Systems guide](https://docs.nvidia.com/nsight-systems/UserGuide/index.html)
describes Vulkan tracing; the separate
[Nsight Graphics shader profiler](https://docs.nvidia.com/nsight-graphics/UserGuide/shader-profiler.html)
supports Vulkan shader investigation.

This experiment does not qualify a universal SPIR-V penalty, optimal Triton or
cuBLAS performance, mixed precision, Linux driver behavior, full-model inference
or equal LLM generation/tuning budgets.

## Reproduce and inspect

See the [full protocol](../../tools/kernel-comparison/PROFILING.md). Use a fresh
output root for another run:

```powershell
./tools/kernel-comparison/Run-Profiling.ps1
./tools/kernel-comparison/Run-NativeLatency.ps1
```

Local evidence:

- [All-case tables](../../artifacts/local/vulkan-profile/qualified/RESULTS.md),
  [full summary](../../artifacts/local/vulkan-profile/qualified/summary.json),
  and [protocol](../../artifacts/local/vulkan-profile/qualified/protocol.json).
- [Direct C++ control protocol](../../artifacts/local/vulkan-profile/native-latency/protocol.json).
- [Checked-in compact ledger](vulkan-profiling-results.json).
- [Final qualification and hashes](../../artifacts/local/vulkan-profile/validation.json)
  and [validation-enabled native proof](../../artifacts/local/vulkan-profile/native-qualified/evidence.json).

The earlier [kernel comparison](kernel-comparison-experiment.md) remains a
historical snapshot of its frozen source revision. Its original timing numbers
do not measure the new parameter-storage default.
