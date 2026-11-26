# Gemma 4 12B Unified: local C#/Vulkan baseline

The local checkpoint executes all 48 decoder layers, maintains a causal KV cache
and generates greedy text through Aurelian. Inference invokes no Python, Torch,
CUDA, llama.cpp or external inference service. Offline NumPy/tokenizers scripts
are independent qualification tools.

## Local assets

The supplied `Downloads/model (1).safetensors` is a 23,919,549,408-byte BF16
checkpoint. Its 677 tensors satisfy the explicit 12B Unified structural contract.
The header contains only `format=pt`: it does not prove an instruction-tuning or
QAT training variant. Google's public 12B IT configuration/tokenizer are compatible
with the tested tensor layout and generated chat tokens.

The ignored directory `artifacts/local/models/gemma4-12b/` contains:

| File | Purpose |
|---|---|
| `model.safetensors` | Local hard link to the original download; no second 24 GB copy |
| `config.json` | Validated Google Gemma 4 12B IT configuration |
| `tokenizer.json`, `tokenizer_config.json` | Google tokenizer assets |
| `q4b32-v1/q4.bin`, `q4b32-v1/q4.json` | Locally compiled PTQ matrix data and source fingerprint |

The original download remains in place. The hard link names the same bytes;
neither path should be edited in place. Source fingerprint:
`5A84CB313260AC447237B890387116DFA8682E49A6B44BC585AE8353ABBFF18D`.
The launcher checks that fingerprint before mixing original embeddings/norms with
the quantized matrices. No weights or generated model artifacts are committed.

The published metadata was fetched from
`https://huggingface.co/google/gemma-4-12B-it/resolve/main/`.
The compatibility check fixes the supported architecture fields rather than
assuming future metadata at that URL is interchangeable.

## Weight compilation and memory

`BlockQ4` is **Aurelian Q4B32**, a versioned local post-training representation:
32 signed values encoded as nibbles, with one FP32 scale and four UInt32 payload
words. It is not Google QAT or the GGUF Q4_0 layout. The resulting text matrices
occupy **7,441,612,800 bytes** on disk. Original BF16 embedding rows and normalization
vectors remain selected reads from the source checkpoint.

Matrix products consume packed weights directly on Vulkan; they never allocate
an expanded FP32 weight matrix. `QuantizedGemv.v.ts` reduces each complete dot
product in one dispatch: a lane loads a packed nibble word and scale once for
eight values, then participates in a workgroup reduction. Optional BF16 rounding
is fused after the full reduction. Widths are bounded at 32,768 using `static for`;
this does not extend the language or introduce dynamic loops. Multi-token inputs
use independent dot products per row. The older 1,024-column chunked kernel remains
an explicit numerical/performance reference, selectable with `--reference`.

`VulkanQuantizedLayers` owns an explicit first-use residency budget, defaulted by
the Gemma adapter to 1.5 GiB. Whole matrices stay resident until disposal when they
fit that budget; other matrices and vocabulary-head slices use temporary packed
allocations. There is no hidden eviction policy. The model constructor accepts a
different authored residency budget. A separate `hostWeightBudget` (default zero)
can retain private validated packed slices in RAM, excluding GPU-resident matrices.
Cache hits reuse those bytes without rereading or revalidating the file. This is
first-use retention until model disposal, with no eviction or implicit RAM growth
beyond the supplied budget. Cache-disabled reads use one bounded reusable host
scratch array. The compute owner retains one growable mapped upload staging buffer;
the existing uploader completes its timeline wait before that buffer can be reused.

The measured run peaks at approximately **1.87 GB of owned buffers**, including
transfer staging and scratch. This excludes driver allocations and other GPU
applications. In the sequential controlled RTX 3070 comparison, decode improves
from **0.30 to 1.01 tokens/second** (3.31 times). The optimized arithmetic run uses an explicit 6 GiB
host budget (5,831,884,800 bytes retained) and the same 1.5 GiB GPU weight budget.
The six-token generation takes 14.26 seconds versus 26.67 seconds; initial
prefill/cache filling is included in total time. Tokens/second excludes prefill
and includes generated channel/control tokens. These are small local smoke prompts,
not a throughput claim for long contexts or concurrent requests.
Times cover the `Generate` call, excluding process startup, source fingerprint
validation and tokenization. Lazy shader compilation during prefill is included.

`WeightTimings` exposes packed-read, weight allocation, upload and execution wall
times, GPU timestamps, dispatch counts and both cache-hit counts. GPU time is a
subset of execution wall time, not an additional phase to sum. The initial read
still validates every scale. Numerical reduction order changes, so output token
agreement does not imply bit-identical full-model logits.

A four-row shared-weight prefill experiment compiled after reducing static expansion,
but was slower than independent GEMV in direct native comparisons and was removed.
Packed cooperative GEMM/prefetch remain future work. Repeated PCIe uploads and
per-operation submissions remain measurable costs even with a populated host cache.

## Use the runtime

`Aurelian.Inference.Gemma4` owns the pinned model graph and chat profile. Shared
`Aurelian.Inference` owns quantized artifacts, packed layer binding, BPE and
`CausalTextGenerator`. The scheduler is supplied through `IComputeSchedule`; the
example uses the existing Dominatus adapter.

```csharp
using Aurelian.Assets.Tensors;
using Aurelian.Inference;
using Aurelian.Inference.Dominatus;
using Aurelian.Inference.Gemma4;

string modelDirectory = "artifacts/local/models/gemma4-12b";
string sourcePath = Path.Combine(modelDirectory, "model.safetensors");
Gemma4TextModel.ValidateConfiguration(Path.Combine(modelDirectory, "config.json"));
using var source = new SafeTensorFile(sourcePath);
using var packed = new QuantizedCheckpoint(Path.Combine(modelDirectory, "q4b32-v1"));
packed.ValidateSource(sourcePath);
using var session = VulkanInferenceSession.Create(new()
{
    MaximumTensorElements = 33_554_432,
});
using var model = new Gemma4TextModel(session.Compute, source, packed);
var tokenizer = new HuggingFaceBpeTokenizer(Path.Combine(modelDirectory, "tokenizer.json"));
var generator = new CausalTextGenerator(model, new DominatusComputeSchedule("chat"));
int[] prompt = tokenizer.Encode(Gemma4Chat.FormatUser("Say hello."));
var result = generator.Generate(prompt, maximumTokens: 16,
    endTokens: new HashSet<int> { tokenizer.TokenId("<eos>"), tokenizer.TokenId("<turn|>") });
string raw = tokenizer.Decode(result.Tokens.Select(token => token.Id), skipSpecial: false);
Gemma4ChatResponse response = Gemma4Chat.ParseResponse(raw);
Console.WriteLine(response.Content);
```

For the measured host-cached profile, supply `hostWeightBudget: 6144L * 1024 * 1024`
to the model constructor. The runtime does not select that RAM budget automatically.

For queued evaluation, supply a `DominatusComputeFeedforward(64 * 1024 * 1024)`
as `session.Compute.StreamController` and pass `useQueuedExecution: true` to the
model. This is explicit composition: the model does not construct a controller
or silently choose a RAM budget. Queue metrics remain available on
`model.QueueMeasurements` (the most recent 128 evaluations). GPU costs include
transformer batches only; the bounded vocabulary head still uses its explicit
per-slice readback boundaries.

The model borrows its source checkpoint, quantized provider and compute owner;
dispose the model first. The model owns its cache, vectors and resident matrices.
Each generation starts a new sequence. Failure/cancellation releases the iterator's
hidden tensor and resets model cache state. Cancellation occurs between named
prefill/decode/predict stages; it does not preempt an in-flight GPU operation.

The default context capacity is 256 tokens, configurable up to **1,024 total
tokens**. Prefills larger than 512 tokens are explicitly chunked to respect the
shared score-tensor row bound. Sliding/global head layouts, unscaled attention,
proportional RoPE, RMS normalization, global K=V before normalization, GELU gating,
layer scalars and final logit softcapping are authored model contracts.

Generation is deterministic greedy selection. The head emits bounded vocabulary
slices; their final logits cross an explicit readback boundary for host argmax.
Intermediate transformer activations stay on the GPU. Chat parsing separates
thinking from response content and preserves an incomplete-thinking stop.

## Submission and transfer experiment

On the RTX 3070, using the same arithmetic prompt, Q4 artifact, 1.5 GiB GPU
weight budget and 6 GiB host budget, two alternating baseline/queued comparisons
and a final queued recheck produced these local measurements:

| Path | Decode tokens/sec | Generation time |
|---|---:|---:|
| Previous separate synchronous upload/dispatch | 0.93–0.96 | 14.41–14.60 s |
| Combined copy/dispatch, synchronous (one ablation) | 0.98 | 13.93 s |
| Combined copy/dispatch, two queued slots | 1.10–1.16 | 11.89–12.56 s |

Mean decode throughput improves about **20%** over the baseline measured in this
round. This is a small sequential local experiment, not a broad throughput
certification. Every arithmetic run emits the same six token IDs and returns
`4`; queued greeting still returns `Hello!`. Generation includes first-use shader
compilation and host cache population, while checkpoint hashing/startup are outside
the generation timer. The owned GPU buffer peak rises from **1,868,577,472** to
**1,956,346,560 bytes** (about 88 MB). This excludes driver/pipeline memory.

The real transformer path reaches two in-flight submissions. Each evaluation
uses 817 submissions for 1,249 dispatches, with no intermediate readback; the
vocabulary head remains the next explicit synchronous boundary. Transfers retain
the same payload volume: scheduling improves overlap and submission overhead,
not the required arithmetic or weight bytes.

The final transfer-aware completion model has **3.62 ms** mean absolute
host-observed error versus **2.90 ms** for the compute-only model across 4,902
completions. The deterministic constant-cost fixture proves delay accounting,
but the real heterogeneous graph does not justify adaptive pacing from this
coarse estimator. It remains inspectable research evidence; bounded two-slot
admission and Vulkan fences remain the operative policy. It is not a qualified
closed-loop Smith predictor.

Ignored evidence: `scheduling-acceptance.json`, `generation-scheduling-*.json`,
`scheduling.json`, `block-scheduling-comparison.json`, and native proof logs under
`artifacts/local/gemma4/`. Native scheduling checks exercise changing uploads,
host scratch consumption, disposal before harvest, physical retirement after
read, resident staging reuse, forecast/nesting rejection, failure cleanup,
owner recovery and composite streamed-linear/tiled-attention boundaries.

## Full-model streaming rebase

The rebase uses the existing Q4B32 checkpoint and its BF16 activation
contract. The 48-layer body submits streamed projections through the
bounded Vulkan queue. `--push-parameters` selects the same push-constant
ABI used by queued E2B. `--gpu-argmax` queues the vocabulary slices and
returns one eight-byte token and score pair. Its lookup table applies the
original BF16 softcap to each score before argmax, including ties created
by rounding. The CPU head provides the control.

`--reuse-streamed-buffers` gives the layer adapter a separate 192 MiB
budget for reusable packed destinations. Each shape admitted to that
budget shares a destination across uploads on the ordered Vulkan queue.
The upload barrier orders overwrites after earlier shader reads. Weights
outside the resident budget still cross PCIe on every evaluation.
`--resident-mib` sets the independent resident weight budget.

The arithmetic prompt below produces six tokens including the turn
marker. The first run fills resident and host caches. Warm throughput
uses the median decode time of the next two generations and excludes the
first emitted token. The 6 GiB host cache budget is fixed in this sweep.
Dedicated VRAM is the sampled Windows process peak on the RTX 3070.

| Resident weights | Submission and head | Warm tok/s | Packed upload GiB/token | Peak dedicated VRAM |
|---:|---|---:|---:|---:|
| 1,536 MiB | Storage parameters, CPU head | 1.41 | 5.43 | 1,803 MiB |
| 1,536 MiB | Push parameters, CPU head | 1.29 | 5.43 | 1,772 MiB |
| 1,536 MiB | Push parameters, GPU head | 1.43 | 5.43 | 1,809 MiB |
| 1,536 MiB | Push parameters, GPU head, reusable destinations | 1.54 | 5.43 | 1,871 MiB |
| 2,816 MiB | Push parameters, GPU head, reusable destinations | 1.88 | 4.18 | 3,154 MiB |

Every arm produced `4` with the same generated IDs and BF16 scores. The
GPU head trimmed output readback from 1 MiB to 8 bytes per token. Its
median decode-stage head time fell from 109 to 72 ms relative to the
push-parameter CPU head. Reused packed destinations held 132 MiB and
reduced measured packed-buffer allocation work from about 23 ms to
under 0.1 ms per token at the same residency. The storage and push ABI
controls varied between runs. The current sweep gives no consistent
throughput advantage to push constants by themselves.

The resident E2B replay fixes a sequence of device bindings and uploads
the token inputs. 12B's projections currently submit through the
streaming queue, which also owns weight uploads and buffer lifetimes.
At 2,816 MiB of resident weights, the arithmetic fixture still reported
817 transformer submissions per decode step. Median GPU copy and compute
intervals were 146 and 48 ms, and CPU recording including staging writes
took 204 ms. These measured intervals can overlap.

The 68-token documentary checklist prompt measures the same two warm
trials with an eight-token generation cap. Both arms use the expanded
built-in shader pack and produce identical token IDs and scores.

| Profile | Warm decode | Warm prefill | Peak dedicated VRAM |
|---|---:|---:|---:|
| Queued storage control, 1,536 MiB resident | 1.40 tok/s | 3.10 s | 1,825 MiB |
| Rebased queue, 2,816 MiB resident | 1.94 tok/s | 2.92 s | 3,162 MiB |

The rebased profile uses 2,816 MiB of resident weights and a 6 GiB host
cache budget for the throughput baseline. On the long prompt, median
transformer copy time was 145 ms and CPU recording was 195 ms per decode
token. Measured GPU compute was 48 ms.

The fully streamed arithmetic diagnostic uses `--resident-mib 0` and
`--host-mib 7168`. One warm generation reached 1.39 tok/s, transferred
6.93 GiB per token, and sampled a dedicated VRAM peak of 332 MiB. It
preserved the arithmetic IDs and scores with zero resident packed weights.

The shared pack adds both ABIs of `QuantizedGemv.v.ts` and
`DecoderNormalize.v.ts` (526 KiB total). On the 68-token prompt, those
pipelines together took 1.49 s to create from source and 3.2 ms from baked
SPIR-V. The source-only arm also compiles the already-baked row reduction
and argmax shaders. For the rebased profile, cold time to first token
from generation entry fell from 8.10 s to 5.91 s. From command entry it
fell from 27.22 s to 24.96 s, including the checkpoint fingerprint check.
These cold numbers come from one fresh process per mode.

```powershell
dotnet build tools/Aurelian.GemmaProof -c Release -m:1
$proof = 'tools/Aurelian.GemmaProof/bin/Release/net10.0/Aurelian.GemmaProof.dll'
$model = 'artifacts/local/models/gemma4-12b'
$out = 'artifacts/local/gemma4/streaming-rebase'
$prompt = 'What is 2 + 2? Answer with only the number.'
dotnet $proof --generate $model $prompt 16 --queued --no-validation --host-mib 6144 `
    --generation-runs 3 --evidence "$out/control.json"
dotnet $proof --generate $model $prompt 16 --queued --no-validation --host-mib 6144 `
    --push-parameters --gpu-argmax --reuse-streamed-buffers --resident-mib 2816 `
    --generation-runs 3 --evidence "$out/rebase.json"
```

Use `--compile-shaders-at-runtime` for the source-only startup control.
`--compile-inference-shaders` builds the shared built-in pack and
`--verify-inference-shaders` compares the embedded binaries with their
Visual TypeScript sources. Raw generation, queue and WDDM memory samples
are under `artifacts/local/gemma4/streaming-rebase`.

## Resident-weight diagnostic

The end-to-end streaming rebase above exercises all 48 transformer layers
and the vocabulary head. This earlier diagnostic isolates a smaller
transformer prefix with controlled placement.

The proof runner can measure the first six real 12B Unified layers, including
the first global-attention layer. It runs a 64-token prefill and 16 fixed
single-token evaluations, excludes the vocabulary head and sampling, and
reports three warmed trials. The streamed arms keep all projection weights in
host RAM and reupload them at each evaluation. The resident arms keep the same
packed projection weights on the device. Token embedding lookup is the same
host-side operation in every arm. The timer ends before output readback and
hashing.

| Packed projections | Submission | Prefill, median ms | Decode, median ms/token | Peak process dedicated VRAM |
|---|---|---:|---:|---:|
| Streamed | Synchronous | 988 | 160.5 | 72 MiB |
| Streamed | Queued | 897 | 142.1 | 107 MiB |
| Resident | Synchronous | 866 | 45.9 | 848 MiB |
| Resident | Queued | 786 | 34.1 | 851 MiB |

RTX 3070, Windows driver 610.88, Vulkan validation disabled. All arms produced
byte-identical prefill activations and final decode activations. The 41 packed
projections total 812 MiB. Keeping them resident eliminates 812 MiB of uploads
per decode step and improves queued decode by 4.2×. GPU copy timestamps in the
streamed queued arm account for about 34 ms per token. CPU recording grows from
1.7 to 62.3 ms per token and includes staging writes. Both quantities can overlap.

In the synchronous resident arm, the packed linear dispatches account for about
792 of 866 ms of prefill. `QuantizedGemv.v.ts` schedules a separate workgroup
for every token/output-column pair. A tiled packed-matrix kernel can reuse a
weight tile across prompt tokens. Resident queued decode still issues 103
submissions and 157 dispatches per token. Its summed GPU compute timestamps are
14.6 of 34.1 ms per token, leaving time for buffer allocation, dispatch recording,
fence operations and gaps between submissions. Those intervals do not give an
exclusive host time breakdown.

```powershell
dotnet build tools/Aurelian.GemmaProof -c Release -m:1
$proof = 'tools/Aurelian.GemmaProof/bin/Release/net10.0/Aurelian.GemmaProof.dll'
$model = 'artifacts/local/models/gemma4-12b'
dotnet $proof --residency $model --streamed
dotnet $proof --residency $model --streamed --queued
dotnet $proof --residency $model
dotnet $proof --residency $model --queued
```

The JSON files `artifacts/local/gemma4/residency-*.json` capture tokens, raw
timings, cache hits, transfer bytes, output hashes and runtime memory telemetry.
Process dedicated VRAM above comes from Windows GPU Process Memory counters.

## E2B Vulkan reference

The original residency experiment below predates the
[Aurelian E2B adapter and matched llama.cpp parity run](gemma4-e2b-parity.md).

The same RTX 3070 ran the
[Google Gemma 4 E2B IT QAT Q4_0 GGUF](https://huggingface.co/google/gemma-4-E2B-it-qat-q4_0-gguf)
with llama.cpp `b12453` (`c7518dbec`), Flash Attention, 512-token batches
and no vision/audio projector. The text checkpoint is 3.12 GiB. The table
shows medians from five warmed synthetic benchmark repetitions. The 128-token
test times incremental evaluation starting with an empty cache.

| Placement, same official GGUF | 512-token prefill, tok/s | 128-token decode, tok/s | Peak dedicated GPU memory | Peak shared GPU memory |
|---|---:|---:|---:|---:|
| 36/36 transformer and output layers on Vulkan, input tables on host | 7,435 | 133.2 | 1,960 MiB | 52 MiB |
| 18/36 layers on Vulkan, rest on CPU | 3,889 | 54.2 | 1,487 MiB | 90 MiB |
| Every weight assigned to Vulkan | 511 | 20.3 | 2,577 MiB | 1,592 MiB |

The 36-layer run keeps 1,342 MiB of model tensors in Vulkan memory and
uses CPU lookups for the large input tables. The last arm assigns 3,179 MiB
of model tensors to Vulkan, with the 1,838 MiB per-layer Q6_K embedding table
among them. Windows reports about 1 GiB of shared memory throughout steady
execution in that arm. Device assignment alone is insufficient evidence of
dedicated residency on this desktop. The 18-layer arm measures hybrid CPU
execution. Aurelian's streamed arm executes its transformer layers on Vulkan
and transfers packed weights from host RAM.

A local all-device diagnostic changes just `per_layer_token_embd.weight`
from Q6_K to Q2_0, shrinking the table from 1,838 to 630 MiB. The other 540
tensor payloads match byte for byte. On Vulkan, this model reports 1,972 MiB
of model buffers, one graph split, a 2,596 MiB peak in dedicated GPU memory
and 32 MiB peak shared. Median performance is **8,007 tok/s** for 512-token
prefill and **128.9 tok/s** for 128-token decode. A follow-up starting with
512 cached tokens decodes at **131.0 tok/s** with 2,600 MiB peak dedicated
memory. Both the compact variant and the official checkpoint returned `4` in
a text generation smoke test. The compact table is lossy and the smoke test
covers a single prompt.

The E2B config has 35 transformer layers, 256-wide per-layer embedding inputs
and 20 layers sharing KV. The 12B Unified configuration gate rejects it with
`AUR-GEMMA-001`, so the earlier test used the 12B adapter for the residency A/B.
The six-layer Aurelian results above isolate transfer and
execution costs within one graph. They cannot be used to compute an E2B runtime
speedup or a numerical parity result.
[E2B config](https://huggingface.co/google/gemma-4-E2B-it/blob/main/config.json)

Reproduce the two default placements with `llama-bench` from build `b12453`
and a SHA-256-verified checkpoint from revision
`675cff42a74c774d6cb76f76d8eacb49b48c9b93`.

```powershell
$model = 'artifacts/local/models/gemma4-e2b/gemma-4-E2B_q4_0-it.gguf'
llama-bench -m $model -dev Vulkan0 -sm none -fa on -lzm off -ngl 999,18 -p 64,512 -n 16,128 -pg 256,64 -b 512 -ub 512 -r 5 -o json -v
llama-bench -m $model -dev Vulkan0 -sm none -fa on -lzm off -ngl 999 -ot '.*=Vulkan0' -p 64,512 -n 16,128 -pg 256,64 -b 512 -ub 512 -r 5 -o json -v
$compact = 'artifacts/local/models/gemma4-e2b/gemma-4-E2B_q4_0-ple-q2_0-it.gguf'
llama-quantize --allow-requantize --token-embedding-type q6_k --tensor-type '^per_layer_token_embd[.]weight$=q2_0' $model $compact Q4_0 8
llama-bench -m $compact -dev Vulkan0 -sm none -fa on -lzm off -ngl 999 -ot '.*=Vulkan0' -p 512 -n 128 -b 512 -ub 512 -r 5 -o json -v
```

The checkpoint SHA-256 is
`fa401b55b07ee70a54c6dae3903c783a6e65064312529ea57175cb5f8dec6634`.
Local benchmark JSON, Vulkan logs and WDDM samples are under
`artifacts/local/gemma4/e2b-reference/`.

## Reproduce qualification

```powershell
dotnet build Aurelian.Inference.slnx -c Release -m:1
dotnet run --project tools/Aurelian.GemmaProof -c Release -- --compile
python tools/gemma4/tokenizer_oracle.py
dotnet run --project tools/Aurelian.GemmaProof -c Release -- --tokenizer
dotnet run --project tools/Aurelian.GemmaProof -c Release -- --kernels
dotnet run --project tools/Aurelian.GemmaProof -c Release -- --benchmark
dotnet run --project tools/Aurelian.GemmaProof -c Release -- --scheduling
dotnet run --project tools/Aurelian.GemmaProof -c Release -- --block
python tools/gemma4/compare_block.py
dotnet run --project tools/Aurelian.GemmaProof -c Release -- --generate
dotnet run --project tools/Aurelian.GemmaProof -c Release -- --generate artifacts/local/models/gemma4-12b "What is 2 + 2? Answer with only the number." 16 --host-mib 6144
dotnet run --project tools/Aurelian.GemmaProof -c Release -- --generate artifacts/local/models/gemma4-12b "What is 2 + 2? Answer with only the number." 16 --host-mib 6144 --queued
```

Compilation refuses to overwrite an existing artifact; omit `--compile` when
reusing the completed local artifact. Offline oracle scripts use already-installed
NumPy/tokenizers. The tokenizer oracle adapts only the merge-array serialization
for the installed older reference package, preserving the ordered BPE pairs.

Evidence under ignored `artifacts/local/gemma4/` records:

- 137 deterministic tokenizer fixtures, including Unicode, byte fallback,
  whitespace and added chat tokens: exact ID/decode agreement.
- Native Q4 products at widths 32, 96, 3,840, 4,096, 8,192, 15,360 and 32,768,
  with 1/3/5 rows: independent scalar checks plus exact fused BF16 rounding checks.
- Alternating warmed resident-kernel benchmarks for 3,840-to-15,360 and
  15,360-to-3,840 projections, with 1/16 rows. Upload/compilation excluded; raw
  wall/GPU samples are recorded. Decode GPU time improves about 2–2.7 times.
- Grouped attention with a nonzero cache offset and a three-token window: CPU
  comparison, including future/past masking.
- Six consecutive decoder layers against NumPy, including the first global
  attention layer: relative RMS error **0.895%**, below the authored **1.5%** bound.
- Two-token prefill versus incremental cache evaluation through those six layers:
  **byte-identical outputs**.
- Full 48-layer generation: greeting returns **Hello!**, arithmetic returns **4**;
  both reach the actual end-of-turn token.
- Final Aurelian regression: **1,394 tests in 31 assemblies**, no failures/skips;
  the standalone inference build has zero warnings/errors. Focused inference
  tests include artifact rejection, chat parsing, cancellation and failure reset.

This proves a working bounded inference path. It does not qualify full-model
numerical parity, broad answer quality, long contexts, multimodal execution,
sampling strategies or QAT quality preservation. Artifact evidence explicitly
retains `QualityQualified=false` and `FullModelParity=false`.

Reference/provenance: `tools/gemma4/NOTICE.md`, Hugging Face's Apache-2.0 Gemma4
Unified implementation and Google's canonical chat template. Models and tokenizers
remain local assets with their upstream terms.
