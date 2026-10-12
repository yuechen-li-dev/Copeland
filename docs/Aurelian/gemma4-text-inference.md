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
