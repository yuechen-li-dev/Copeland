# GPT-2 small on Vulkan

The latest full runtime build took **75.71 ms** for 32 decode steps on the RTX 3070. The fresh llama.cpp control took **67.87 ms**. [Dispatch allocation and queue policy](#dispatch-allocation-and-queue-policy)

On an RTX 3070 with Vulkan driver 610.88 and a Ryzen 7 7700X, the dedicated
single-row projection increased Aurelian decode throughput from **93.1 to
304.5 tokens/s (3.27×)**. The llama.cpp Vulkan reference reached **470.9
tokens/s**. All three paths consumed the same pinned GPT-2 small F32 GGUF.
[Checkpoint](https://huggingface.co/ddh0/GPT-2-GGUF/blob/3e8b9c162842b9361969b18dfd7e8a5649caa2c0/GPT-2-f32.gguf) ·
[llama.cpp b12453](https://github.com/ggml-org/llama.cpp/releases/tag/b12453)

| Runtime | 32-token prefill median | 32-step decode median | Decode range | Decode tokens/s |
| --- | ---: | ---: | ---: | ---: |
| Aurelian, tiled projections | 12.24 ms | 343.75 ms | 341.24–346.52 ms | 93.1 |
| Aurelian, GEMV for decode | 12.30 ms | 105.08 ms | 99.89–109.55 ms | 304.5 |
| llama.cpp Vulkan | 3.97 ms | 67.96 ms | 67.42–68.77 ms | 470.9 |

The 12-layer model uses learned positions, standard causal attention and F32
K/V. Every sample evaluates a fixed 32-token prompt followed by 32 fixed
single-token inputs, produces final-row logits for each evaluation, and scans
those logits for the top token. The timed interval starts with the input
request. Initialization and two warmups per process precede measurement.
Each mode contributed nine measured samples from three fresh processes.
The llama.cpp control offloaded 13 of 13 layers to Vulkan0, used four host
threads and disabled flash attention. Its context rounds to 256 allocated
positions. Aurelian allocates 64 and attends over the populated prefix.

The GPT-2 graph keeps weights, scratch and K/V resident. Aurelian's two modes
use one submission per evaluation and the same kernels in prefill. During
decode, the projected rows switch from a 64×64 tiled matmul to dense GEMV,
including the vocabulary head. Across 32 decode evaluations, Aurelian's GPU
timestamps fell from **316.49 to 73.83 ms**. Approximately 31 ms of the
105.08 ms GEMV request remains outside those GPU intervals, including host
recording, fence and query overhead, logits transfer and the CPU scan.

All 891 measured top-token IDs matched across the 27 samples. The maximum
absolute change in the winning logit was 0.04372 between the two Aurelian
kernels and 0.14408 between the tiled path and llama.cpp Vulkan. A separate
llama.cpp CPU diagnostic agreed on all 33 top tokens. This benchmark measures
short-context FP32 inference over one fixed sequence.

## Dispatch timings and persistent staging

The follow-up timed the same FP32 decode model and positions on both Vulkan
backends. Aurelian placed timestamp queries around dispatches inside each
original submission. The b12453 reference used ggml's built-in Vulkan timing
logger. Each column reports 15 measured requests across three processes,
with two warmups per process. GPU ranges include dispatch dependencies and
timestamp overhead.
[Reference logger](https://github.com/ggml-org/llama.cpp/blob/b12453/ggml/src/ggml-vulkan/ggml-vulkan.cpp)

| GPU time over 32 decode steps | Aurelian | llama.cpp |
| --- | ---: | ---: |
| LayerNorm and affine | 14.43 ms | ≤7.46 ms |
| QKV projection, 768 → 2,304 | 9.25 ms | 7.58 ms |
| Attention output projection, 768 → 768 | 6.14 ms | 3.36 ms |
| FFN up projection, 768 → 3,072 | 11.46 ms | 10.10 ms |
| FFN down projection, 3,072 → 768 | 12.08 ms | 10.31 ms |
| Attention score, softmax and value | 9.58 ms | 7.35 ms |
| Q/K/V layout and cache writes | 5.64 ms | 3.64 ms |
| Vocabulary projection, 768 → 50,257 | 11.22 ms | 11.29 ms |
| Total instrumented GPU intervals | 81.58 ms | 62.27 ms |

The reference's normalization bound sums `NORM`, `MUL` and all remaining
`ADD` timings, including additions outside LayerNorm. The Aurelian interval
includes the final row gather. Projection ranges compare matching FP32
dimensions and counts, including the bias and residual work timed in each
range. ggml fuses most of those additions. Its attention operates over a
256-cell cache while Aurelian uses the 33–64 active cells. The vocabulary
projection is within 0.1 ms across engines in this measurement.

With timestamp profiling disabled, each staging mode ran 27 measured
requests across three processes. Both paths copied and scanned all 50,257
FP32 logits per step. All token IDs and winning logits agreed exactly between
the Aurelian staging modes.

| Readback staging | Median decode | Median time in readback | Vulkan allocations during decode |
| --- | ---: | ---: | ---: |
| One buffer per read | 102.37 ms | 14.04 ms | 32 |
| Persistent buffer | 93.33 ms | 5.76 ms | 0 |

Persistent staging saved **9.03 ms** end to end. A fresh uninstrumented
llama.cpp control took **68.49 ms**, leaving a **1.36×** decode latency gap.
The GPU logger raised the reference's host wall time, so wall comparisons
use uninstrumented runs.

Append `profile` to the Aurelian harness arguments to emit phase data, or
`persistent-staging` to retain the synchronous readback buffer. For the
reference GPU run, set `GGML_VK_PERF_LOGGER=1` and
`GGML_VK_PERF_LOGGER_FREQUENCY=1` before launching the reference harness.
Raw runs, logger output, the summarizer, and `summary.json` are under
`artifacts/local/whisper-baseline/acceleration/body-profile/`.

## Width-specialized LayerNorm and host allocations

With the same FP32 GGUF and 32+32 token workload, each wall-time mode ran in
three fresh processes with two warmups and nine measured requests per process.
The generic baseline already uses GEMV and persistent Vulkan logits staging.
The final mode also reuses one array of 50,257 floats and copies the full logits
directly into it for the CPU scan.

| Mode | Prefill | 32-step decode | Tokens/s | Readback during decode | Managed MB per decode |
| --- | ---: | ---: | ---: | ---: | ---: |
| Aurelian, generic LayerNorm | 11.92 ms | 94.61 ms | 338.2 | 5.91 ms | 15.57 |
| Aurelian, LayerNorm768 | 11.55 ms | 83.96 ms | 381.2 | 6.24 ms | 15.57 |
| Aurelian, LayerNorm768 and caller-owned logits | 11.65 ms | 79.79 ms | 401.1 | 3.68 ms | 2.65 |
| llama.cpp b12453 Vulkan | 3.94 ms | 67.57 ms | 473.5 | — | — |

Each Aurelian mode made zero warm Vulkan buffer allocations during decode.
Managed MB are decimal megabytes allocated on the inference thread over
32 steps. The two logit buffers previously allocated for every read account
for 12.9 MB of the measured decrease. All Aurelian winning logits agreed
exactly. All runtimes agreed on the 33 top IDs, with a maximum winning-logit
difference of 0.131 from llama.cpp.

In separate single-submission dispatch profiles (15 requests per mode), the
LayerNorm range fell from **14.80 to 3.33 ms**. The shader specializes the
three scans to twelve values per lane and preserves the centered-variance
reduction and affine transform. A multi-row in-place Vulkan test agreed
bit-for-bit with the generic shader. LayerNorm alone closed 39% of the
original decode wall gap in this round. With host logits reuse that reached
55%, leaving 12.21 ms over 32 steps. Prefill still takes 2.95× the reference
time at 32 tokens.

A separate three-second .NET allocation trace attached to the optimized
process after warmup. Of 864 allocation ticks, 28.2% of sampled bytes were
attributed to `KernelParameters[]` in `VulkanComputeMath.Configure`.
Other leading sites constructed dispatch records and dispatch arrays.
This trace covers complete requests, including prefill. The table reports
exact per-thread allocation counters for decode. An isolated follow-up can
put the 64-byte parameter on the stack when writing host-visible dispatch
slots and measure the change in bytes and milliseconds per request.

To reproduce the three Aurelian rows, pass `gemv persistent-staging`, then
add `width-norm`, then `host-logits` to the same benchmark executable.
Add `profile` in separate runs to capture dispatch ranges. Raw logs, samples,
allocation counters, a serial runner and checked results are under
`artifacts/local/whisper-baseline/acceleration/kernel-allocation/`.

## Dispatch allocation and queue policy

All Aurelian runs used GEMV, LayerNorm768, persistent staging and caller-owned
logits. Each mode contributed 27 requests in three fresh processes, with two
warmups each. Four separately built graphics assemblies isolated the two
runtime changes while the model workload and benchmark executable stayed the
same. The full build also writes uploads directly into staging. GPT-2 loads its
weights before timing and performs no mel upload during inference.

| Mode | 32-token prefill | 32-step decode | GPU decode interval | Managed MB per decode |
| --- | ---: | ---: | ---: | ---: |
| Previous parameter writer | 11.66 ms | 78.47 ms | 62.66 ms | 2.62 |
| Stack parameter writer alone | 11.72 ms | 79.50 ms | 63.08 ms | 1.86 |
| Full build, synchronous | 11.46 ms | 75.71 ms | 61.09 ms | 1.86 |
| Full build, retained fixed depth two | 11.49 ms | 76.13 ms | 61.02 ms | 1.89 |
| Full build, retained Dominatus | 11.57 ms | 78.27 ms | 60.84 ms | 1.94 |
| llama.cpp b12453 Vulkan | 3.99 ms | 67.87 ms | — | — |

The isolated parameter change removed **760,320 managed bytes per decode**.
Warm Vulkan allocations stayed at zero. The full build's remaining latency
gap was **7.84 ms for decode (1.12×)** and **7.48 ms for prefill (2.88×)**.
The two builds with the stack writer execute the same hot decode path.

An interleaved cross-check kept three model processes resident and allowed
one measured request at a time. The order rotated within blocks and reversed
across cycles. Across 18 matched requests per mode in two fresh process sets,
the stack-only build used 0.74 MB fewer managed bytes and took 2.41 ms longer
than the control (median of paired differences). The full build took 1.14 ms
longer. The allocation reduction reproduced. A decode latency gain from the
stack writer did not. In the follow-up allocation trace, the largest sampled
site was dispatch construction in `Gpt2Model.Project` (16.7% of sampled
inference allocation bytes).

Dominatus selected two physical slots. Both queued controls reached only one
outstanding submission, since the host reads logits after each of the 33
evaluations. Fixed depth two and Dominatus both recorded 33 completion waits.
All Aurelian builds agreed exactly on the winning logits, and llama.cpp
matched all 33 winning IDs. The largest winning-logit delta was 0.131.

The benchmark accepts `queue=fixed-two` and `queue=dominatus` after its
usual `gemv persistent-staging width-norm host-logits` arguments. Omit the
queue option for the synchronous path. Raw samples, controller observations,
assembly hashes, traces and the serial and paired drivers are under
`artifacts/local/whisper-baseline/acceleration/schedule-allocation/`.

## Run the comparison

Use an x64 Visual Studio developer PowerShell with CMake, Ninja,
the Vulkan SDK, .NET 10 and Python 3.9 or later. The release archive SHA-256
is `e74753f436de016647da61eea0103b6d7d5a32cd4a081e16e171fe4a86d38fc3`.
The runner verifies the checkpoint SHA-256.

```powershell
$run = Join-Path $PWD 'artifacts/local/whisper-baseline/acceleration'
$llama = Join-Path $run 'llama'
New-Item -ItemType Directory -Force "$llama/include" | Out-Null
curl.exe -fL 'https://github.com/ggml-org/llama.cpp/releases/download/b12453/llama-b12453-bin-win-vulkan-x64.zip' -o "$llama/llama.zip"
Expand-Archive "$llama/llama.zip" "$llama/bin"
curl.exe -fL 'https://huggingface.co/ddh0/GPT-2-GGUF/resolve/3e8b9c162842b9361969b18dfd7e8a5649caa2c0/GPT-2-f32.gguf' -o "$llama/GPT-2-f32.gguf"
foreach ($header in 'llama.h','ggml.h','ggml-cpu.h','ggml-alloc.h','ggml-backend.h','ggml-opt.h','gguf.h') {
    $folder = if ($header -eq 'llama.h') { 'include' } else { 'ggml/include' }
    curl.exe -fL "https://raw.githubusercontent.com/ggml-org/llama.cpp/b12453/$folder/$header" -o "$llama/include/$header"
}
cmake -S tools/gpt2-benchmark -B "$llama/harness" -G Ninja -DCMAKE_BUILD_TYPE=Release "-DLLAMA_INCLUDE=$llama/include" "-DLLAMA_BIN=$llama/bin"
cmake --build "$llama/harness"
dotnet build tools/Aurelian.Gpt2Benchmark -c Release
python -B tools/gpt2-benchmark/run_benchmark.py "$llama/GPT-2-f32.gguf" tools/Aurelian.Gpt2Benchmark/bin/Release/net10.0/Aurelian.Gpt2Benchmark.dll "$llama/bin/gpt2-benchmark-reference.exe" "$run/gpt2-qualified"
```

The run directory contains per-process results, Vulkan offload logs, token
agreement and latency ranges in `gpt2-qualified/summary.json`.
