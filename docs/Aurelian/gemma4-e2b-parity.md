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
and parameter ABI. Use `--optimized --storage-parameters` for the matched control.
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
