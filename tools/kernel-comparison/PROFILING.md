# Vulkan profiling controls

For the follow-up queue admission, subgroup, independent-barrier and operating-state
experiments, see [the research report](../../docs/Aurelian/scheduling-and-subgroup-experiment.md).
`Run-Research.ps1` and `Run-ClockResearch.ps1` retain separate protocols and fresh
output roots; the latter reuses the former's exact compiled shaders.

Run the native Windows diagnostic after installing the original comparison's
Python dependencies. It requires the Vulkan SDK tools on PATH (`glslangValidator`,
`spirv-opt`, `spirv-val`, `spirv-dis`), the repo's resolved DXC, .NET 10, NVIDIA's
CUDA driver, and the existing PyTorch CUDA environment. The power-state refresh
control targets the single local RTX 3070; it is a diagnostic mode, with no CUDA
dependency added to Aurelian's production runtime.

```powershell
./tools/kernel-comparison/Run-Profiling.ps1
```

The runner refuses existing output directories. `-Output`, `-Fixtures`, `-Python`
and `-Rounds` select a fresh root, existing frozen fixtures, Python interpreter
and independent process count. The default is three rounds, with backend order
reversed in the middle round and a 20-second cooldown before each Vulkan-only
process. No clock configuration is changed.

Build the benchmark project directly, as the runner does. A full solution build
currently copies some Debug-configured compiler dependencies into the harness's
Release output folder. After solution-wide validation, rebuild
`tools/Aurelian.KernelComparison -c Release` before profiling. The measured
processes pin every DLL hash; their compiler dependencies are Release builds.

Each round measures Vulkan alone, Vulkan with a completed CUDA no-op before
each compiler sample group outside all native timing regions, and matched CUDA
C kernels. The latter use NVRTC and direct CUDA driver module/launch calls;
PyTorch supplies allocation, events and graph capture. No PyTorch tensor
operation implements the timed kernel. There is no overlapping CUDA work in
the Vulkan refresh control. Retaining an idle context alone was insufficient
to maintain P2 in exploratory runs, so the refresh control is explicit.

The six native cases are small/large bias+SiLU, large softmax, medium RMSNorm,
large GEMM and the awkward GEMM tail from the frozen manifest. The first four
compiler variants receive identical HLSL: DXC default, DXC followed by
`spirv-opt -O`, glslang HLSL, and DXC `-O0`. glslang cannot distinguish the
emitter's readonly/writable StructuredBuffer overloads, so two unused writable
parameter helpers are removed for **every** compiler variant. Canonical and
adapted HLSL are both retained. All SPIR-V passes `spirv-val`.

Separate, explicitly named shader ablations specialize all applicable scalar
parameters or hoist the stable SiLU exponential. They are not compiler swaps.
The static-parameter portable GEMM keeps the changing per-dispatch K offset
dynamic. A changed source shape must pass the binder and numerical oracle.

Each native compiler/case has five prepared plans: timestamped/untimestamped
single operations, timestamped 16-operation batches, and host-visible parameter
controls for single/16-operation batches. Device-local parameters are uploaded
once at preparation. Every plan is checked against four FP64-oracle fixture
distributions before timing. There are 60 samples with both blocking and
100-microsecond bounded polling, alternating compiler order. GPU time is the
16-operation command-buffer interval divided by 16; compute barriers remain
inside it. Ordinary wall latency and a separately instrumented CPU phase
replay are recorded independently.

The CUDA C controls use the same 256-thread, four-elements-per-lane mapping for
bias+SiLU. Stable `expf`, stable `__expf`, and hoisted `__expf` variants are
qualified separately. Single-operation graph replay measures wall latency;
external event nodes inside a 16-operation graph measure GPU time. Both graphs
are replayed after changing input values and poisoning output, for all four
distributions. Positive timestamp and complete coordinate-grid checks prevent
empty captures from qualifying.

Empty Vulkan command buffers compare the existing submitter's blocking and
polling timeline waits with direct Vulkan fence and queue-idle controls. The
CUDA floor control launches an empty kernel and synchronizes its context.
The CUDA control includes actual kernel work, so this diagnoses a latency
floor rather than providing identical GPU-work instructions.

A separate direct C Vulkan API control removes .NET and Silk.NET from the
empty-submission experiment. With Clang and the Vulkan SDK installed, run it
after the primary profiling processes finish:

```powershell
./tools/kernel-comparison/Run-NativeLatency.ps1
```

It compiles the maintained C++ specimen, then runs three fresh processes with
the same cooldown. Each records 500 samples for timeline semaphore waits,
binary fence waits and queue-idle waits. Source/executable/CSV hashes, the
compiler identity, device/queue family and initial clocks are preserved in
its own protocol. It contains no shaders or Aurelian runtime code.

`summary.json` retains per-process medians, CPU phases, raw report hashes and
clock snapshots. Raw reports include source/binary/SPIR-V/PTX provenance.
`summarize_profile.py` rejects missing, extra or duplicate coordinates, changed
sources/artifacts, failed numerical checks, non-finite timings and mismatched
compiler inputs. The checked-in report is
[Vulkan profiling results](../../docs/Aurelian/vulkan-profiling-experiment.md).

Clocks remain unmanaged, and snapshots cannot prove identical clocks during
every dispatch. CUDA refresh can also change context-switch behavior. Treat
it as a diagnostic intervention, not a production runtime dependency or a
universal SPIR-V/PTX ranking. All original comparison artifacts remain
historical snapshots of their own source revision.
