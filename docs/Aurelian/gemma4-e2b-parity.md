# Gemma 4 E2B Vulkan parity

Aurelian and llama.cpp ran the same text-only
[Google Gemma 4 E2B IT QAT Q4_0 GGUF](https://huggingface.co/google/gemma-4-E2B-it-qat-q4_0-gguf)
on an RTX 3070 using driver 610.88. The checkpoint SHA-256 is
`fa401b55b07ee70a54c6dae3903c783a6e65064312529ea57175cb5f8dec6634`.
The E2B adapter preserves packed weight values, including the Q6_K output head.
It implements the 35-layer text graph, the per-layer input projections and the
20 layers using shared KV. It accepts one sequence and up to 1,024 positions.
Both runtimes keep transformer and output weights on the GPU and gather the
input embedding rows on the host. Aurelian reads those rows through its GGUF index.

## Output parity

Both proof runners replay identical token IDs. Each timed step produces
262,144 post-softcap logits for the last token. In the 84-token chat fixture,
Aurelian's FP32 path matches the reference's top token at all 33 positions,
with **0.017%** maximum relative RMS error. The optimized path reaches
**0.044%** on the same fixture. The controlled llama.cpp run uses FP32 KV,
disables Flash Attention, and disables FP16 and integer-dot shortcuts in the
Vulkan backend. The FP32 path also passes the 64-token synthetic stress fixture
at **0.071%** maximum relative RMS. Those stress logits exposed a layer scaling
shader that rounded to BF16. E2B now explicitly uses FP32 scaling.

Against llama.cpp's normal Flash Attention and FP16 KV, the tuned path matches
all top-1 tokens and the top-five sets over the 33 positions in the 64-token
text test and the 17 positions in the 512-token test. The maximum relative RMS
errors are 2.78% and 1.46%, respectively. The 512-token test exercises incremental
decode across the sliding-window boundary. The on-device argmax produces the
same 33 token IDs as full readback. The single-turn arithmetic smoke answers
`4` and emits the Gemma end-of-turn token.

## End-to-end performance

Rates cover three warm repetitions after one warmup. Prefill includes the
first output head and a full logit readback. Decode processes one supplied
token at a time with the same initial context in both runtimes.

| Fixture | Aurelian prefill | llama.cpp prefill | Aurelian decode | llama.cpp decode |
|---|---:|---:|---:|---:|
| 64 prompt, 32 decode | 33.30 ms | 12.56 ms | 61.4 tok/s | 166.5 tok/s |
| 512 prompt, 16 decode | 120.08 ms | 38.04 ms | 60.4 tok/s | 159.3 tok/s |

A final Aurelian recheck measured 34.23 ms and 59.7 tok/s at 64 tokens, and
125.95 ms and 51.6 tok/s at 512 tokens. All logit comparisons passed again.

The initial scalar-projection E2B run of the same 64-token fixture measured
181.7 ms prefill and 49.7 decode tokens/s. The tuned result is approximately
5.5× faster in prefill and 24% faster in decode. Cooperative projection,
pruning token rows after the last KV producer, fused prompt attention and
lower CPU embedding decode cost all contribute. The device head writes flat
logits directly and computes softcap before readback. Greedy generation can
read just the winning token and score.

The optimized Aurelian runs peak at 1,593 MiB and 1,744 MiB of process dedicated
GPU memory, with 74 MiB shared in each. The compared llama.cpp Vulkan runs use
`GGML_VK_DISABLE_HOST_VISIBLE_VIDMEM=1` and peak at 1,465 MiB and 1,963 MiB
dedicated. Without that flag, one observed 64-token run held about 1,038 MiB
shared and 451 MiB dedicated during decode and measured about 20 tok/s. WDDM
memory sampling is needed to interpret the GPU placement on this desktop.

## Where the remaining time goes

- At 64 tokens, Aurelian spends 12.8 of 16.3 ms per decode step on body GPU
  work. An instrumented run attributes about 7.4 ms to Q4 projections and
  2.6 ms to RMSNorm. The Q6_K output head adds about 1.6 ms of GPU work.
  Resident decode still records 846 body dispatches in two submissions.
- At 512 tokens, an instrumented prefill spends 46 ms in cooperative
  projection and 35 ms in fused grouped attention. Cooperative weight and
  input packing are timed separately. Host input preparation fell from 27.6
  to 10.2 ms after reusing the Q6_K scale and high-bit decoding across four
  weight groups.
- The fused one-query attention path loses at long context. On the 512-token
  fixture the isolated switch to separate score, softmax and value shaders
  cut decode from 27.6 to 17.6 ms/token in the first A/B. The tuned profile
  uses fused attention up to 128 live keys for single-token steps. Multi-row
  prefill uses the fused path.

The highest-leverage remaining work is a faster resident Q4 decode kernel
and tiled grouped attention for 256- and 512-wide Gemma heads. The first
profiling lane should keep the GGUF and 64-/512-token fixtures fixed so that
kernel changes can be evaluated separately from streaming.

## Packed decode and prefill tuning

The next pass compared eight, 16 and 32 lanes per packed Q4 output on the
RTX 3070. Sixteen lanes gives eight outputs per 128-thread workgroup and won
the short-context A/B. The eight-lane case measured 56.1 tok/s on that run.
Prefill now scores eight keys per iteration and reuses the packed activation
across Q/K/V and the MLP gate/up pair. The prefill issues 45 fewer dispatches
on these fixtures. Each kernel family shares a Visual TypeScript source
across its compiled shape variants.

| Fixture | Previous prefill | Tuned prefill | Previous decode | Tuned decode |
|---|---:|---:|---:|---:|
| 64 prompt, 32 decode | 33.00 ms | 31.37 ms | 61.0 tok/s | 69.4 tok/s |
| 512 prompt, 16 decode | 119.90 ms | 111.76 ms | 60.9 tok/s | 68.9 tok/s |

The final pass ran the 64-token pair in previous/tuned order and the
512-token pair in tuned/previous order. Earlier trials included substantially
slower runs. The table uses the final warm medians.
Peak runtime-owned buffer allocation was identical within each pair,
at 1,614 MiB and 1,764 MiB respectively. Q4 body projection measured about
5.3 ms per decode step in the instrumented run. At 512 tokens, fused prompt
attention measured about 26 ms, with cooperative projection at 46 ms.
The fresh device-local llama.cpp reference measured 14.37 ms prefill and
161.7 tok/s decode at 64 tokens, and 41.94 ms and 156.3 tok/s at 512.

Full-logit comparison against llama.cpp retained the top token and top-five
set at all 50 positions. Maximum relative RMS error was 2.78% at 64 tokens
and 1.46% at 512. The GPU argmax now uses `when utility` for its local
selection. Its 33 greedy results agree with both the original reduction
and the full logit readback. Native kernel checks cover negative scores,
signed zero, subnormals and ties between workgroups.

With `--optimized`, add `--prior-kernels` for the original kernel choices.
Starting from that configuration, select individual experiments with
`--decode-lanes 16`, `--wide-prefill-attention` or `--reuse-prefill-pack`.
The eight-lane variant remains available with `--decode-lanes 8`.
Raw results are under `artifacts/local/gemma4/utility-tuning`.

## Resident decode after removing parameter buffers

The optimized E2B proof now records the 64-byte kernel parameter record as
Vulkan push constants. All three Vulkan compute submission paths share the same
dispatch recorder. The Visual TypeScript source and the parameter layout are
shared between the two ABIs. The built-in shader cache keys on both shader name
and parameter ABI. For this historical A/B, use
`--previous-decode --row-major-q4 --separate-input-uploads` on both arms
and add `--storage-parameters` to the control.
Other inference sessions opt in through `VulkanInferenceOptions.PushDispatchParameters`.

| Fixture | Control prefill | Push prefill | Control decode | Push decode |
|---|---:|---:|---:|---:|
| 64 prompt, 32 decode | 31.85 ms | 29.62 ms | 66.69 tok/s | 74.86 tok/s |
| 512 prompt, 16 decode | 110.89 ms | 111.50 ms | 69.99 tok/s | 73.41 tok/s |

Each pair uses the median of three warm repetitions. The 64-token decode body
GPU interval fell from 10.73 ms to 9.56 ms, and the 512-token interval went from
10.12 ms to 9.12 ms. The optimized configuration eliminated 475 buffer
allocations and 30,400 bytes from peak owned GPU buffer allocation on each
fixture. It gave bit-identical full logits across all 50 positions.

Absolute throughput varied substantially during the desktop session. A later
matched pass ran at 34.70 → 37.20 tok/s and 34.48 → 35.58 tok/s, respectively. The device-local
llama.cpp reference in that same pass measured 84.73 and 83.13 tok/s, with
prefill at 25.23 and 74.68 ms. Aurelian matched all top-one tokens and top-five
sets, with maximum relative RMS error of 2.78% and 1.46% respectively.
The final build recheck ran at 31.45 → 35.94 tok/s and 30.07 → 29.30 tok/s,
with identical logits again. The 64-token decode gain held across all three
pairs. The 512-token speed result varied across pairs.

The round also tested these changes on the same GGUF.

| Experiment | Outcome |
|---|---|
| Native signed INT8 dot with Q8 activation packing | 67.44 tok/s versus 70.11 on the short control. Reuse raised it to 68.07 tok/s. Both had 31/33 matching top tokens and 9.08% maximum relative logit RMS. Restricting it to wide outputs restored 33/33 and was slower than the control. |
| RMSNorm fused with residual addition and layer scaling | Bit-identical, with 140 fewer decode body dispatches. Short decode was 66.85 tok/s versus 70.11. |
| Cooperative projection that dequantizes Q4 directly into the workgroup tile | Bit-identical and saved 57 MiB of peak owned GPU buffers at 512 tokens. Prefill took 141.54 ms versus 114.59 ms. |
| One body submission and dependency-based barrier elision | Exact logits. The fastest short-context run reached 77.09 tok/s with both changes. The remaining runs did not establish a repeatable gain over push constants alone. |
| One subgroup per RMSNorm head | Kept top-one and top-five agreement with under 0.09% relative logit RMS against the Aurelian control. Warm decode was slower than the neighboring push-constant control. |
| 64×32 cooperative projection tiles and per-query causal loop bounds | The larger tile helped 512-token prefill in two loaded pairs and regressed short prefill. The per-query loop bound did not improve 512-token prefill. Neither qualified as a general default. |
| Separate score, softmax and value passes for multi-row prefill | The 512-token path was several times slower than the fused control. |

Raw data and experiment patches are under `artifacts/local/gemma4/native-dot-tuning`.
The final instrumented runs repeated the same ranking during the slower
desktop period. At 64 tokens, packed Q4 projection took 11.65 ms of body GPU
time per decode token, followed by RMSNorm at 4.79 ms and fused attention at
3.92 ms. At 512 tokens, cooperative projection and fused attention accounted
for 135.73 and 77.84 ms of the prefill GPU intervals. These numbers come from
the profiled runs, which execute with extra timestamp queries.

The 100 tok/s target remains open. Moving from roughly 75 to 100 tok/s requires
removing about 3.4 ms from the warm token path. The baseline profiles point to
resident Q4 weight reads and dequantization in decode, plus cooperative projection
and FP32 grouped attention in prefill. A useful next milestone is 85 tok/s
on both fixtures with the same logit gate, and a Q4 body projection GPU interval
under 4 ms per token. Test a decode-specific Q4 layout that coalesces reads
across output rows while preserving FP32 activations, the existing Q4
quantization and one resident weight copy. For prefill, tile more than one
query per workgroup so the queries can reuse K/V loads.

## Coalesced Q4 and host profiling

The tuned E2B path groups eight neighboring output rows, stores the GGUF
binary16 scale bits in pairs, and interleaves the four nibble planes so
adjacent lanes can read adjacent output weights. One resident copy feeds
decode, cooperative prefill packing, and the portable FP32 prefill kernel.
Decode uses FP32 activations and two independent K accumulators. This keeps
the quantized values unchanged and changes the FP32 reduction order.

The input embeddings share one reusable host payload. The first body
submission copies both tensors before executing their consumers, removing
two synchronous upload submissions from each token. The isolated upload
A/B improved decode from 87.85 to 90.35 tok/s at 64 tokens and from 86.34
to 87.94 tok/s at 512, with bit-identical logits.

| Warm fixture | Previous Aurelian prefill | Tuned prefill | llama.cpp prefill | Previous Aurelian decode | Tuned decode | llama.cpp decode |
|---|---:|---:|---:|---:|---:|---:|
| 64 prompt, 32 decode | 31.42 ms | 27.29 ms | 12.11 ms | 78.30 tok/s | 88.34 tok/s | 172.55 tok/s |
| 512 prompt, 16 decode | 128.82 ms | 103.42 ms | 37.26 ms | 76.81 tok/s | 85.88 tok/s | 164.64 tok/s |

Final matched runs use three warm repetitions, the same GGUF and supplied
tokens, and device-local llama.cpp weights. The compact weights reduced
peak Aurelian-owned GPU buffers by 111.05 MiB on both fixtures. Maximum
relative logit RMS against the previous Aurelian path was 0.00014% at 64
tokens and 0.00009% at 512. All 50 top-one tokens and top-five sets also
matched llama.cpp. The portable FP32 prefill recheck kept all 33 top-one
tokens and top-five sets with 0.00018% maximum relative RMS against its
row-major control.

Run `--optimized --previous-decode --row-major-q4 --separate-input-uploads`
to select the previous path. Add `--profile-cpu` to record validation, descriptor updates, command
recording, queue submission, fence wait, timestamp retrieval, embedding
decode, readback, and per-token managed allocation. Fence wait includes
the GPU work. Use a separate run with `--profile` to collect shader intervals
without using that run for latency comparisons.

| Decode timing per token | 64-token prompt | 512-token prompt |
|---|---:|---:|
| Aurelian total with CPU instrumentation | 11.32 ms | 11.65 ms |
| Body and head GPU intervals | 9.62 ms | 9.29 ms |
| Command recording | 0.81 ms | 0.90 ms |
| Descriptor work | 0.18 ms | 0.21 ms |
| Full logit readback | 0.15 ms | 0.16 ms |
| llama.cpp total without instrumentation | 5.80 ms | 6.07 ms |

Even assigning llama.cpp's entire time to the GPU leaves at least 3.83 ms
and 3.22 ms of GPU-time gap. The time outside Aurelian's GPU intervals
includes driver calls, transfers, waits and managed orchestration.
Independent shader profiling measured packed Q4 projections at 3.79 ms
per token on both fixtures, down from 5.17 ms in the short row-major run.
RMSNorm takes about 2.3 ms, attention takes about 1.7–2.0 ms, and the Q6_K
output head takes about 1.6 ms. At 512 tokens, cooperative projection and
fused attention take 45.31 and 25.37 ms of the prefill GPU intervals.

With the same loaded desktop conditions, disabling the native NVIDIA
cooperative matrix 2 path reduced short-context llama.cpp from about 84.5
to 73.9 tok/s. Disabling integer dot measured 78.9 tok/s, while disabling
the separate decode-vector capability measured 86.4 tok/s within the
control variation. The backend capability line reports `NV_coopmat2`.
These A/Bs leave a substantial gap with the conservative native settings
too. During the initial pass, Nsight's Vulkan trace required registry write
permissions and the pinned backend's perf logger aborted at
`ctx->compute_ctx.expired()`.
[Pinned llama.cpp Vulkan backend](https://github.com/ggml-org/llama.cpp/blob/c7518dbec/ggml/src/ggml-vulkan/ggml-vulkan.cpp)

Raw timings, logits, capability experiments and profiler logs are under
`artifacts/local/gemma4/coalesced-q4`.
The 85 tok/s and sub-4 ms Q4 targets passed. Reaching 100 tok/s now requires
about 1.3–1.6 ms less per token. A matched fused-attention experiment
would help identify the next change.

### Elevated Nsight decode capture

The native trace contains 62 regular decode steps from the two complete
warm iterations of the 64-token fixture. The repeated submit pattern
identifies decode steps 2–32 in each iteration. The first decode step has
a distinct submit pattern, and the GPU export ends during the last warm
iteration. The Aurelian columns use the same decode positions from three
warm iterations of the existing CPU and GPU batch profile.

| Median per decode token | llama.cpp with Nsight | Aurelian batch profile |
|---|---:|---:|
| End-to-end time | 6.37 ms | 11.32 ms |
| Native GPU graph span or Aurelian body and head GPU intervals | 5.09 ms | 9.65 ms |
| Time outside those GPU intervals | 1.26 ms | 1.66 ms |

The native GPU graph span runs from the first recorded GPU range to the
last in each ten-submit decode graph. The union of its recorded intervals
was 5.03 ms per token, with overlaps counted once. About 690 of 728 native
pipeline binds occurred while the GPU graph was in flight. The final
graph submit returned 3.12 ms before GPU completion at the median. Those
timestamps show that the native runtime keeps submitting work during
GPU execution.

The measured GPU intervals differ by 4.56 ms of the 4.95 ms wall-time
difference across the captures. The profilers and GPU interval boundaries
differ, so these measurements give a graph-level comparison. The previous
untraced native run had a 5.73 ms wall median at the same decode positions.
The trace's logits matched the untraced native run bit for bit.

Nsight exported no GPU debug-label ranges and no pipeline names on the
bind events. The queue timestamps establish the aggregate GPU difference.
Profiling individual native operators still requires labeled events.
The trace and the per-step extraction are
`nsight-llama64-20261126-021813.sqlite` and
`nsight-llama64-20261126-021813-analysis.json` under
`artifacts/local/gemma4/coalesced-q4`.

### Tiled attention, decode RMSNorm and queued submission

`--optimized` now uses the 32-key attention split once the E2B decode
context has more than 32 keys. Each tile computes a local maximum and FP32
weighted sum, then a second pass rescales and merges the partials. Multi-row
prefill uses the existing attention path. At the 1536-wide residual
normalizations, the tuned kernel holds its input in registers between the
sum-of-squares reduction and the gamma multiply. The K projection then uses
one FP32 normalization, rotation and cache-write dispatch in place of three.
Use `--optimized --previous-decode` for the unchanged control.

| Warm fixture | Previous Aurelian | Tuned synchronous | Tuned queued | llama.cpp |
|---|---:|---:|---:|---:|
| 64 prompt, 32 decode | 87.80 tok/s | 113.31 tok/s | 125.31 tok/s | 171.26 tok/s |
| 512 prompt, 16 decode | 85.40 tok/s | 107.63 tok/s | 121.14 tok/s | 161.42 tok/s |

All rates are medians of three warm repetitions using the same GGUF and
teacher-forced tokens. The queued option `--queued-e2b` keeps up to three
submissions in flight and drains before logits readback. It stays opt-in
because the second staging path raises peak owned allocations by 2.60 MiB
at 64 tokens and 20.51 MiB at 512, relative to the tuned synchronous path.
The default shader changes add about 0.07 and 0.40 MiB, respectively.
Queued prefill was 25.34 and 103.12 ms, against 13.60 and 39.57 ms in
llama.cpp. The tuned runs retained all 50 top-one tokens and top-five
sets. Maximum relative RMS change against the prior Aurelian path was
0.00014% on both fixtures. The queued and synchronous logits were
bit-identical.

Isolated tests at 512 tokens measured 88.58, 88.76 and 78.64 tok/s for 32,
64 and 128 keys per tile, against 83.18 tok/s for that control. At 64 tokens
the 32-key variant measured 98.59 tok/s against 89.63, so it is the default
for both fixtures. The width specialization alone passed 100 tok/s on both
fixtures in its A/B. In the instrumented runs, the separate RMSNorm
dispatches fell from about 2.30 to 0.68–0.69 ms/token, and attention from
1.98 to 1.00 ms at 64 tokens and from 1.65 to 1.13 ms at 512. Fused K cache
writes eliminated 30 dispatches per decode token with bit-identical logits
in isolation. Its performance effect was small in the fully queued A/B.
Pass `--separate-key-cache` to reproduce that A/B.

The short native label probe and both full captures used
`--trace=vulkan,vulkan-annotations`. The API labels show 35 split-key flash
attention passes and 35 merges per decode token, as well as 70 native
RMSNorm plus multiply and add dispatches, another 35 with a further multiply, and
15 K normalization, RoPE and half-precision cache writes. The GPU span of
the isolated native Q6_K output and softcap submit was about 0.76 ms at
both prompt lengths, versus about 1.6 ms for Aurelian's Q6_K output shader.
The native Q6_K debug marker itself reported about 4 µs inside that submit.
The markers identify native shader families and fusion. Their durations
are unreliable as isolated execution costs, so the native head comparison
uses the surrounding identifiable submission interval.

Raw runs, annotated traces and `native-label-summary.json` are under
`artifacts/local/gemma4/decode-attention`.

### Q6_K output projection

The tuned E2B profile uses 16 lanes per output. Each lane loads four
consecutive quantized values at a time and accumulates FP32 dot products.
The interleaved layout groups eight output rows and preserves every GGUF
Q6_K code, signed subscale and binary16 superblock scale. The raw GGUF
and interleaved word kernels share a Visual TypeScript entry with a
compile-time layout parameter.

| Q6_K shader variant | GPU ms per token, 64 prompt | GPU ms per token, 512 prompt |
|---|---:|---:|
| Original byte loads, 32K-row shards | 1.571 | 1.581 |
| Original byte loads, 64K-row shards | 1.550 | |
| Four-value loads, raw GGUF, 32K-row shards | 0.880 | 0.881 |
| Four-value loads, interleaved, 32K-row shards | 0.830 | 0.831 |
| Four-value loads, interleaved, 64K-row shards | 0.783 | 0.783 |

The larger shards cut the projection from eight dispatches to four
without increasing the resident 315 MiB weight payload. They use the same
32K-row chunks for upload, with 128 KiB more peak Aurelian-owned GPU
buffer allocation for the larger logit slice. Interleaving adds a
39.375 MiB temporary host buffer and about 50 ms to measured checkpoint
loading in the final queued comparison. Use `--head-layout word
--narrow-head` for the four-value kernel with raw GGUF weights.
`--head-layout row --narrow-head` reproduces the head used in the
historical measurements above. The new 0.783 ms shader interval is close
to the previous native 0.76 ms Q6_K and softcap submission span, which
covers a different timing boundary.
[Pinned llama.cpp Q6_K shader](https://github.com/ggml-org/llama.cpp/blob/c7518dbec/ggml/src/ggml-vulkan/vulkan-shaders/mul_mat_vec_q6_k.comp)

| Warm fixture | Queued control, two passes | Tuned synchronous | Tuned queued, two passes | llama.cpp | Tuned queued / llama.cpp prefill |
|---|---:|---:|---:|---:|---:|
| 64 prompt, 32 decode | 126.06 / 127.47 tok/s | 131.29 tok/s | 139.91 / 139.88 tok/s | 172.27 tok/s | 24.95 / 12.12 ms |
| 512 prompt, 16 decode | 123.26 / 111.98 tok/s | 124.62 tok/s | 136.82 / 136.97 tok/s | 165.79 tok/s | 102.56 / 36.87 ms |

The two queued pairs ran in opposite order per fixture. Each figure is a
median of three warm repetitions. Against the original head, all 50
top-one tokens and top-five sets matched, with at most 0.000036% relative
logit RMS. Synchronous and queued logits were bit-identical, and GPU
greedy selections matched the full-logit argmax at all 50 positions.
Against the current llama.cpp run, the same top-one and top-five results
matched with maximum relative logit RMS of 2.78% and 1.46%.

Queued decode is about 81–83% of llama.cpp throughput, with 1.27–1.34 ms
remaining per token. The Aurelian body GPU interval was 6.26–6.38 ms per
token in the instrumented runs. Its Q4 projections alone took about
3.8 ms. Summing the packed tensor shapes gives 999 MiB of Q4 weights
visited per decode token, of which the feed-forward gate, up and down
projections account for 835 MiB. Turning off native cooperative matrix 2
left llama.cpp at 168.13 and 162.11 tok/s. This limits how much of the
current difference can be attributed to that native capability. A 0.60–0.70 ms reduction
would bring queued decode within 10% of reference throughput.

Raw timing, CPU and GPU profiles, and full logits are under
`artifacts/local/gemma4/q6-head`.

### FP32 Q4 feed-forward decode

The tuned E2B proof combines the gate and up projections and their GELU
multiply in one dispatch. Both projections reuse the same FP32 activation
loads and share the Q4 zero-point correction. Their packed weights occupy
consecutive regions of one buffer. Prefill reads the original matrices
through row slices of that buffer. The model retains one copy of each
matrix and removes two dispatches per decode layer.

The down projection assigns four independent 128-column partials to
each 512-column tile and splits K across two workgroups. The existing
1536-wide RMSNorm dispatch sums the two FP32 results before normalizing.
It reuses 12 KiB of partial scratch across layers. The new paths also
apply to the single remaining row in pruned prefill.

`--profile` reports `FeedForwardMs` separately from the shader totals.
These GPU intervals are medians across decode steps and are measured
separately from queued wall time.

| GPU stage per decode token | 64 prompt control | 64 prompt tuned | 512 prompt control | 512 prompt tuned |
|---|---:|---:|---:|---:|
| Gate | 0.828 ms | | 0.840 ms | |
| Up | 0.821 ms | | 0.821 ms | |
| GELU times up | 0.085 ms | | 0.086 ms | |
| Gate, up and GELU multiply total | 1.734 ms | 1.524 ms | 1.748 ms | 1.542 ms |
| Down and post-feed-forward RMSNorm | 1.099 ms | 1.000 ms | 1.103 ms | 1.006 ms |

On the 64-token fixture, assigning a full Q4 block to each lane raised
down time from 1.01 to 1.32 ms. Four independent coalesced partials in a
single workgroup matched the control. Splitting K twice or four ways
lowered down time to about 0.96 ms with a separate merge. Merging inside
RMSNorm saved another dispatch and kept both split counts near 1.00 ms
for down and normalization together. Two splits use half the scratch
of four. Folding the residual add into the same dispatch increased the
measured epilogue interval, so the selected path uses the separate add.

| Warm fixture | Queued control | Gate/up only | Tiled down only | Both | llama.cpp |
|---|---:|---:|---:|---:|---:|
| 64 prompt, 32 decode | 140.80 tok/s | 142.81 tok/s | 144.60 tok/s | 148.74 tok/s | 171.97 tok/s |
| 512 prompt, 16 decode | 139.26 tok/s | 139.21 tok/s | 132.66 tok/s | 139.03 tok/s | 164.63 tok/s |

The control and combined columns pool six warm repetitions at 64
tokens and nine at 512, with run order alternated. The isolated wall
comparisons each use three warm repetitions. The combined path improved
the short fixture by 5.6%. Long-context paired medians varied from
136.57 to 142.74 tok/s combined and 130.83 to 139.90 tok/s for the
control, with nearly equal pooled medians. The short and long combined
rates are 86.5% and 84.5% of llama.cpp. Reaching 90% requires saving
another 0.26 and 0.44 ms per token, respectively.

Each isolated change and their combination kept all 50 top-one tokens
and top-five sets against the separate FP32 control. The combined maximum
relative logit RMS was 0.000123%. All 50 top-one and top-five sets
also matched llama.cpp, with maximum relative RMS of 2.78% and 1.46%.
Queued and synchronous logits were bit-identical. GPU greedy tokens
matched the full-logit argmax.

With `--optimized`, use `--previous-feed-forward` to reproduce the
control or the historical head results. Use `--down-tiles 0` to isolate
gate/up or `--separate-gate-up` to isolate tiled down. `--down-tiles 4`
selects four partials, and `--separate-down-norm` runs an independent
merge. Raw runs and logits are under `artifacts/local/gemma4/q4-feedforward`.

### RDNA patch study and finer attention tiles

The study used commit `cda6af57f083aee529b5d55f05d1d5aeb709e102` of
`llama-cpp-rdna-boosts`. Block 05 pipelines loads in the RDNA4 Q8_0
attention band and conditions its wider attention kernels on the shape.
Blocks 07 and 08 tune matvec workgroup geometry by format and shape.
Block 08 also fuses gate, up and GLU, matching the operation Aurelian
already fuses for dense E2B. The local experiments use Vulkan and FP32
activations on the RTX 3070.
[Pinned patch documentation](https://github.com/stew675/llama-cpp-rdna-boosts/blob/cda6af57f083aee529b5d55f05d1d5aeb709e102/patches/README.md)

| Experiment | GPU result at 64 / 512 prompt tokens |
|---|---|
| Gate/up with 64 threads | 1.755 / 1.766 ms, up from 1.521 / 1.536 ms at 128 threads |
| Gate/up with 256 threads | 1.498 / 1.509 ms, with lower queued throughput on both fixtures |
| Paired attention key stripes with shared query loads | 1.046 / 1.096 ms for the score/value kernel, up from 0.867 / 0.915 ms |
| Attention with 64-key tiles | 1.752 / 1.838 ms including merge, up from 1.001 / 1.122 ms |
| Attention with a 128-key split threshold | 1.972 / 3.320 ms including merge where applicable. The short fixture takes unsplit attention. |
| Parallel merge with 32-key tiles | 0.985 / 1.104 ms for attention and merge |
| Parallel merge with 16-key tiles | 0.619 / 0.848 ms for attention and merge |

The selected path uses 16-key tiles once decode has more than 32
attended keys. The merge distributes loads and exponentials across lanes
and accumulates the sums in tile order. It handles up to 64 partials,
covering 1024 keys even at the smaller tile size. Compared with the
prior default, peak Aurelian-owned GPU buffers increased by 0.08 / 0.39
MiB. For the tile-size A/B, use `--split-decode-attention 32` with
`--optimized` to select 32-key tiles and the same parallel merge.

| Warm fixture | 32-key tiles | 16-key tiles | llama.cpp | Share of llama.cpp |
|---|---:|---:|---:|---:|
| 64 prompt, 32 decode | 146.68 tok/s | 151.70 tok/s | 167.41 tok/s | 90.6% |
| 512 prompt, 16 decode | 142.14 tok/s | 143.62 tok/s | 164.22 tok/s | 87.5% |

The wall-time rates pool six warm repetitions per cell and nine for
the 512-token Aurelian cells. Each fixture includes both Aurelian arm
orders. The short result reaches 90% against this round's native
reference and 88.2% against the previous 171.97 tok/s reference. The
long result needs another 0.20 ms/token to reach 90% against this
round's reference.

The comparison script accepts `--kl-tolerance 1e-8` alongside the RMS
gate. It computes the maximum reference-to-candidate KL divergence in
nats over full softmax distributions at the teacher-forced positions.
The 16-key path kept all 50 top-one tokens and top-five sets against the
prior default, with maximum relative RMS below 0.00020% and maximum KL
of 1.47e-11 nats. Changing only the merge at 32 keys and the paired
stripe experiment both yielded bit-identical logits.
[Upstream numerical qualification notes](https://github.com/stew675/llama-cpp-rdna-boosts/blob/cda6af57f083aee529b5d55f05d1d5aeb709e102/GREEDY-PURITY.md)

Block 05's KV reuse across query heads is a separate hypothesis for
long contexts. Block 06's prefill shape policies belong in the prefill
benchmark lane. The host-expert tier in block 09 offers a methodology
for instrumenting misses and copy overlap when studying larger models.
The E2B measurements here cover resident dense weights.

Raw timings, logits, and prototype patches are under
`artifacts/local/gemma4/rdna-research`.

### Basalt study and fused query preparation

Basalt targets Blackwell CUDA and descends from Strata. This study uses
commit `8c0fe539e06fd0eeae57524fa9d2f6c8389a6763`. It borrows
data-reuse and fusion ideas for the RTX 3070 Vulkan path. All Aurelian
arms use the same E2B GGUF, token fixtures and FP32 activations.
[Basalt source and lineage](https://github.com/jesdga95/basalt/tree/8c0fe539e06fd0eeae57524fa9d2f6c8389a6763)

Basalt's GQA kernel stages K/V once across a group of query heads and
uses matrix instructions for the grouped work. Aurelian tested sharing
each FP32 K/V load across two query heads in a Vulkan workgroup. A
single-head version of the same shader served as an additional control
for the specialization itself. Basalt also builds persistent RoPE
tables and uses fused kernels in its decoder. The Aurelian experiments
tested a standalone table lookup and fused RMSNorm and rotation separately.
[Basalt GQA kernel](https://github.com/jesdga95/basalt/blob/8c0fe539e06fd0eeae57524fa9d2f6c8389a6763/src/kernels/cuda/gqa_attn.cu#L154)
[Basalt RoPE setup](https://github.com/jesdga95/basalt/blob/8c0fe539e06fd0eeae57524fa9d2f6c8389a6763/src/models/qwen4exp/decode/layer.cpp#L504)
[Basalt row normalization](https://github.com/jesdga95/basalt/blob/8c0fe539e06fd0eeae57524fa9d2f6c8389a6763/src/kernels/cuda/row_norm.cu)

| GPU intervals per decode token | 64 prompt | 512 prompt |
|---|---:|---:|
| Current attention kernel before merge | 0.489 ms | 0.578 ms |
| Specialized 16-key attention, one head | 0.562 ms | 0.635 ms |
| Specialized 16-key attention, two heads sharing K/V | 0.739 ms | 0.775 ms |
| Query RoPE, computed per invocation | 0.079 ms | 0.078 ms |
| Query RoPE, standalone table lookup | 0.086 ms | 0.086 ms |
| Bounded RMSNorm plus query RoPE, separate | 0.289 ms | 0.290 ms |
| Same shader families with fused query RMSNorm/RoPE | 0.182 ms | 0.183 ms |

The optimized E2B path uses the fused query kernel. One workgroup owns
each query head and rotates its pairs in place after the RMS reduction.
It reuses the kernel that already prepares K for cache writes through a
Visual TypeScript module variant. It removes 35 dispatches per decode
token and adds 7 KiB of immutable RoPE factors for the seven global
layers. The attention-sharing and table prototypes are archived under
`artifacts/local/gemma4/basalt-study`.

Use `--separate-query-rope` with `--optimized` to select the original
query path. The prototypes and fused path preserved bit-identical full
logits at all 50 fixture positions.

| Warm fixture | Separate query preparation | Fused query preparation | llama.cpp |
|---|---:|---:|---:|
| 64 prompt, 32 decode | 156.36 tok/s | 157.96 tok/s | 171.83 tok/s |
| 512 prompt, 16 decode | 143.55 tok/s | 145.26 tok/s | 167.77 tok/s |

The Aurelian columns pool six warm repetitions per cell with opposite
arm ordering. Individual passes ranged from essentially flat to a gain
of about 2%. The pooled gains were 1.0% and 1.2%. The fused rates are
91.9% and 86.6% of the current native reference.

Basalt also captures verify windows with CUDA graphs. Aurelian's
existing prepared Vulkan batches fixed parameters and bindings at
record time. The decode replay experiment below extends those batches
with a per-token parameter upload.
[Basalt batch graph capture](https://github.com/jesdga95/basalt/blob/8c0fe539e06fd0eeae57524fa9d2f6c8389a6763/src/models/qwen4exp/decode/verify/batch_capture.cpp#L17)

### Prepared single-token Vulkan replay

`--optimized --replay-e2b` records the E2B single-token body, logit head,
input copies and full-logit readback once. Its 751 dispatches use pinned
bindings and retained staging. Each replay uploads a contiguous,
aligned device-local parameter table and the token's embeddings.
The runtime patches Q/K RoPE positions, V cache slot, sliding-window
offset and KV length, split grid and merge count. Indirect dispatch
keeps the attention grid sized to the live KV window.
[Vulkan indirect dispatch](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdDispatchIndirect.html)

| Warm fixture | Queued E2B | Prepared replay | llama.cpp | Replay share of llama.cpp |
|---|---:|---:|---:|---:|
| 64 prompt, 32 decode | 156.52 tok/s | 170.68 tok/s | 173.75 tok/s | 98.2% |
| 512 prompt, 16 decode | 146.70 tok/s | 162.89 tok/s | 166.74 tok/s | 97.7% |

Aurelian columns pool six warm repetitions per cell in both arm
orders. Native rates use three warm repetitions from llama.cpp
`b12453` with the same GGUF and supplied tokens. Replay saved 0.53 and
0.68 ms per decode token. A separate queued storage-parameter run
measured 141.54 and 133.23 tok/s. Replay uses the storage ABI and
removes command re-recording, descriptor work and the separate
readback submission from the steady token path. In the long replay
profile the median token took 6.14 ms wall, 5.86 ms inside the compute
interval and 0.039 ms for host embedding preparation. The fused
body, head and readback are reported together by the replay harness.

First preparation took 0.42–0.45 s, including about 0.37 s compiling
the split-attention and merge pipelines in the final passes. The
warm runs reuse that graph across sequence resets. Peak Aurelian-owned
GPU buffers fell by 1.42 and 19.32 MiB from the queued control.

All 50 full logits rows matched queued E2B bit for bit, and top-one
tokens and top-five sets matched llama.cpp. The Vulkan proof also
compares replay with fresh recording as the tile count changes,
the KV window slides, and position moves backward. It checks the
final K/V cache contents as well as the output.

Replay is an opt-in full-logit benchmark path. Prompt evaluation
uses the ordinary model path before captured single-token decode.
Use `--optimized --queued-e2b` for the queued control.
Raw outputs are under `artifacts/local/gemma4/decode-replay`.

## Reproduce

Build the C++ reference with the headers and Vulkan binaries from llama.cpp
`c7518dbec` (build `b12453`). In a Visual Studio x64 development shell, set
`$llama` to that installation directory, with `include` and `bin` subdirectories.

```powershell
$out = 'artifacts/local/gemma4/e2b-parity'
$model = 'artifacts/local/models/gemma4-e2b/gemma-4-E2B_q4_0-it.gguf'
$tokenizer = 'artifacts/local/models/gemma4-e2b/tokenizer.json'
python tools/gemma4/e2b_fixtures.py $tokenizer $out
cmake -S tools/gemma4 -B "$out/reference-build" -G Ninja '-DCMAKE_BUILD_TYPE=Release' "-DLLAMA_INCLUDE=$llama/include" "-DLLAMA_BIN=$llama/bin"
cmake --build "$out/reference-build"
dotnet build tools/Aurelian.GemmaProof -c Release -m:1
$proof = 'tools/Aurelian.GemmaProof/bin/Release/net10.0/Aurelian.GemmaProof.dll'
dotnet $proof --e2b $model "$out/ids-text64.txt" "$out/aurelian" --optimized
$previousPlacement = $env:GGML_VK_DISABLE_HOST_VISIBLE_VIDMEM
try {
    $env:GGML_VK_DISABLE_HOST_VISIBLE_VIDMEM = '1'
    & "$llama/bin/gemma-e2b-reference.exe" $model "$out/ids-text64.txt" "$out/llama"
} finally {
    $env:GGML_VK_DISABLE_HOST_VISIBLE_VIDMEM = $previousPlacement
}
python tools/gemma4/compare_e2b.py "$out/llama" "$out/aurelian" --tolerance 0.035
dotnet $proof --e2b-generate $model $tokenizer 'What is 2 + 2? Answer with only the number.' 12
```

Use `--profile` for per-shader interval JSON. The accelerated A/B switches
are `--packed-prefill`, `--packed-decode`, `--cooperative`,
`--bounded-norm`, `--fused-prefill`, `--fused-decode` and
`--device-head`. Pass `--full-prefill` to keep all prompt rows in the
shared-KV layers, or `--layer-submissions` for one submission per layer.
Reference timings and full logits are under `artifacts/local/gemma4/e2b-parity`.
WDDM samples are under `artifacts/local/gemma4/e2b-reference`.
