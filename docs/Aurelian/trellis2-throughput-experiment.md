# TRELLIS.2 compute feedforward and throughput experiment

The subsequent [attention tile pass](trellis2-attention-throughput-experiment.md)
reduces queue/dispatch overhead while retaining byte-identical FP32 results.

The real pot's 5,870-token transformer block runs faster with Dominatus-directed
submission lookahead. The default preserves FP32 arithmetic and the existing
BF16 boundaries. A complete positive 30-block prediction reproduces the
original Vulkan output byte for byte. Cooperative FP16 matrix execution is an
explicit experimental option with separate numerical evidence.

This qualifies a throughput pass, not a new end-to-end generation time. The
original 1,903-second, 12-step shape sampler has not been re-timed. Its remaining
decoder/paging boundary and independent-reference qualification limits remain
documented in [the shape experiment](trellis2-shape-experiment.md).

## What changed

The old affine loop waited separately for each weight upload and compute batch.
Attention also waited after each recorded batch. The compute owner now has one
bounded submission-ring implementation for both paths. Each physical slot owns
its descriptors, parameters and timestamp pair; streamed slots additionally own
their staging and device weight buffers. Copies and their dependent computation
share a command buffer. The existing plant queue and timeline submitter remain
the physical owners.

Dominatus runs explicit `plan` and `feed` states. The known batch tape and upload
budget select depth two when useful, or depth one for a single batch. Submission
and completion facts update the blackboard and a shadow completion estimate.
Predictions are corrected from host-observed completion and measured GPU costs.
They never authorize early reuse: a slot's real Vulkan timeline value must
complete first. Completion feedback is reconciled in submission order.

The design draws on Oct Prometheus's `PX16_M29_SUBMISSION_RING.md` and
`P15_M13_SHADOW_FEEDFORWARD_CANARY.md`. The latter's reservations are advisory;
this experiment implements actual upload/recording overlap through Aurelian's
existing C# Vulkan ownership. It does not import the full Prometheus controller
or claim multi-queue, multi-GPU, cross-block speculative execution, GPU rewind,
or an optimally commissioned controller. The current shadow estimate includes
host harvest delay, so it is not an exact GPU completion timestamp.

Both occupancy and shape sampler commands attach this controller by default.
Other compute clients can supply `IComputeStreamController` without making
the graphics assembly depend on Dominatus. The synchronous path remains the
explicit comparison/control path when no controller is attached. Inspection
is bounded, as are the 512 retained recent feedforward measurements; aggregate
submission/completion counts retain the whole run.

An additional CPU hot-path fix retains the safetensors finite-value check while
constructing its diagnostic string only on failure. Previously every finite
weight value constructed an unused error message. An allocation regression
specimen checks that a selected BF16 read allocates its payload/value arrays
and bounded fixed overhead, rather than a diagnostic per element.

## Native results

RTX 3070; actual image conditioning, original seed/noise and irregular coordinates.
Each block comparison warms both paths and alternates three baseline/staged
trials. Times exclude final qualification readback and use the median.

| Experiment | Synchronous median | Staged median | Speedup | Numerical result |
|---|---:|---:|---:|---|
| Default FP32 staging | 2,548.57 ms | 1,952.87 ms | 1.305x | All output bits identical |
| Staging plus cooperative affines | 2,485.68 ms | 1,494.42 ms | 1.663x | 0.14818% relative RMS; retained 0.5% block bound |

The separate runs have their own paired baselines; do not mix their medians.
The timeline query confirmed two genuinely in-flight submissions, separately
from the count of unharvested slots. Neither metric measures hardware warp
occupancy. GPU timestamps surround the compute stage after upload dependencies;
they are not a general hardware-utilization counter.

Tracked Vulkan allocation high water was 858,748,080 bytes (819 MiB) for the
default block benchmark and 893,218,800 bytes (852 MiB) for the cooperative
benchmark. These include owner buffer staging/parameters and exclude opaque
driver allocations and other GPU applications.

The optional cooperative path uses the already admitted KHR F16/F32 subgroup
tuple. GPU packing pads the real 5,870-row tail to 5,872 and transposes streamed
weight tiles. K=8,192 uses two K=4,096 products with FP32 accumulation and adds
bias once. Unsupported/small/unaligned affine shapes retain FP32. New kernels
remain real Visual TypeScript files (`PackMatrixHalf.v.ts`,
`LinearEpilogue.v.ts`), using the existing cooperative intrinsic.

One large feed-forward projection falls from 1,536 dispatches to 144 and from
approximately 212 ms to 43 ms GPU time. A complete positive prediction differs
from the original FP32 Vulkan result by 0.41567% relative RMS; the largest
inspected hidden-boundary difference is 0.62243% RMS. This is comparison to
the previous native implementation, not an independent upstream oracle.
Guidance can amplify these differences. Cooperative execution therefore stays
opt-in; the full guided trajectory has not been qualified or promoted.

Remaining measured work is concrete: self-attention still takes approximately
650 ms per block and its 132 submissions contain many FP32 K-chunk dispatches.
Weights are also still read, expanded and uploaded again for each evaluation.
Persistent packed-weight residency and a better attention kernel are the next
throughput targets. This pass does not claim the GPU does useful work 100% of
the time.

## Evidence and validation

Local artifacts are under ignored `artifacts/local/trellis2/shape/`:

- `throughput-final/evidence.json`: warmed FP32 performance and bit parity.
- `throughput-cooperative-final/evidence.json`: optional matrix performance and
  numerical bound.
- `flow-staged-comparison.json`: seven inspected block boundaries plus final
  velocity, all bit-identical, with matching diagnostic provenance hashes.
- `flow-cooperative-comparison.json`: retained 1% native-prediction comparison
  bound, with matching weight/input/conditioning provenance.
- `stream-kernels-final/evidence.json`: FP32 tails, cooperative row/output tails,
  K=8,192 accumulation, K=6,144 FP32 fallback, and binary16 packing versus .NET
  rounding on ties/subnormals/maximum finite values. Injected loader/observer
  failures drain in-flight resources, restore live allocation bytes, preserve
  the original exception and leave the owner reusable. Invalid depth rejects
  before ring allocation.
- `optimization-sparse-regression/evidence.json`: five sparse/math cases,
  including staged attention with 5,870 keys and a query tail.
- `optimization-compute-regression/evidence.json`: all 55 existing native
  compute cases passed, including cooperative primitives.

The Release solution and proof tool build with zero warnings/errors. Aurelian
passes 1,362 tests across 30 assemblies; Copeland.TS passes 1,971 tests across
its three assemblies (1,511 frontend, 261 C# backend, 199 JavaScript backend).
No skips. The existing repository Naga executable is selected for WGSL checks;
no tool was downloaded. A concurrent first build encountered Windows proof-DLL
locks and was rerun successfully after that proof completed. A first test run
without `AURELIAN_NAGA` failed the WGSL prerequisite; the configured full run
passed. Frozen Zig suites were not run because no legacy Zig code changed.

The default staging capability is verified in the real learned block and
complete positive prediction. Full 12-step timing and cooperative guided
sampling qualification remain explicitly outside the evidence above.

## Reproduce

```powershell
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet build Aurelian.slnx -c Release -m:1
dotnet test Aurelian.slnx -c Release -m:1 --no-build
dotnet test Copeland.TS.slnx -c Release -m:1

dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --trellis2-stream-kernels
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --trellis2-throughput --output artifacts/local/trellis2/shape/throughput-final
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --trellis2-throughput --cooperative --output artifacts/local/trellis2/shape/throughput-cooperative-final
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --trellis2-shape-flow-probe --staged --output artifacts/local/trellis2/shape/flow-staged
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --trellis2-shape-flow-probe --cooperative --output artifacts/local/trellis2/shape/flow-cooperative

# Optional offline comparison; never called by the C#/Vulkan inference path.
python tools/trellis2/compare_flow_blocks.py `
  --native artifacts/local/trellis2/shape/flow-staged `
  --reference artifacts/local/trellis2/shape/flow-diagnostic `
  --output artifacts/local/trellis2/shape/flow-staged-comparison.json --require-bit-exact
python tools/trellis2/compare_flow_blocks.py `
  --native artifacts/local/trellis2/shape/flow-cooperative `
  --reference artifacts/local/trellis2/shape/flow-diagnostic `
  --output artifacts/local/trellis2/shape/flow-cooperative-comparison.json --maximum-relative-rms 0.01
```

`--trellis2-sample-shape` and `--trellis2-sample-structure` use the default
staged FP32 path. Add `--cooperative` only for the explicit precision experiment,
and use a separate output directory to retain original qualified artifacts.
Model weights, generated assets and numerical dumps remain local/ignored.
