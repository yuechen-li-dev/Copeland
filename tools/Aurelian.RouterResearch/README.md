# Router viability research

This is an executable CPU/GPGPU viability experiment, not a production router.
Visual TypeScript owns the GPU packet policy. Copeland binds it to VD-MIR;
Aurelian emits HLSL, DXC produces validated SPIR-V, and Aurelian's Vulkan owners
allocate, submit, fence and timestamp the work. The tool has friend access to
existing graphics internals so research does not require a new public runtime API.

## Predeclared question and design

Can general compute process a bounded useful routing/policy workload, and does
GPU acceleration improve the complete host-memory path over competent CPU controls?
The motivating reference is DragonGod's deterministic candidate selection,
unavailable-path queue decision and deterministic tie breaking:
https://github.com/yuechen-li-dev/DragonGod/tree/main/samples/dragon_router

The design fixes packet counts 1, 64, 1024, 8192 and 65536; candidate counts 1 and
8; and ordered ACL counts 0, 64, 512 and 2048. Mixed traffic includes ordinary
random addresses plus packets constructed to match the first or last rule
(earlier overlapping matches still take precedence). Separate early/late lanes
and an awkward 4099-packet shape expose match position and dispatch bounds.
All generated fixtures and hashes are retained. No best-case-only reporting.

The primary CPU controls are indexed scalar, indexed Vector256 and indexed
Vector256 with eight workers and all available logical processors. Both GPU and CPU receive the same
protocol/port-bucket rule index, preserving rule order. A separate unindexed
oracle verifies index correctness. There is no claim these controls are the
best possible DPDK, SIMD or compiled decision-tree implementations.

Each packet is a 64-byte slot containing a 60-byte Ethernet frame without FCS,
plus four storage-padding bytes. The bounded parser accepts Ethernet IPv4 with
20-byte IPv4 headers, unfragmented TCP/UDP, valid IPv4 checksum, and total IP
length fitting that frame. Routes cover 255 /24s under 10.0.0.0/16; the final
/24 and other networks miss. This is direct prefix indexing, not arbitrary LPM.
Forwarding chooses the usable candidate with least congestion, then lowest port
ID. Because scores are packet-independent within a snapshot, both optimized CPU
and GPU paths use a precomputed route-choice table. The oracle independently
scans candidates. This avoids manufacturing GPU work that the CPU can eliminate.
Ordered IPv4 source/destination prefix, destination-port range and protocol
rules allow or deny. No rule match permits by default.

The GPU consumes raw frames, not CPU-extracted tuples. Host-owned commit copies
frames, rewrites TTL/checksum for forwarding, and records queue indices in input
order. Generation mismatch rejects stale results. Immutable per-batch port state
does not reserve finite egress capacity. DragonGod's per-packet ticks, deferred
retry loop, dynamic backpressure and actual packet transmission are not emulated.
MAC/neighbor resolution and Ethernet next-hop rewriting are also outside this
packet-policy experiment.

## Measurement boundaries and acceptance

* `gpu-resident`: timestamped kernel plus separately recorded host submission
  latency; no upload, readback or commit. Diagnostic only.
* `gpu-transfer-commit`: host staging write, whole-frame upload, dispatch,
  completion, decision readback, host copy/rewrite/queue-index commit. One
  submission, retained staging and recorded commands. Blocking completion;
  no busy-spin budget. Policy upload is setup cost, measured separately from
  steady-state packet service and not a claim about frequent policy updates.
* `gpu-original-readback`: the identical full path using the allocator's original
  first-compatible memory selection. The default now prefers coherent CPU-cached
  readback memory when available, with the original compatible fallback. Memory
  flags and mapped CPU read time are retained for this paired ablation.
* CPU variants: raw-frame parse, policy, decision and the identical host commit.

80 samples per coordinate alternate variant order after 250 ms warmup. Run three
fresh processes serially; run validation separately. No simultaneous GPU jobs.
Retain wall samples and GPU timestamps. Source and binary hashes bind each run.
Working-source changes during a run reject it. SIMD uses eight-rule vectors;
the GPU uses the maximum bucket count as its workgroup-uniform finite loop bound
and masks completed/matched packet scans. This uses the existing compute loop
contract without introducing a shader-language extension. CPU process time is coarse;
individual sample CPU percentages are not meaningful.

One-packet CPU samples average 64 complete calls to exceed timer resolution;
other coordinates and all GPU samples measure one completed batch per sample.
CPU singleton values therefore describe amortized service, not latency tails.
The first exploratory run was rejected by the integrity gate for zero-duration
singleton CPU samples. It is retained under `artifacts/local/router-research/qualified`;
the corrected protocol and memory-selection ablation use a fresh output directory.

An initial **host-memory usefulness gate**, chosen before measurements, is at
least 1 million input packets/s and sampled p95 batch service <= 1 ms at batch
size <= 8192. A **GPU advantage gate** additionally requires >= 1.25x the fastest
measured CPU control at the same coordinate in every independent round. These
are research gates, not network latency SLAs or proof of appliance economics.
Report all results whether either gate passes or fails.

Batch service is not packet arrival-to-egress latency. Inputs are preloaded;
there is no NIC, arrival queue or batch-fill delay in measured service. Only
the retained raw samples may support subsequent explicitly modeled queueing.
No ASIC comparison, packet-loss qualification, full-system power measurement,
or cost advantage is implied. General-purpose compute is not free: server,
NIC, GPU, power, engineering, support and redundancy all belong in a later TCO.

## Run

```powershell
dotnet build tools/Aurelian.RouterResearch -c Release -m:1 -nr:false -p:UseSharedCompilation=false
dotnet run --project tools/Aurelian.RouterResearch -c Release --no-build -- artifacts/local/router-research/smoke --quick --validation
dotnet run --project tools/Aurelian.RouterResearch -c Release --no-build -- artifacts/local/router-research/validation --validation
dotnet run --project tools/Aurelian.RouterResearch -c Release --no-build -- artifacts/local/router-research/round1
```

Use fresh directories for every run. `run.json` stays rejected until every
coordinate and correctness check completes. Shader source, HLSL, SPIR-V and
disassembly plus packet/table fixtures are kept with the evidence.

For the complete validation lane plus three serial performance processes and
integrity-checked summary, run `./tools/Aurelian.RouterResearch/Run-Research.ps1`.
Full repository regression is separate from timing:

```powershell
dotnet build Aurelian.slnx -c Release -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test Aurelian.slnx -c Release --no-build -m:1 -nr:false
```

## Overlapping-batch follow-up

`Run-Streaming.ps1` runs the same 1024/8192-packet, zero/2048-rule fixtures
through four CPU controls and GPU rings of depth 1, 2, 4 and 8. Every observation
contains 32 completed batches, including pipeline fill and drain. Throughput is
amortized over all 32; per-batch completion latency includes time in the ring.
There is still no NIC arrival or transmission measurement.

Each GPU slot owns independent buffers, descriptors, query pool and fence. The
ordinary Aurelian asynchronous submission path records and retires commands
against real timeline completion before reuse. Prepared command retention is
synchronous-only and is not bypassed. In-flight slots reject early reuse.

The final follow-up explicitly disables .NET tiered compilation, compiling CPU
methods with optimization from the first call. An earlier pilot's first CPU
coordinate was still in tiered JIT code; its apparent GPU win is excluded.
The script restores the caller's environment afterward.

A separate sustained >=250 ms phase per coordinate measures application-process
CPU time, not whole-system CPU or power. It stays separate from interleaved
throughput/latency samples. Match source/binary/shader hashes and primary fixture
hashes before interpreting the follow-up:

```powershell
./tools/Aurelian.RouterResearch/Run-Streaming.ps1 -Output artifacts/local/router-research/stream-reproduction
python tools/Aurelian.RouterResearch/summarize_streaming.py artifacts/local/router-research/stream-reproduction artifacts/local/router-research/reproduction
```

The [qualified report](../../docs/Aurelian/router-viability-experiment.md) retains
the blocking-path losses and the pipeline's application-CPU tradeoff. Frozen
primary sources are kept with `qualified2`; the streaming extension has its own
source/binary hashes and frozen source copies.
