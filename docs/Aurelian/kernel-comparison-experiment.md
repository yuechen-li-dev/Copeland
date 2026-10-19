# FP32 kernel and runtime comparison — 2026-10-19

The experiment improved Aurelian substantially, but **does not support removing
PyTorch on performance grounds**. Across fifteen diagnostic cases, Aurelian's
optimized path improved GPU time by **2.98×** and ordinary single-call latency
by **1.79×** over the frozen original-shader/unfused control. All fifteen cases
improved by at least 5% on both metrics.

The stronger controls remain ahead. Tuned Inductor is **1.95× faster than the
improved Aurelian GPU path**, and its single-operation CUDA Graph replay is
**3.02× faster than Aurelian prepared replay**. Compiled PyTorch won all fifteen
cases on both comparisons. These are geometric means of per-case median ratios,
not an official KernelBench score or a full-model inference result.

## What the optimizer comparison actually shows

Compared with explicit eager formulas, tuned Inductor improved GPU execution by
**1.83×**: twelve cases improved by at least 5%, three were within 5%, and none
regressed by 5%. Ordinary, unreplayed Python calls told a different story:
compiled single-call latency regressed by about **22%** geometrically, with
eleven regressions and four improvements. That is a wrapper/submission result,
not evidence that the generated GPU computation became slower.

A follow-up captured **one operation** in a CUDA graph and compared replay
through GPU completion with prepared Vulkan. After removing the ordinary
Python-call path, tuned Inductor improved replay latency over eager CUDA Graphs
by **1.24×**, with nine improvements, six cases within 5%, and no 5% regressions.
The ordinary-call penalty therefore does not establish that the optimizer is
generally harmful. The generated Inductor source is retained for inspection.

## Concrete improvements

The maintained changes use the existing compiler and Vulkan resource owner:

- Portable SGEMM now maps adjacent lanes to adjacent columns. The original
  shader is frozen in the benchmark tool as a control.
- `MatrixKernel.Resident64` provides a 64×64 output tile, sixteen FP32
  accumulators per lane, and one full-K dispatch. The benchmark selects it
  explicitly for the large matrices. Smaller cases retain the coalesced
  portable path; no universal tile-selection claim is made.
- Visual TypeScript now supports `BoundedIndices<Max>(count)` loops with
  certified workgroup-uniform bounds. The bound is a typed VD-MIR fact. Shader
  emission retains a runtime loop instead of expanding the full K dimension.
  Divergent or shadowed counts and unsafe synchronization fail before emission.
- `TensorActivation.BiasSilu` fuses channel bias and stable SiLU in a 256-lane
  kernel with four contiguous passes and masked tails.
- `VulkanComputeMath.Prepare` records immutable dispatch/descriptors once and
  replays through the existing submitter and timeline fence. Blocking and
  explicit short-polling waits are measured separately. Existing public entry
  signatures remain available.

A smaller resident register tile did not improve this workload selection and
was removed from maintained runtime code. Its source and measurements remain
in ignored exploratory artifacts. The comparison also exposed a missing
GELU-gate reference arm in the old native proof; that proof now checks it.

Representative GPU medians, in microseconds per operation:

| Operation | Original shader/unfused | Improved Aurelian | Tuned Inductor |
| --- | ---: | ---: | ---: |
| Bias + SiLU, 1024×4096 | 422.38 | 116.87 | 81.90 |
| Softmax, 4×128 | 24.02 | 3.22 | 2.64 |
| GEMM, M=N=512, K=1024 | 1800.92 | 282.66 | 131.31 |

The GEMM improvement is **6.37×**, while its remaining GPU gap is **2.15×**.
Its K-dispatch count fell from eight to one. The remaining difference is
visible in GPU execution time after that change; it cannot be attributed solely
to Python dispatch overhead.

Representative matched single-operation replay latency, also in microseconds:

| Operation | Aurelian prepared | Tuned Inductor CUDA Graph |
| --- | ---: | ---: |
| Bias + SiLU, 4×128 | 60.15 | 17.30 |
| RMSNorm, 128×1024 | 68.60 | 18.85 |
| GEMM, M=N=512, K=1024 | 330.90 | 144.00 |

Tiny operations expose a roughly 55–65 µs Aurelian completion path versus
17–20 µs CUDA replay in these runs. This isolates a submission/completion
latency problem worth profiling. These measurements do not yet assign that
cost to individual driver, synchronization or managed-code functions.

## Protocol and qualification

Hardware was one native Windows RTX 3070, 8 GiB, driver 610.88, with a Ryzen 7
7700X. The baselines used PyTorch 2.11.0+cu128 and the community Triton Windows
3.6.0.post26 distribution. Aurelian used .NET SDK 10.0.401 and its existing
Visual TypeScript → HLSL → DXC → validated SPIR-V → Vulkan path.

The primary lane uses FP32 inputs and arithmetic: TF32 is disabled, Triton dot
products use IEEE precision, and cooperative FP16 matrices are not enabled for
performance comparison. The five families are bias/SiLU, softmax, RMSNorm,
GEMM and GEMM/bias/SiLU. Each has three timed shapes and an awkward shape
excluded from timing. Four changed-value distributions use an independent
FP64 NumPy oracle, common serialized inputs and a frozen tolerance manifest.
Held-out shapes were visible to the author; this was not a blinded model eval.

Both suites ran three independent rounds, alternating backend order, with eight
warmups, 250 ms preconditioning and forty samples per case/variant. Primary GPU
times place timestamps inside a Vulkan command list or CUDA graph containing
sixteen operations. Matched latency replays exactly one operation through GPU
completion. Vulkan additionally retrieves its timestamp query. Neither includes
upload, compilation or readback. Cold compilation figures have different
boundaries and are descriptive only.

The primary suite retained **16,200 timed samples and 4,560 correctness checks**.
The replay-latency follow-up retained another **16,200 samples and 2,160 checks**.
Artifact gates verify complete coordinate grids, manifest/source hashes and
successful numerical checks. No slower or failed case was removed.

Validation on the final implementation:

- Release `Aurelian.slnx` build: zero warnings and errors.
- Full Aurelian tests: **1,483 passed in 33 assemblies**, zero failures/skips.
- Full `Copeland.TS.Tests`: **1,730 passed**, zero failures/skips.
- Artifact-integrity negative tests: **7 passed**.
- Native Vulkan compute proof: **77 checks passed**, with Khronos validation
  enabled, including bounded-loop counts 0/1/3/512/513/uint-max, masked/transposed
  resident GEMM with alpha/beta, maximum K=8192, changed-input replay, source-plan
  snapshot stability and disposed-tensor rejection.
- Fixture generation reproduced byte-for-byte under NumPy 1.24.2 and 2.2.6.

Clocks were unmanaged, the desktop remained active, and a clock-lock attempt
was denied without changing clock settings. Primary runs retain 100 ms GPU
telemetry and per-run medians/ranges. Windows profiler event coverage was
incomplete, so complete kernel launch counts are not qualified. Source caches
and native dispatch counts remain inspectable.

This does not qualify mixed-precision peak throughput, expert-tuned Triton
limits, native Linux, full KernelBench coverage, full-model quality, training,
or matched LLM generation budgets. It establishes a reproducible improvement
loop and concrete remaining GPU and submission-latency gaps.

## Reproduce and inspect

See the [protocol and runner](../../tools/kernel-comparison/README.md) and
[small checked-in result ledger](kernel-comparison-results.json). Raw local
evidence and all-case tables are available after running:

- [Primary table](../../artifacts/local/kernel-comparison/RESULTS.md) and
  [primary summary](../../artifacts/local/kernel-comparison/summary.json).
- [Matched replay table](../../artifacts/local/kernel-comparison/replay-latency/RESULTS.md)
  and [replay summary](../../artifacts/local/kernel-comparison/replay-latency/summary.json).
- [Native proof](../../artifacts/local/kernel-comparison/native-final-qualified/evidence.json).

```powershell
./tools/kernel-comparison/Run-Comparison.ps1 -Install
artifacts/local/kernel-comparison/venv/Scripts/python.exe tools/kernel-comparison/replay_latency.py artifacts/local/kernel-comparison
```

Use a fresh output directory for another experiment; runners refuse to
overwrite completed runs. Existing local exploratory outputs are retained.
