# CPU/GPGPU router-task viability experiment

2026-10-30. Outcome: **meaningful progression**. A pipelined CPU/GPGPU packet-policy
path delivers 105.74 million input packets/s versus 109.43 million
for the strongest measured CPU control, with approximately 5.06x
less application-process CPU time per input packet. This is evidence for a useful
accelerator tradeoff on a bounded host-memory workload. Physical inline routing
and economic superiority remain unqualified.

The experiment also removed a real Aurelian bottleneck: GPU-to-CPU allocation now
prefers coherent CPU-cached memory, with the existing coherent fallback. This
changes memory selection in its owning allocator; packet code does not implement
a separate allocation or submission engine.

## Architectural follow-up: overlap batches

The first 43-coordinate experiment measured one completed GPU batch at a time.
Its negative throughput result isolated a large handoff cost. A bounded follow-up
therefore keeps the **same packet bytes, policies, output oracle and shader** and
varies GPU admission depth (1, 2, 4, 8) on the 1,024/8,192-packet, zero/2,048-rule
fixtures. Hash checks match all four fixtures to the primary matrix.

Every sample contains 32 complete batches, including pipeline fill and drain.
All work finishes before the sample ends. Throughput divides total completion
time by 32; each batch also has a separate submit-through-host-commit latency.
Latency includes time in the in-flight ring. It does not include NIC arrival,
batch filling from a network, physical egress or downstream queueing.

Separate slots own independent input/output/staging buffers, descriptor sets,
timestamp queries and timeline fences. Aurelian's normal asynchronous submitter
retires recorded commands against their actual completion fences. The existing
prepared-command retention contract is synchronous-only, so the follow-up records
commands through the ordinary pool rather than bypassing that contract. Recording,
submission, waits, whole-frame upload, decision readback and CPU commit are all
inside measured service.

Final qualification uses optimized CPU code from the first call with
`DOTNET_TieredCompilation=0`. An earlier pilot exposed a tiered-JIT warmup confound
on its first CPU coordinate; that pilot is retained and excluded from final
comparisons. The final follow-up has its own code/binary hashes, three fresh
performance processes and a separate complete Vulkan-validation run. It compares
CPU and GPU within those runs; it does not combine CPU times from the primary
binary with GPU times from the follow-up binary.

For 8,192 packets per batch, eight candidate paths and a 2,048-rule policy:

| Implementation | Input Mpps | Worst round batch p95 us | Application CPU ms / million inputs |
|---|---:|---:|---:|
| scalar | 33.01 | 261.30 | 30.71 |
| simd | 37.26 | 227.40 | 22.18 |
| parallel-all | 109.43 | 98.10 | 48.23 |
| gpu-depth1 | 48.95 | 213.20 | 10.28 |
| gpu-depth2 | 89.63 | 261.40 | 9.69 |
| gpu-depth4 | 105.74 | 441.00 | 9.54 |
| gpu-depth8 | 101.48 | 838.80 | 9.44 |

Rates and CPU costs are medians of three per-process summaries. Four in-flight
batches reach 96.63% of the strongest CPU control's median
rate. Per-round GPU/CPU throughput ratios are
1.000, 0.966, 0.966.
That is comparable throughput, not a statistically established throughput win.
The original >=1.25x throughput-advantage gate is still unmet.

The resource result is different: the application-process CPU cost drops from
48.23 to 9.54 ms per million inputs, approximately
5.06x. Its sustained profiling phases averaged approximately
5.55 logical CPU cores of process
time for the CPU control versus 1.00
for the GPU path. CPU time can exceed wall time because multiple CPU workers run
concurrently. Every resource phase lasts at least 250 ms to exceed the process
CPU clock's quantum, and stays separate from interleaved latency/throughput samples.

This demonstrates the intended flexibility/performance tradeoff: the GPU can
perform this bounded ASIC-like packet policy at roughly the multicore CPU rate
while the application consumes much less CPU time. That could matter when CPU
capacity is needed for other work. System-wide driver/interrupt CPU costs, GPU
power and the value of spare CPU capacity were not measured. This is not a total
energy, co-tenancy isolation or financial-return claim.

Depth eight adds latency without improving throughput here. Across the four
follow-up workloads, none establishes a consistent GPU throughput win over the
best CPU control. The complete [streaming ledger](router-viability-streaming-results.json)
retains all depths, per-round throughput, in-flight latency and process CPU costs.
Raw evidence and frozen source copies are under
`artifacts/local/router-research/streaming-qualified/`.

## Blocking-path experiment

43 coordinates, seven implementations/controls, 80 samples per coordinate,
three fresh performance processes, and a separate complete Vulkan-validation
process ran on an AMD Ryzen 7 7700X (8 cores / 16 logical processors), RTX 3070
(8 GiB), Windows, NVIDIA driver 610.88, and .NET 10.0.14.

The primary result includes raw frame upload, GPU parsing/policy/route decision,
completion, decision readback, and CPU frame copy/TTL/checksum/queue-index commit.
CPU controls parse the same frames and perform the same commit. The independent
oracle scans unindexed rules and original route candidates. CPU performance
controls include indexed scalar, indexed Vector256, eight-worker SIMD and
all-16-worker SIMD. Both optimized CPU and GPU use the same ordered ACL index
and precomputed snapshot route choices.

| Packets/batch | ACL rules | Best CPU service us | GPU kernel us | Full GPU service us | GPU input Mpps |
|---:|---:|---:|---:|---:|---:|
| 1,024 | 2,048 | 14.50 | 39.86 | 111.05 | 9.22 |
| 8,192 | 0 | 62.60 | 8.30 | 260.70 | 31.42 |
| 8,192 | 2,048 | 81.55 | 36.94 | 180.95 | 45.27 |
| 65,536 | 2,048 | 708.15 | 254.00 | 2341.25 | 27.99 |

Values are medians of process medians. Best CPU service is the median of the
fastest CPU control in each process. GPU kernel time is the timestamp inside
the full GPU path and excludes upload/readback/commit. It is diagnostic and is
not a matched speedup comparison against the complete CPU path. The ledger
retains every per-round result and all 43 cases, including losses.

For the 8,192-packet, 2,048-rule case, GPU batch service p95 was at most
343.10 us across the three rounds. The median GPU service
rate was 45.27 million **input** packets/s; the best CPU rate
was 100.45 million input packets/s. Per 8,192 inputs, this
fixture produced 7,514 forward decisions, 443 queue decisions, 65 malformed,
121 TTL-expired, 25 route misses and 24 policy denials. There was no transmitted
traffic in this measurement.

19 cases passed the predeclared host-memory usefulness gate (>=1 million input
packets/s in every round, sampled batch-service p95 <=1 ms, batch <=8,192).
**Zero** cases passed the additional GPU-advantage gate (>=1.25x the best measured
CPU in every round). No coordinate had a GPU advantage consistently across all
three rounds, even without the 1.25x margin. This evidence supports general-compute
packet-policy viability and rejects the tested blocking GPU offload as a throughput upgrade
over these CPU controls.

## The blocker removed and the cost that remains

The original allocator selected the first host-visible, coherent memory type
for both uploads and readbacks. On this device that readback type was uncached.
The new default prefers host-visible, coherent, cached memory for readback;
upload/device-local policies are unchanged. Noncoherent cached memory is not
admitted, so the existing coherency contract requires no new invalidate path.

The same-process ablation records original and cached memory flags and uses the
same shader, fixtures, staging/copy shape, completion path and host commit.
For the 8,192-packet, 2,048-rule fixture, full service improved from
888.60 to 180.95 us by the median-of-round-medians measure. Individual
round speedups were 3.50x, 4.93x, 4.91x.
Mapped CPU read/copy time fell from a median 718.90
to 11.65 us. These mapped-read samples include
managed result allocation and the copy into the decision array.

After that fix, the same fixture still required about 181 us for a complete GPU
batch versus about 82 us for the CPU. Resident dispatch wall service was about
93.70 us with resident kernel time
about 39.14 us. This isolates a
substantial submission/completion/query cost in addition to transfer and commit.
It does not by itself assign that cost to a particular driver function.

Cheap packet decisions leave relatively little work with which to amortize
offload. The CPU also eliminates packet-independent route scoring at snapshot
compilation time; the GPU receives the same optimization. Accelerating repeated
scoring that a CPU can precompute would have manufactured a misleading win.

## Scope and remaining qualification

This implements Ethernet/IPv4 validation, fixed 20-byte IPv4 headers,
unfragmented TCP/UDP classification, ordered source/destination prefix and
destination-port/protocol ACLs, direct /24 route indexing, snapshot candidate
selection, TTL/checksum rewrite, and ordered host queue-index output. The
[protocol and reproduction guide](../../tools/Aurelian.RouterResearch/README.md)
defines the exact admitted packet model and policy bounds.

DragonGod's [sample](https://github.com/yuechen-li-dev/DragonGod/tree/main/samples/dragon_router)
provides the behavioral reference for congestion-based choice, lower-port tie
breaking and unavailable-path queue decisions. This is not a port or performance
comparison of its complete sequential retry/drain runtime. Packet-independent
route choices are compiled once per immutable snapshot. Snapshot generations
reject stale decisions; they do not reserve finite egress capacity.

The concrete next boundary is the physical data path: there is no NIC RX/TX
integration, next-hop MAC/neighbor realization, capacity-aware persistent egress
queue, offered-load generator or loss/arrival-to-egress measurement. Recorded
wired Realtek adapters had no link; traffic used here was deterministic host-memory
fixtures. GPU-resident and host-memory service rates cannot establish wire rates.
The CPU/GPU handoff cost is a measured obstacle on the blocking path; overlapping
batches amortizes it while increasing in-flight completion latency. NIC-direct
processing would require its own implementation and qualification.

This study measures a bounded subset of traditional packet hardware work. It
does not qualify arbitrary LPM, IPv6/options/fragments, transport protocol stacks,
stateful NAT, dynamic backpressure, deferred retry semantics or QoS scheduling.
Traffic and policies are synthetic, tables and buffers are warm, policy uploads
are setup costs, and workstation clocks were unmanaged. The 65,536-packet rows
show substantial between-process variance; these are not latency guarantees.
Single-packet CPU samples average 64 calls to exceed timer resolution. Other
coordinates and all GPU samples measure one completed batch per sample.

The economic hypothesis remains conditional. CPU-only processing has the highest
measured throughput; the four-deep GPU path offers a measured application-CPU
resource tradeoff at comparable throughput. Software ownership and changing policy can
have value, but this experiment does not price engineering/support, servers,
NICs, GPUs, power, redundancy or comparable ASIC appliances. Idle before/after
GPU telemetry is not an energy measurement. Purchasing a GPU specifically for
this workload requires a separate cost/power and deployment comparison.

## Qualification and provenance

* Full final Release build: passed, zero warnings/errors.
* Full Aurelian regression: 1,526 tests passed across 33 assemblies, including
  seven new memory-selection tests. The repository-local Naga 27.0.0 executable
  was supplied through `AURELIAN_NAGA` after an initial missing-PATH failure.
* Vulkan validation was required and enabled. The complete validation lane and
  three performance lanes each passed 363 correctness/transition checks and
  retained 24,080 timing samples. Performance evidence totals 72,240 samples;
  validation contributes a separate 24,080.
* Known outcomes include malformed/fragmented packets, TTL expiry, route miss,
  queueing, first-match allow/deny, deterministic path ties and the IPv4 checksum
  one's-complement zero boundary. Rewritten checksums are independently recomputed.
* Multiple fixture seeds, changed packet contents at the same GPU addresses,
  blocked/recovered snapshots, poisoned outputs and stale-generation rejection
  exercise output integrity and snapshot transport.
* Streaming follow-up: 512 group samples per lane, 32 completed batches per
  group, 32 full coordinate/slot-output checks per lane, four admission depths
  and four CPU controls. Three performance lanes plus separate validation
  passed source/binary/shader/fixture and sample-integrity checks. Sustained
  process-CPU resource profiles are separate from throughput samples.
* Seven real-evidence mutation checks passed: duplicate samples, missing CPU
  controls, changed fixture hashes, missing correctness checks, invalid timings
  and changed experiment coordinates are rejected.
* The initial exploratory run is retained and rejected by the summary gate
  because singleton CPU samples hit timer resolution. The corrected measurement
  and memory ablation were run in fresh directories. No rejected timings enter
  the final ledger.

The portable [results ledger](router-viability-results.json) contains all per-round
summaries, source/binary hashes and raw-run hashes. Raw samples, packet/table/oracle
fixtures, shader HLSL/SPIR-V/disassembly, machine metadata and frozen authoring
sources remain under `artifacts/local/router-research/qualified2/`. Final build
and test logs are `artifacts/local/router-research/aurelian-build-complete.log` and
`artifacts/local/router-research/aurelian-test-complete.log`.

Reproduce with a fresh output path:

```powershell
$env:AURELIAN_NAGA = (Resolve-Path artifacts/local/webgpu-tools/bin/naga.exe).Path
dotnet build Aurelian.slnx -c Release -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test Aurelian.slnx -c Release --no-build -m:1 -nr:false
./tools/Aurelian.RouterResearch/Run-Research.ps1 -Output artifacts/local/router-research/reproduction
python tools/Aurelian.RouterResearch/test_integrity.py artifacts/local/router-research/reproduction -v
./tools/Aurelian.RouterResearch/Run-Streaming.ps1 -Output artifacts/local/router-research/stream-reproduction
python tools/Aurelian.RouterResearch/summarize_streaming.py artifacts/local/router-research/stream-reproduction artifacts/local/router-research/reproduction
```

## Complete comparison

The final column is the minimum per-round GPU speedup relative to that round's
best CPU control. Values below 1 mean the CPU won at least one round. GPU p95 is
the worst of the three sampled round p95s. All values are microseconds unless
otherwise specified.

| Coordinate | Best CPU service | Full GPU service | GPU worst round p95 | Minimum GPU speedup |
|---|---:|---:|---:|---:|
| n1-c1-r0-mixed | 0.29 | 55.40 | 65.30 | 0.00x |
| n64-c1-r0-mixed | 12.70 | 57.30 | 63.40 | 0.02x |
| n1024-c1-r0-mixed | 11.80 | 64.70 | 73.40 | 0.18x |
| n8192-c1-r0-mixed | 69.95 | 138.25 | 204.90 | 0.49x |
| n65536-c1-r0-mixed | 604.15 | 976.15 | 4558.10 | 0.60x |
| n1-c1-r64-mixed | 0.03 | 84.10 | 111.40 | 0.00x |
| n64-c1-r64-mixed | 1.10 | 91.55 | 113.70 | 0.01x |
| n1024-c1-r64-mixed | 12.70 | 120.00 | 136.10 | 0.10x |
| n8192-c1-r64-mixed | 91.05 | 280.10 | 313.40 | 0.27x |
| n65536-c1-r64-mixed | 624.95 | 2275.00 | 2606.00 | 0.27x |
| n1-c1-r512-mixed | 0.03 | 119.75 | 133.20 | 0.00x |
| n64-c1-r512-mixed | 1.30 | 118.70 | 129.40 | 0.01x |
| n1024-c1-r512-mixed | 14.30 | 133.40 | 142.70 | 0.11x |
| n8192-c1-r512-mixed | 79.65 | 264.20 | 323.90 | 0.28x |
| n65536-c1-r512-mixed | 642.85 | 2272.25 | 2659.70 | 0.28x |
| n1-c1-r2048-mixed | 0.03 | 117.10 | 136.40 | 0.00x |
| n64-c1-r2048-mixed | 1.40 | 118.35 | 128.70 | 0.01x |
| n1024-c1-r2048-mixed | 14.60 | 111.05 | 134.00 | 0.10x |
| n8192-c1-r2048-mixed | 82.65 | 184.20 | 248.70 | 0.45x |
| n65536-c1-r2048-mixed | 755.25 | 1307.20 | 5693.70 | 0.57x |
| n1-c8-r0-mixed | 0.03 | 103.80 | 113.30 | 0.00x |
| n64-c8-r0-mixed | 0.90 | 102.50 | 107.30 | 0.01x |
| n1024-c8-r0-mixed | 11.55 | 120.15 | 125.90 | 0.09x |
| n8192-c8-r0-mixed | 62.60 | 260.70 | 311.70 | 0.24x |
| n65536-c8-r0-mixed | 543.80 | 2154.70 | 3581.30 | 0.24x |
| n1-c8-r64-mixed | 0.03 | 108.00 | 122.50 | 0.00x |
| n64-c8-r64-mixed | 1.10 | 114.10 | 118.40 | 0.01x |
| n1024-c8-r64-mixed | 13.25 | 126.15 | 130.70 | 0.10x |
| n8192-c8-r64-mixed | 68.90 | 261.25 | 323.40 | 0.26x |
| n65536-c8-r64-mixed | 588.75 | 2277.05 | 3688.10 | 0.25x |
| n1-c8-r512-mixed | 0.03 | 116.00 | 133.80 | 0.00x |
| n64-c8-r512-mixed | 1.40 | 124.00 | 131.10 | 0.01x |
| n1024-c8-r512-mixed | 14.60 | 131.40 | 145.60 | 0.11x |
| n8192-c8-r512-mixed | 82.80 | 269.10 | 336.50 | 0.29x |
| n65536-c8-r512-mixed | 663.65 | 2283.20 | 3854.10 | 0.23x |
| n1-c8-r2048-mixed | 0.04 | 110.40 | 174.60 | 0.00x |
| n64-c8-r2048-mixed | 1.30 | 127.30 | 160.70 | 0.01x |
| n1024-c8-r2048-mixed | 14.50 | 111.05 | 151.50 | 0.10x |
| n8192-c8-r2048-mixed | 81.55 | 180.95 | 343.10 | 0.30x |
| n65536-c8-r2048-mixed | 708.15 | 2341.25 | 5436.00 | 0.30x |
| n8192-c8-r2048-early | 76.85 | 320.40 | 1078.10 | 0.16x |
| n8192-c8-r2048-late | 73.20 | 300.25 | 446.20 | 0.18x |
| n4099-c8-r512-mixed | 44.65 | 242.05 | 316.80 | 0.14x |
