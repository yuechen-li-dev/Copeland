# Whisper Tiny.en on Vulkan

On JFK, the 32-key attention, cooperative convolution and packed vocabulary path took **33.23 ms** versus **36.99 ms** for the previous combined Aurelian path and **29.31 ms** for whisper.cpp flash. [Final comparison and numerical agreement](#wider-attention-packed-vocabulary-and-cooperative-convolution)

Copeland commit `64cba528` plus the benchmark changes was measured against
[whisper.cpp v1.9.6](https://github.com/ggml-org/whisper.cpp/releases/tag/v1.9.6)
on Windows with an NVIDIA GeForce RTX 3070, driver 610.88, and a Ryzen 7 7700X.
Both runtimes selected the RTX 3070 Vulkan backend. The reference uses the
native whisper.cpp encoder and decoder via its C API and four host threads.

| Runtime | Total median | Total range | Encode + cross KV | Prompt to first token | Subsequent decode |
| --- | ---: | ---: | ---: | ---: | ---: |
| Aurelian, per-head submissions | 452.03 ms | 446.28–456.86 ms | 56.49 ms | 30.28 ms | 366.25 ms |
| Aurelian, batched heads | 426.78 ms | 409.70–456.13 ms | 56.89 ms | 28.31 ms | 338.84 ms |
| whisper.cpp, standard attention | 36.82 ms | 36.25–37.57 ms | 6.96 ms | 2.33 ms | 27.43 ms |
| whisper.cpp, flash attention | 29.20 ms | 28.81–29.49 ms | 3.83 ms | 1.97 ms | 23.22 ms |

Each row summarizes nine timed requests across three fresh processes, with two
warmups per process and permuted mode order. Each request consumed the same
3,000 × 80 FP32 log-mel window from the 11-second upstream
[`jfk.wav` sample](https://github.com/ggml-org/whisper.cpp/blob/v1.9.6/samples/jfk.wav).
On both runtimes, prompt processing used two separate single-token calls for
start-of-transcript and no-timestamps. Greedy selection considered text IDs and
end-of-text. All 36 timed requests produced the same 24 text tokens plus
end-of-text. Timing starts with supplying the precomputed features, includes the
complete model and host token selection, and excludes WAV decoding, feature
extraction, device/model initialization and warmups. Phase medians are computed
independently. Aurelian expands the shared F16 checkpoint weights to resident
FP32 storage and uses its existing BF16-rounded grouped self-attention kernel
in the decoder. The two runtimes can choose different arithmetic and kernel
layouts even with the same checkpoint and token sequence.

## Wider attention, packed vocabulary and cooperative convolution

These A/Bs start from the previous combined path: FP32 softmax, cooperative 16-key encoder attention, 2 × 2 projection tile reuse and fused projection epilogues. The separate FP32 two-slot control remains the numerical reference. Each request uses the same model, mel, greedy selector and full FP32 host readbacks as the earlier JFK campaigns.

| Change in isolation | Paired request delta | Faster pairs | GPU time for changed operation, before → after |
| --- | ---: | ---: | ---: |
| Runtime-selected 32-key tile | +0.21 ms | 24/60 | attention 3.58 → 3.81 ms |
| Runtime-selected 64-key tile | +0.31 ms | 19/60 | attention 3.58 → 3.65 ms |
| Packed vocabulary weights | −2.36 ms | 57/60 | vocabulary 4.78 → 2.46 ms |
| Cooperative convolution | −0.91 ms | 48/60 | front end 1.52 → 0.34 ms |

Paired changes are medians of individual request differences, across three resident process groups with rotating execution order. Only one client executed at a time. The GPU column uses separate timestamped runs (15 measured requests per variant). Convolution's cooperative multiply cost 0.138 ms across the two layers, activation packing 0.038 ms, and output cropping 0.037 ms. The front-end total includes im2col and pointwise operations.

A second attention A/B closed the key width at shader compilation, unrolled the per-tile loops and removed one barrier per tile. Against the original 16-key kernel, 32 keys saved a median 0.29 ms (25/36 faster, all three process-group medians favorable) and 64 keys saved 0.05 ms (20/36). GPU attention intervals were 3.57, 3.15 and 3.32 ms for 16, 32 and 64 keys. The 32-key variant therefore went into the combined trial.

The first combined rerun pooled 27 unprofiled requests per mode. It measured 35.02 ms for the 32-key combination, 35.34 ms for the previous combined path, 39.48 ms for the FP32 two-slot control and 29.58 ms for flash. In a separate rotating-order paired run, the combination saved 3.97 ms against the previous path (21/24 faster). This difference between paired and standalone measurements is why the final comparison also repeats the 16-key and 32-key combinations together.

The final build shares one typed GEMV shader between FP32 and packed weights. A fresh five-way comparison included both tile widths with the two larger wins enabled. Each row pools nine timed requests in each of three fresh processes after two warmups. Phase medians are computed independently. The encode timer includes scheduling cross-cache preparation; a pending encoder batch can complete in the prompt phase.

| Final build | Full request | Encode + cross-cache | Prompt | Subsequent decode |
| --- | ---: | ---: | ---: | ---: |
| Aurelian FP32 two-slot control | 42.23 ms | 13.45 ms | 4.25 ms | 24.43 ms |
| Previous combined path | 36.99 ms | 8.33 ms | 3.10 ms | 25.43 ms |
| Packed vocabulary + cooperative convolution, 16 keys | 33.79 ms | 7.09 ms | 2.88 ms | 23.71 ms |
| Packed vocabulary + cooperative convolution, 32 keys | **33.23 ms** | **7.12 ms** | **2.89 ms** | **23.25 ms** |
| whisper.cpp v1.9.6 flash | **29.31 ms** | **3.85 ms** | **1.96 ms** | **23.39 ms** |

The fastest standalone median was 3.76 ms below the previous combined path and 3.92 ms above flash. That closes 49% of this cohort's previous gap. It remains 0.99 ms above the 10%-of-flash target. The two campaigns' standalone deltas differed substantially, so the rotating-order paired comparison is also reported. The FP32 control and all variants used the same 25-token JFK continuation.

On the final build the 32-key combination beat the previous path in **36/36 paired requests**, by a median **3.42 ms**. Switching just the combined path from 16 to 32 keys saved 0.29 ms (25/36, three favorable process-group medians). This small wall-time gain was more consistent than the instrumented attention interval: the final profile recorded 3.44 ms at 32 keys and 3.28 ms at 16, reversing the isolated profile's ordering. The vocabulary multiply measured 2.46 ms GPU and the convolution front end 0.38 ms. The final wall-time gap is concentrated in encode and prompt; subsequent decode was within 0.14 ms of flash on this clip.

The 32-key combination produced the same 25 tokens and matched the FP32 control's full-vocabulary top prediction at all 26 evaluations. Relative to the 576,000 FP32 encoder outputs its RMS / maximum absolute error was 0.000515 / 0.007075. Across all 1,348,464 logits it was 0.007117 / 0.028679. Packing vocabulary weights alone reproduced every encoder and logit value from the previous combined path. These comparisons cover one clip and one GPU.

The packed GEMV keeps the same FP32 input, accumulation order and output. The benchmark retains the FP32 token embedding for gathers and uploads a second, packed copy for the vocabulary projection. That isolates the multiplication A/B at a cost of 39,831,552 additional resident bytes. Convolution uses the previous im2col layout, half-precision weights packed at load, and 2 × 2 cooperative multiplication. The wider attention kernels assign two lanes to each softmax row. They share storage for Q and probabilities, and for scores and the output product.

To reproduce the operation toggles on the `fused persistent-read persistent-upload host-logits batch-cross queue=fixed-two projection-epilogues reuse-projections` benchmark, choose one of `cooperative-attention` (16), `attention-32` or `attention-64`. Append `packed-vocab` or `cooperative-convolution` independently. Use `profile-gpu` for per-kernel intervals or `validate capture` for numerical comparisons. Reference and Aurelian timing runs use neither diagnostic. [Runner commands](#reproducing-a-profile)

All Aurelian final runs used 66 compute submissions and 26 full logits readbacks per request with zero warm Vulkan allocations. The native compute proof passed the odd-width packed GEMV and both attention tile tails. The shared shader reproduced the previous GPT-2 capture across 1,658,481 FP32 logits. Release tests passed for Aurelian Core (1,458), Copeland TS (2,362) and inference (19). Raw evidence is in `artifacts/local/whisper-baseline/acceleration/whisper-gap-ab/`, including `summary.json`, `manifest.json` and `validation.json`.

## Tiled cooperative encoder attention and projections

The control is the FP32 two-slot path with fused projection epilogues. Every Aurelian variant retains full host logits, the same decoder, persistent upload/readback and batched decoder cross-attention. Runs use the same RTX 3070, Tiny.en checkpoint, JFK mel window and two-token prompt as the flash reference.

A fresh final comparison used nine timed requests after two warmups in each of three processes per runtime. The combined kernels saved **5.81 ms (13.9%)** and closed **46%** of the FP32 control's gap to flash. The remaining gap is **6.73 ms**. Each Aurelian request used 66 compute submissions, 26 full logits readbacks and zero warm Vulkan allocations. The combined path increased the dispatch count from 3,033 to 3,105 through activation packing and cropping. Median managed allocation on the request thread rose from 752,320 to 870,520 bytes.

| Final median, 27 requests per row | Total | Encode + cross-cache | Prompt | Subsequent decode |
| --- | ---: | ---: | ---: | ---: |
| FP32 two-slot control | 41.89 ms | 12.63 ms | 4.26 ms | 24.34 ms |
| Both encoder changes | **36.09 ms** | **8.02 ms** | **2.96 ms** | **24.92 ms** |
| whisper.cpp flash | **29.36 ms** | **3.85 ms** | **1.97 ms** | **23.33 ms** |

| Isolated A/B | Request | Encode + cross-cache | Four attention kernels, GPU | 32 projection multiplies, GPU |
| --- | ---: | ---: | ---: | ---: |
| FP32 control | 42.82 ms | 13.21 ms | 5.71 ms | 7.60 ms |
| Tiled cooperative attention | 41.34 ms | 11.35 ms | 3.22 ms | 7.25 ms |
| Direct packed projections, one output tile | 40.68 ms | 11.12 ms | 4.99 ms | 4.14 ms |
| Direct packed projections, 2 × 2 output tiles | 38.43 ms | 10.33 ms | 5.71 ms | 2.24 ms |
| Cooperative attention and 2 × 2 projections | 35.84 ms | 8.15 ms | 3.34 ms | 2.51 ms |

Request and encode timings pool 21 requests per variant across three fresh, reordered processes. GPU timings come from 15 separately instrumented requests per variant. Each GPU cell is the median of the per-request sum of its dispatch intervals. The 32 projections include eight cross-cache products. The encode wall timer ends after scheduling cross-cache preparation, so the first decoder call can absorb some outstanding encoder work.

The attention workgroup holds 16 queries and streams 16 keys at a time for one 64-channel head. Both QK and probability × V use cooperative matrix multiply with FP32 accumulators. Running maxima, exponentiation, the denominator, output rescaling and final division use FP32. Q, K and V use binary16 inputs. The denominator sums the unrounded FP32 weights. The probability × V product reads those weights rounded to binary16 after exponentiation. The kernel masks the final partial key tile. Packing attention inputs added 0.13 ms GPU in the combined run.

The single-tile projection control loads fragments directly from packed buffers. The 2 × 2 variant reuses two left and two right fragments to update four output tiles per reduction step. Weight layout, activation packing, cropping and arithmetic are identical between those variants. In isolation, reuse saved 1.91 ms GPU over direct single-tile multiplication. Packing plus cropping cost another 1.01 ms with reuse. Cooperative matrix strides for packed buffers are measured in 32-bit words. [KHR cooperative matrix specification](https://github.khronos.org/SPIRV-Registry/extensions/KHR/SPV_KHR_cooperative_matrix.html)

In 60 interleaved request pairs with all five variants resident, attention alone saved a median 1.77 ms and projections with reuse saved 3.56 ms against FP32 (58/60 faster for each). Reuse alone saved 1.86 ms against the direct single-tile projection (59/60). The combined path saved 5.52 ms against FP32 (57/60).

| Validation capture versus FP32 | Encoder RMS | Encoder max abs | Logits RMS | Logits max abs | Vocabulary top-1 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Cooperative attention | 0.000248 | 0.004273 | 0.004278 | 0.021042 | 26/26 |
| Projections with reuse | 0.000398 | 0.017993 | 0.003117 | 0.015831 | 26/26 |
| Combined | 0.000457 | 0.017156 | 0.006320 | 0.025929 | 26/26 |

The captures include 576,000 encoder values and 1,348,464 logits. Every variant returned the same 25 tokens including end-of-text. Single-tile and reused projections produced identical encoder and logit values. These checks cover one English clip. The native Vulkan proof also passed a 19-query, 71-key, two-head attention case against a CPU reference (maximum absolute error 0.0000774), and tested direct and reused projection tiles. Release validation passed 1,453 Aurelian Core tests, 2,362 Copeland TS tests and 19 inference tests.

To reproduce, use the runner and control arguments below, including `projection-epilogues`. Append `cooperative-attention` for the attention A/B, `direct-projections` for direct single-tile loads or `reuse-projections` for tiled projection reuse. Append both `cooperative-attention reuse-projections` for the combined path. Use separate runs with `profile-gpu` and with `validate capture`, then compare captures using `tools/whisper-benchmark/compare_precision.py`. The raw samples, paired run order, capture comparisons, asset and binary hashes are in `artifacts/local/whisper-baseline/acceleration/encoder-tiles/` (`summary.json` and `manifest.json`). The fast paths require the enabled Vulkan subgroup FP16-input/FP32-accumulator cooperative tuple. [Runner commands](#reproducing-a-profile)

## CPU and GPU profiling on the two-slot path

The JFK benchmark runs two prompt evaluations and continues through end-of-text. For this experiment all Aurelian variants used fixed queue depth two, batched decoder cross-attention, persistent staging and full host logits. The timed requests exclude profiling, capture and Vulkan validation. The FP32 control refers to the existing two-slot path, including its usual decoder rounding. whisper.cpp v1.9.6 flash reads the same checkpoint and can compute with different precision.

| Warm JFK path | Total | Encode + cross KV | Prompt | Subsequent decode |
| --- | ---: | ---: | ---: | ---: |
| Archived Aurelian FP32 | 47.96 ms | 12.53 ms | 4.86 ms | 28.88 ms |
| Shared LayerNorm and dispatch bindings | 44.07 ms | 13.48 ms | 4.44 ms | 25.98 ms |
| Shared runtime plus fused projection epilogues | **42.58 ms** | **13.48 ms** | **4.26 ms** | **24.77 ms** |
| Fused epilogues plus native packed-half encoder attention | 45.64 ms | 16.48 ms | 4.25 ms | 23.78 ms |
| whisper.cpp flash | **29.40 ms** | **3.87 ms** | **1.99 ms** | **23.41 ms** |

Each figure is an independently pooled median over 21 timed requests from three fresh processes with two warmups each. The FP32 changes saved 5.37 ms (11.2%) and closed 29% of the original 18.56 ms gap. The combined path's three process medians were 41.24, 42.95 and 42.58 ms.

Separate interleaved tests kept the models resident, rotated request order, and ran one client at a time. For LayerNorm and binding reuse, the control forced generic normalization and rewrote every binding on the same build. Specializing LayerNorm alone saved 4.98 ms (58/60 pairs faster). Binding reuse alone saved 0.96 ms (52/60). The combined shared runtime saved 5.12 ms against the archived binary (59/60). Adding bias + GELU and bias + residual fusion then saved another 1.34 ms (34/36). The full path saved 6.64 ms in paired requests against the archived binary (34/36).

Fusion removed 428 dispatches per request, reducing 3,461 to 3,033 while preserving the 66 compute submissions and 26 full-vocabulary readbacks. Warm Vulkan allocations were zero. Median managed allocation on the inference thread fell from 876,048 to 737,152 bytes per request. Both FP32 changes together reproduced all 576,000 encoder outputs, 1,348,464 logits and 25 token IDs exactly. With packed encoder attention, all 26 top-1 logits still agreed on JFK. The maximum absolute change was 0.004064 in encoder outputs and 0.025326 in logits. The native and software half conversions produced identical captures.

### Where the remaining time goes

| Matched work | Aurelian GPU | whisper.cpp flash GPU |
| --- | ---: | ---: |
| Four encoder attention operations | 5.41 ms | 0.57 ms |
| Encoder block Q/K/V, attention output and MLP matrix products | 6.19 ms | 1.01 ms |

The Aurelian figures are medians of per-request operation sums over 15 timestamped requests. The reference figures use seven warmed requests from its Vulkan graph logger. The projection row covers 24 products on each side, excluding convolution and cross-cache construction. Aurelian uses FP32 resident matrices and the reference selected F16 matrix kernels. Profiling adds overhead, and independently measured GPU intervals do not sum to a wall-time prediction.

Specializing the 312 decoder-block LayerNorm dispatches cut their GPU time from 4.74 to 1.26 ms. In the separate fusion A/B the decoder-block activation and epilogue kernels went from 3.26 to 2.46 ms. The optimized run still spends 4.78 ms GPU in vocabulary projection across 26 evaluations. On the CPU, timeline waits occupied 87.2% of the original and 86.0% of the optimized inference thread's sampled in-request time (about three seconds each, including blocked time). The optimized uninstrumented wall run measured 2.84 ms recording commands, 0.72 ms submitting, and 34.99 ms waiting for completion per request.

A separate attention-only profile measured 12.45 ms GPU for the old software-unpack attention, 8.36 ms for native unpack and 4.76 ms for FP32. Q/K/V packing took 0.121 ms in both packed variants. A further matched A/B split the existing cooperative projection path into its component dispatches. Across all 32 encoder and cross-cache projections, FP32 resident multiplication cost 7.58 ms GPU. With prepacked half weights, the cooperative multiplies alone cost 13.84 ms, activation packing cost 0.45 ms and output cropping cost 0.58 ms. Eliminating both extra passes would still leave the multiply slower on this GPU. The matched unprofiled requests took 42.02 ms for FP32 and 49.58 ms for cooperative projections. The half-projection path matched the transcript and all 26 top-1 logits.

The next projection experiment is to reuse each loaded operand tile across multiple output tiles. The current shader produces one 16 × 16 output tile per workgroup and stages both operands through shared memory on each 16-element reduction step. For attention, the next experiment is a tiled cooperative QK and probability × V kernel, keeping FP32 softmax and the existing output comparison. The two candidates can be tested independently.

The Release Aurelian Core suite passed 1,447 tests, the inference solution passed 19 and the Copeland TS suite passed 2,362 under .NET SDK 10.0.404. The native Vulkan compute proof passed the width-384 and width-768 LayerNorm paths, the fused epilogues and the packed attention tail cases.

The raw requests, paired order, sampled stacks, shader timings, capture comparisons and binary/asset hashes are in `artifacts/local/whisper-baseline/acceleration/whisper-runtime-profile/` (`summary.json` and `manifest.json`).

### Reproducing a profile

From the repository root with the baseline assets and Release build from [Reproduce on Windows](#reproduce-on-windows), use the same control arguments for timing and diagnostics.

```powershell
$assets = 'artifacts/local/whisper-baseline'
$runner = 'tools/Aurelian.WhisperBenchmark/bin/Release/net10.0/Aurelian.WhisperBenchmark.dll'
$inputs = @("$assets/ggml-tiny.en.bin", "$assets/qualified/input.mel.f32")
$control = @('fused', 'persistent-read', 'persistent-upload', 'host-logits', 'batch-cross', 'queue=fixed-two')
dotnet $runner @inputs "$assets/fp32-wall.json" 7 @control projection-epilogues
dotnet $runner @inputs "$assets/fp32-gpu.json" 5 @control projection-epilogues profile-gpu
dotnet $runner @inputs "$assets/half-encoder.json" 7 @control projection-epilogues half-attention profile-attention
& "$assets/w/bin/whisper-benchmark-reference.exe" @inputs "$assets/flash-wall.json" 7 flash
```

Omit `projection-epilogues` to measure separate bias, GELU and residual dispatches. Add `general-norm` to force the generic LayerNorm kernel or `rewrite-bindings` to force descriptor and parameter writes. Each switch can be varied independently. `profile-gpu` returns GPU intervals grouped by model operation and shader after the existing queue completions. The queue also reports host command recording, submission and completion-wait milliseconds. The host wait overlaps GPU execution. For a CPU-side call-stack trace, attach the .NET sample profiler to a warmed 100-request run. For the native GPU graph breakdown, set `GGML_VK_PERF_LOGGER=1` on a separate flash run.

## Scheduling evidence

| Aurelian path | Compute submissions and synchronous waits | Dispatches | Measured GPU compute | Decoder cross-attention wall / GPU |
| --- | ---: | ---: | ---: | ---: |
| Per head | 814 | 16,721 | 161.55 ms | 237.53 / 56.52 ms |
| Batched heads | 278 | 16,721 | 162.97 ms | 204.58 / 56.17 ms |

Batched heads cut median end-to-end time by **5.6%**. Decoder cross-attention
accounted for **53%** of per-head wall time and **67%** of dispatches. Its
submissions fell from **624 to 104**, with the same **11,232** dispatches,
and its wall time fell by 33 ms. This is a controlled change to command-buffer
grouping. The benchmark uses the same kernel calls, barriers between dispatches,
tensor shapes and attention scratch. Per-head submissions are the production
default used here.

Aurelian remains **12.3× slower** than whisper.cpp standard attention with
the original policy and **11.6× slower** after batching. The batched run spends
about 205 ms in decoder cross-attention, including 56 ms within GPU compute
timestamp intervals. That residual includes tensor allocation, dispatch
construction, parameter writes, descriptor updates, command recording and
submission/completion overhead in one combined interval. The current decoder
gathers each head's K and V from the resident cross cache on every token.
Each head then dispatches a score multiply, softmax,
12 chunks for the value multiply over 1,500 keys, and a scatter, in addition to
the gathers. This explains why grouping the same dispatches still leaves
substantial work. The 26 vocabulary readbacks account for about 10 ms per
request. Measured GPU compute alone also exceeds the reference's wall time,
which makes the GPU work decomposition a separate part of the gap.

## Where the inference thread spends its time

Two fresh `dotnet-trace` captures sampled the batched-head path on the same RTX
3070. Each process completed two warmups and 15 measured requests. A separate
untraced process ran the same workload between captures. All 45 timed requests
matched the reference token IDs. Median wall times were 427.9 and 422.5 ms in
the captures and 417.1 ms in the control. Decoder cross-attention GPU time was
56.0–56.2 ms in all three processes.

| Sampled main-thread stack | Whole request, capture 1 / 2 | Within decoder cross-attention, capture 1 / 2 |
| --- | ---: | ---: |
| Raw Vulkan allocator, allocate | 14.9% / 15.6% | 18.8% / 19.3% |
| Raw Vulkan allocator, free | 31.7% / 32.3% | 41.9% / 42.0% |
| Vulkan timeline fence wait | 46.1% / 44.9% | 32.1% / 32.2% |
| Other stacks | 7.4% / 7.2% | 7.2% / 6.4% |

The percentages measure sampled elapsed thread time, including time blocked
inside native calls. The fence wait overlaps the GPU timestamps. Each capture
uses its final five seconds through the last observed `Transcribe` frame.
Samples under `Evaluate → Attend` identify decoder cross-attention. Each
interval counts once within a column. Nsight Systems required registry write
access to register its Vulkan layer, so driver internals and GPU hardware
counters remain unresolved.

In the synchronous path each one-query cross-attention call creates and
disposes six scratch tensors. Four decoder layers across 26 steps generate 624
scratch allocations and 624 frees. Reusing one workspace across these sequential
calls provides a direct experiment with the same dispatches and arithmetic.
The sampled allocator share gives the size of the interval to investigate.
Actual savings require measuring the changed workload.

To repeat one capture after building the Release benchmark, run:

```powershell
$run = 'artifacts/local/whisper-baseline'
$profile = Join-Path $run 'profile'
New-Item -ItemType Directory -Force $profile | Out-Null
dotnet-trace collect --providers Microsoft-DotNETCore-SampleProfiler --format Speedscope --output "$profile/capture.nettrace" -- dotnet tools/Aurelian.WhisperBenchmark/bin/Release/net10.0/Aurelian.WhisperBenchmark.dll "$run/ggml-tiny.en.bin" "$run/qualified/input.mel.f32" "$profile/capture.json" 15 batched
```

The measured captures, summarization script and `host-profile-summary.json`
are under `artifacts/local/whisper-baseline/profile/`.

## Acceleration experiments

The four Aurelian modes below also ran against two freshly measured
whisper.cpp controls. Each mode had nine timed requests across three
processes after two warmups per process. All 54 requests produced the same
25 token IDs, including end-of-text.

| Cumulative Aurelian mode | Median request | Min–max request | Vulkan allocations per request | Cross-attention wall / GPU | Cross-attention dispatches |
| --- | ---: | ---: | ---: | ---: | ---: |
| Batched heads | 421.59 ms | 414.70–465.22 ms | 1,011 | 202.35 / 56.10 ms | 11,232 |
| Reuse cross-attention scratch | 299.58 ms | 289.63–324.51 ms | 387 | 73.67 / 55.62 ms | 11,232 |
| Read K/V directly | 236.55 ms | 221.71–253.43 ms | 387 | 12.85 / 4.35 ms | 312 |
| Use dense GEMV for one-row projections | 183.06 ms | 169.23–218.34 ms | 387 | 11.56 / 4.35 ms | 312 |

Scratch reuse removed all 624 hot-path allocations identified by the profile
and cut total latency by **29%** at the same dispatch and submission counts.
Direct cache attention uses one score dispatch, one softmax and one weighted
value dispatch per layer and token. It removed 97% of decoder cross-attention
dispatches and cut cross-attention GPU time by **12.8×** versus reusable tiled
attention. GEMV cut decoder-block GPU time from 59.01 to 13.58 ms and the
logits projection from 13.85 to 5.21 ms, including the final normalization
in each logits measurement.

Combined, the changes reduced median latency by **56.6% (2.30×)**. The
repeated whisper.cpp controls measured 37.19 ms with standard attention and
29.69 ms with flash attention. With all three changes Aurelian spent 61.78 ms
in encoding and cross-cache preparation, 9.69 ms on the two prompt steps,
and 112.34 ms on subsequent decode, using independently measured medians.

Run the reproduction command below with `--acceleration` and a fresh output
directory to repeat these six modes. Raw results are under
`artifacts/local/whisper-baseline/acceleration/whisper-qualified/`.

## Reprofile after retaining encoder and decoder scratch

The `gemv-scratch` benchmark mode retains fixed-shape encoder, encoder
attention, and decoder scratch across requests, including self-attention
score and probability buffers keyed by the active cache length. Each mode
contributed 21 timed requests from three fresh processes. The runner
checked the full output token sequence and operation dispatch and
submission counts. Scratch retention increased the peak of owned Vulkan
allocations by 1.2 MB.

| Aurelian mode | Median request | Encode + cross KV | Subsequent decode | Warm Vulkan allocations |
| --- | ---: | ---: | ---: | ---: |
| GEMV | 170.90 ms | 57.95 ms | 104.26 ms | 387 |
| GEMV with scratch reuse | 91.34 ms | 38.62 ms | 48.28 ms | 27 |

The retained workspace removed 360 allocations and cut request latency by
**46.6%**. The remaining allocations correspond to audio staging and 26
logits readbacks. Stack sampling on a separate 48-request warm run in each
mode put allocator calls outside logits readback at **42.3% → 0.07%** of
inference-thread time. The allocation count comes from Vulkan telemetry.
The percentages come from the last three seconds of the sampled thread
trace and include time blocked inside native calls.

Aurelian's encoder tiled attention now accounts for **24.46 ms** of GPU
intervals. The whisper.cpp Vulkan logger reported **3.32 ms** for the
standard score, softmax and value kernels and **0.64 ms** for fused flash
attention across the four layers. Aurelian's range includes the tiled
head gathers and scatters, while the reference kernels use F16 operands.
The uninstrumented whisper.cpp total medians were 37.00 ms with standard
attention and 30.38 ms with flash attention. The updated Aurelian path
took **3.01×** as long as the flash control.

Run the Aurelian harness with `gemv` and `gemv-scratch` on the same mel
fixture, or pass each mode to the `dotnet-trace collect` command above.
Raw benchmark results and both traces are in
`artifacts/local/whisper-baseline/acceleration/body-profile/`.

## Fused encoder attention and transfer allocation

Each mode ran 21 timed requests across three fresh processes, with two
warmups per process and varied mode order. Each run used the same Tiny.en
F16 checkpoint and precomputed JFK mel fixture. All timed requests produced
the same 25 token IDs including end-of-text. Each Aurelian request copied
the full logits to the host 26 times.

| Mode, cumulative changes | End to end | Encode + cross KV | Subsequent decode | Warm Vulkan allocations | Managed MB per request |
| --- | ---: | ---: | ---: | ---: | ---: |
| Aurelian with retained scratch | 93.14 ms | 38.61 ms | 49.78 ms | 27 | 13.82 |
| Fused FP32 encoder attention | 68.77 ms | 16.08 ms | 48.09 ms | 27 | 12.99 |
| Persistent logits readback staging | 66.23 ms | 16.53 ms | 43.52 ms | 1 | 12.98 |
| Persistent mel upload staging | 61.13 ms | 15.70 ms | 40.86 ms | 0 | 12.97 |
| Caller-owned host logits | **58.87 ms** | **16.08 ms** | **39.17 ms** | **0** | **2.18** |
| whisper.cpp v1.9.6 standard | 37.46 ms | 6.98 ms | 27.88 ms | — | — |
| whisper.cpp v1.9.6 flash | 29.19 ms | 3.83 ms | 23.29 ms | — | — |

A workgroup streams 32 keys at a time for 16 queries from one 64-channel head.
It updates a running softmax maximum, denominator and weighted value sum in
FP32. The four layers use **12 dispatches**, including two KV packing
dispatches per layer, down from **2,352**. The full encoder-attention GPU
interval dropped from **24.36 to 5.15 ms**. The comparison includes packing.
The flash reference uses F16 operands. The fused kernel alone closed 38% of
the gap to flash in this round.

Across the 26 reads, median readback time fell from **10.32 to 3.69 ms**
with persistent GPU staging. Median mel upload time fell from **0.90 to
0.42 ms** when its buffer was retained. Timing the complete request gave
a larger 5.11 ms change on the upload step. The measured upload accounts
for about 0.48 ms of that change, and the other phases varied across
processes. Writing logits directly into a caller-owned array further cut
the readback median to **1.85 ms** and saved 10.79 MB of managed allocation
per request. These measurements use decimal MB on the inference thread.

The combined paths closed 54% of the original gap to flash and 62% of the
gap to standard attention. The final run still issued 260 decoder compute
submissions over 26 evaluations, including 104 for cross-attention. Its
decoder blocks took 22.91 ms wall versus 13.15 ms of GPU intervals, and
cross-attention took 10.25 ms wall versus 4.30 ms of GPU intervals. Those
wall intervals include recording, submission and waiting.

A three-second .NET allocation trace after warmup attributed **43.9%** of
sampled bytes to the mel upload's temporary `byte[]` and **14.4%** to
`KernelParameters[]` in dispatch setup (638 allocation ticks). The upload
copies a 3,000 × 80 FP32 input into a 960,000-byte managed array before writing
mapped staging. A direct span write can test the cost of that copy while
preserving input validation and the GPU transfer. A separate experiment can
append each layer's three cross-attention dispatches to the decoder batch and
measure how much the 104 synchronous submissions cost at zero warm Vulkan
allocations.

Run the Aurelian harness with `gemv-scratch` and then `fused`. Append
`persistent-read`, `persistent-upload` and `host-logits` cumulatively for
the allocation comparisons. Raw results, process medians, logs, the serial
runner and the summarizer are under
`artifacts/local/whisper-baseline/acceleration/kernel-allocation/`.

## Decoder scheduling and allocation policy

The control includes fused FP32 encoder attention, retained scratch, persistent
transfer staging and caller-owned host logits. Each isolated experiment changes
one variable from that control. Each mode contributed 21 timed requests in
three fresh processes with two warmups. The same RTX 3070 ran all processes
serially. Each Aurelian request executed 3,461 dispatches and read logits 26
times. Both reference modes and every Aurelian mode returned all 25 JFK tokens.

| Synchronous mode | Request | Encode + cross KV | Subsequent decode | Managed MB per request | Compute submissions |
| --- | ---: | ---: | ---: | ---: | ---: |
| Previous optimized path | 61.52 ms | 16.60 ms | 40.66 ms | 2.18 | 274 |
| Direct mel staging alone | 58.01 ms | 15.75 ms | 38.86 ms | 1.23 | 274 |
| Stack dispatch parameters alone | 58.67 ms | 15.81 ms | 39.58 ms | 1.89 | 274 |
| Batched decoder cross-attention alone | 49.92 ms | 15.90 ms | 30.55 ms | 2.04 | 66 |
| All three changes | 52.27 ms | 16.67 ms | 31.96 ms | 0.77 | 66 |
| whisper.cpp v1.9.6 standard | 37.23 ms | 6.96 ms | 27.77 ms | — | — |
| whisper.cpp v1.9.6 flash | 28.95 ms | 3.86 ms | 23.11 ms | — | — |

Direct staging writes the validated 960,000-byte FP32 mel span to the mapped
upload buffer. The upload interval fell from 0.409 to 0.278 ms. The larger
change in median request time includes variation in other phases. Dispatch
setup writes the 64-byte parameter value from the stack. Batching records
the existing cross-attention dispatches into the decoder body command buffer.
The four layers and the separate logits submission now use two compute
submissions per evaluation, down from ten. All synchronous modes had zero
warm Vulkan buffer allocations.

For a second measurement, seven model processes held their allocations across
requests. The driver admitted one request at a time in rotating order outside
the model timers. Two independent process sets yielded 42 matched requests
per mode. The table reports median within-block changes. This controls for
drift between whole process runs. Co-resident GPU contexts make this a
separate experiment from the isolated wall-time comparison above.

| Change from synchronous control | Paired request change | Paired managed allocation change |
| --- | ---: | ---: |
| Direct mel staging alone | +0.30 ms | −0.96 MB |
| Stack dispatch parameters alone | +0.13 ms | −0.30 MB |
| Batched decoder cross-attention alone | −10.09 ms | −0.15 MB |
| All three | −10.08 ms | −1.42 MB |

The paired upload interval fell by 0.14 ms with the direct write. The two
batching repeat sets saved 10.04 and 10.18 ms per request. The remaining
managed allocation trace was led by construction of dispatch records in
`Activate` (11.9%) and the model's `Project` (11.4%).

| Queue policy, all three changes | Request | Managed MB per request | Warm Vulkan buffer allocations | Peak outstanding |
| --- | ---: | ---: | ---: | ---: |
| Retained fixed depth one | 48.81 ms | 0.84 | 0 | 1 |
| Retained fixed depth two | 47.54 ms | 0.83 | 0 | 2 |
| Retained Dominatus | 49.94 ms | 0.95 | 0 | 2 |
| Per-request Dominatus | 62.65 ms | 1.04 | 140 | 2 |

The fixed controls and `DominatusComputeFeedforward` use the same Vulkan
submission ring and fence-based retirement. The resident graph forecasts
zero queued transfer bytes. Dominatus chose depth two and observed each
submission and completion. The benchmark owns the ring lifetime. Retaining
the ring removed 140 buffer allocations per warm request and saved 12.71 ms
against a fresh ring with the same controller. Queued operator wall intervals
include completion at the next host read. GPU intervals for those modes are
reported at the queue level after harvest.

The paired check put the retained fixed two-slot queue 2.32 ms ahead of the
synchronous combined path. Dominatus took 0.17 ms longer and allocated
0.092 MB more managed memory per request than the matched fixed two-slot
queue. Both controllers used the same GPU graph.

The fixed two-slot path left **18.59 ms (1.64×)** to flash and **10.31 ms
(1.28×)** to standard. On the synchronous path, the remaining encoder gap
to flash was 12.81 ms and the subsequent decode gap was 8.85 ms. The
synchronous GPU intervals summed to 38.97 ms per request.

Use `fused persistent-read persistent-upload host-logits batch-cross` for
the combined run. Append `queue=fixed-one`, `queue=fixed-two`,
`queue=dominatus` or `queue=dominatus-per-request` for the policy comparisons.
The upload and parameter changes are in the runtime. The four controlled
assemblies, their hashes, command drivers, raw samples and traces are under
`artifacts/local/whisper-baseline/acceleration/schedule-allocation/`.

## Encoder precision and decoder submissions

All Aurelian latency variants use the retained two-slot queue, pooled scratch, direct mel staging, batched decoder cross-attention and full host logits. Each standalone row pools seven requests from each of three fresh processes after two warmups. The paired column uses request-matched differences in two resident, interleaved runs.

| Isolated change | Request, ms | GPU, ms | Compute submissions | Paired request change, ms |
| --- | ---: | ---: | ---: | ---: |
| FP32 control | 49.17 | 39.46 | 66 | +0.00 |
| Packed FP16 projections | 54.11 | 46.14 | 66 | +7.13 |
| Packed FP16 attention operands | 54.67 | 44.78 | 66 | +7.62 |
| Decoder body and logits in one submission | 46.69 | 37.96 | 40 | -0.17 |

The projection variant retains transposed FP16 weights, packs activations for each projection, pads 1,500 rows to 1,504, and crops the FP32 cooperative-matrix result. It covers encoder Q/K/V, output and MLP projections plus decoder cross-cache K/V preparation. The attention variant packs Q/K/V for each block using the existing FP32 softmax and FP32 accumulation. Convolution and the decoder body use FP32.

| Fresh comparison | Request, ms |
| --- | ---: |
| Aurelian FP32 two-slot control | 48.36 |
| Aurelian with joined logits | 46.86 |
| whisper.cpp standard | 36.53 |
| whisper.cpp flash | 29.02 |

Each Aurelian request made 26 full-logit readbacks and zero warm Vulkan allocations. Joining the decoder and logits removed 26 compute submissions. Its two request-matched runs moved wall time by +0.44 and -1.10 ms respectively. The FP32 two-slot path remains the control. A separate synchronous diagnostic localized the FP16 attention cost to the four encoder attention batches, which rose from 5.55 to 13.80 ms GPU including packing. With cooperative projections, the encoder output and MLP batches rose from 5.36 to 10.01 ms. The group timers include conversion, multiply and output handling. The precision captures use separate validation-enabled runs and are excluded from every latency median.

| FP16 change | Encoder RMS / max absolute error | Logits RMS / max absolute error | Full-vocabulary top-1 agreement |
| --- | ---: | ---: | ---: |
| Projections | 0.000398 / 0.01799 | 0.003117 / 0.01583 | 26/26 |
| Attention operands | 0.000237 / 0.00406 | 0.004266 / 0.02533 | 26/26 |
| Both | 0.000464 / 0.00884 | 0.006018 / 0.02561 | 26/26 |

The comparison covers all 576,000 final normalized encoder values and 1,348,464 logits per variant. All variants followed the same 25-token JFK trajectory including end-of-text. This single clip measures numerical drift along that trajectory. Corpus-level word error rate is outside this measurement.

Append `half-projections`, `half-attention`, or `join-logits` to the `fused persistent-read persistent-upload host-logits batch-cross queue=fixed-two` control. For numerical comparisons add `capture` in a separate run. The last request writes `encoder.f32` and one little-endian FP32 file per logit vector beside the JSON result.

```powershell
python tools/whisper-benchmark/compare_precision.py control.json variant.json
```

The raw samples, separately timestamped synchronous operation groups, capture comparisons and build hashes are under `artifacts/local/whisper-baseline/acceleration/shape-precision-qualified/`.

## Separating FP16 packing from encoder attention

The two-slot FP32 control and packed-Q/K/V variant use the same JFK mel window, checkpoint, decoder path and 26 logits readbacks. Each request submitted 66 compute batches. In separate profiling processes, Vulkan timestamps bracketed each dispatch and its following dependency barrier inside the original encoder attention command buffer. Each queue slot owns its query pool until its fence completes. The table reports medians of per-request sums over all four encoder layers, with 21 requests per mode.

| Mode | K/V scatters, ms GPU | Pack Q, ms GPU | Pack K/V, ms GPU | Fused attention, ms GPU | Attention batch, ms GPU | Uninstrumented request, ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| FP32 two-slot control | 0.119 | 0.000 | 0.000 | 5.034 | 5.152 | 50.58 |
| Packed FP16 Q/K/V | 0.119 | 0.045 | 0.077 | 12.531 | 12.770 | 58.36 |

Packing Q and K/V together cost 0.121 ms GPU. The packed-input attention dispatch took 7.497 ms more than the FP32 dispatch. This interval includes packed loads, binary16-to-FP32 conversion, shared-memory work, score accumulation, softmax and the output write. The timestamp boundaries do not distinguish those operations within the dispatch.

The FP32 run and the FP16 run each reproduced their corresponding prior encoder and logits captures. Enable `profile-attention` on the `fused persistent-read persistent-upload host-logits batch-cross queue=fixed-two` control, then repeat with `half-attention`. Each sample exposes per-layer GPU milliseconds in `EncoderAttentionProfile`. Use separate runs without profiling for request latency. Raw results are under `artifacts/local/whisper-baseline/acceleration/prefill-attention-qualified/`.

## Reproduce on Windows

Use an x64 Visual Studio developer PowerShell with CMake, Ninja, Vulkan SDK
(including DXC and glslc), .NET 10 and Python 3.9+ with NumPy installed. Run from
the repository root. Ninja avoids the nested MSBuild FileTracker path-length
failure during upstream Vulkan shader generation.

```powershell
$run = Join-Path $PWD 'artifacts/local/whisper-baseline'
New-Item -ItemType Directory -Path $run -Force | Out-Null
curl.exe -fL 'https://codeload.github.com/ggml-org/whisper.cpp/zip/refs/tags/v1.9.6' -o "$run/whisper.cpp-v1.9.6.zip"
Expand-Archive "$run/whisper.cpp-v1.9.6.zip" -DestinationPath $run
curl.exe -fL 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.en.bin' -o "$run/ggml-tiny.en.bin"

$ref = Join-Path $run 'whisper.cpp-1.9.6'
$build = Join-Path $run 'w'
cmake -S $ref -B $build -G Ninja -DCMAKE_BUILD_TYPE=Release -DGGML_VULKAN=ON -DGGML_NATIVE=OFF -DWHISPER_BUILD_TESTS=OFF -DWHISPER_BUILD_EXAMPLES=ON -DWHISPER_SDL2=OFF
cmake --build $build --target whisper-cli --parallel 6
cmake -S tools/whisper-benchmark -B "$build/harness" -G Ninja -DCMAKE_BUILD_TYPE=Release "-DWHISPER_SOURCE=$ref" "-DWHISPER_LIBRARY=$build/src/whisper.lib" "-DWHISPER_BIN=$build/bin"
cmake --build "$build/harness"

dotnet build tools/Aurelian.WhisperBenchmark -c Release
python -B tools/whisper-benchmark/run_baseline.py "$run/ggml-tiny.en.bin" "$ref/samples/jfk.wav" tools/Aurelian.WhisperBenchmark/bin/Release/net10.0/Aurelian.WhisperBenchmark.dll "$build/bin/whisper-benchmark-reference.exe" "$run/qualified"
```

The runner verifies the checkpoint SHA-256, the Vulkan device, token equality,
dispatch counts and timestamp coverage. It writes per-process logs, all
samples, asset hashes and a summary to `artifacts/local/whisper-baseline/qualified/`.
This measures model latency on one English clip, one encoder window, and the
no-timestamp decoder path.

## Validation

The Release Aurelian Core suite passed all 1,438 tests across 20 assemblies.
Native Vulkan checks passed the existing 2D convolution at both strides and
produced bit-identical tiled attention outputs across the two submission modes,
including with the existing streaming controller enabled. The batched Whisper
run with Vulkan validation enabled matched the benchmark token sequence.

For the acceleration experiments, the Release Aurelian Core suite passed
1,441 tests across 20 assemblies. Native checks confirmed bit-identical
workspace reuse, direct cache attention at both head widths over 1,501 keys,
and dense GEMV with partial input and output tiles.
