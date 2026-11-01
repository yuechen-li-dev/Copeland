# GPU-oriented packet classification

2026-10-30. **Meaningful progression:** a GPU-oriented work decomposition improves
the 1,024-packet, 2,048-rule kernel by approximately 2.96x
and complete pipelined service by 1.58x relative to
the original GPU algorithm. The CPU remains faster for the complete small-batch
workload. The cooperative GPU kernel regresses on other measured shapes; no
universal GPU or ASIC-replacement claim follows.

## The actual ASIC operation

The mathematical task is packet-header matching followed by an action selection:

```
match(packet, rule) = source-prefix matches
                   AND destination-prefix matches
                   AND destination port is in range
                   AND protocol matches
selected_rule(packet) = lowest matching rule index
```

Rule predicates are independent for a fixed packet. Different packets are also
independent under an immutable table snapshot. The final first-match selection
is a reduction. Longest-prefix forwarding is a related operation with longest
prefix selection instead of lowest rule index, while exact-match forwarding
asks for a key lookup. Queue scheduling and shared mutable flow state do not
have the same independence.

Cisco documents distinct LPM, central exact-match and HCAM policy databases in
Silicon One. Hardware lookup mechanisms differ; implementing their decision
contract on a GPU does not require simulating the ASIC's circuits or using its
physical table layout. See [Cisco's architecture](https://www.cisco.com/c/en/us/products/collateral/switches/c9350-series-smart-switches/c9350-series-smart-switches-architecture-wp.html).

This bounded classifier retains our existing four-field policy model: source
IP, destination IP, destination port and protocol. It does not add source-port
rules or claim a complete five-tuple classifier. It continues to parse raw frame
bytes on the GPU, preserves the existing route/TTL/checksum/status behavior, and
uses the independent CPU oracle.

## The GPU-oriented design

The old kernel assigns one invocation to each packet and serially scans that
packet's candidate rules. It exposes packet parallelism but little rule
parallelism. The new Visual TypeScript kernel uses a two-dimensional tile:

* 16 packets per workgroup, with 512 invocations total.
* Headers parsed by 16 adjacent invocations before classification.
* A logical team of 32 invocations per packet; adjacent lanes test adjacent
  candidate rules from the existing structure-of-arrays index.
* Each lane finds its earliest local match; five parallel shared-memory minimum
  steps recover the globally first matching rule.
* Parsed keys use field-major shared storage: parser lanes write adjacent words,
  and rule-team lanes read a common key. Shared keys plus reduction storage use
  3 KiB per workgroup.
* Padding and rejected packets still participate in every workgroup barrier.
  Output is written only by live packet leaders after the last barrier.

The same protocol/port rule index is used by both GPU variants and the optimized
CPU controls. The comparison changes work mapping, group shape and shared-key
layout; it does not replace the policy with an easier one or force a linear
scan of the complete rule table. Both shaders use the same Aurelian allocation,
copy, submission, completion and CPU commit path.

This targets integer SIMT execution, occupancy, contiguous global loads and
shared-memory communication. These are the relevant hardware features for this
prototype. [NVIDIA's best-practices guide](https://docs.nvidia.com/cuda/cuda-c-best-practices-guide/index.html)
discusses coalescing and shared-memory access patterns. Correctness uses existing
Visual TypeScript workgroup storage and barriers and does not assume a physical
subgroup width. The design does not add a compiler intrinsic or bypass VD-MIR.

GPU-oriented classification has prior research, including compact range trees,
bit-vector intersections and shared-memory layouts. This experiment is not a
novelty claim or a reproduction of those papers; see
[Zhou, Singapura and Prasanna, 2014](https://www.ieee-hpec.org/2014/CD/index_htm_files/FinalPapers/131.pdf).
A compiled bitset representation would be another algorithm class, with its
own index-size and update-cost tradeoffs. It is not implemented here.

## Matched results

RTX 3070, Ryzen 7 7700X, Windows, NVIDIA 610.88, .NET 10. Three independent
performance processes alternate variant order after warmup. A separate full
Vulkan-validation process exercises the same implementation. CPU methods are
optimized from first call (`DOTNET_TieredCompilation=0`). No regression/build
jobs overlap the final measurements.

All table rows use four in-flight batches for GPU throughput. Each observation
contains 32 completed batches, including ring fill and drain; all batches finish
before timing stops. Kernel time is measured separately with GPU timestamps.
Complete pipeline service includes host staging, whole-frame upload, command
recording/submission, fences, readback, CPU frame copy/rewrite and queue-index
commit. Input Mpps counts all dispositions, not transmitted packets.

| Packets | Policy rules | Serial GPU kernel us | Cooperative GPU kernel us | Serial pipeline input Mpps | Cooperative pipeline input Mpps | Best CPU input Mpps |
|---:|---:|---:|---:|---:|---:|---:|
| 1,024 | 0 | 4.03 | 5.22 | 41.70 | 39.14 | 97.15 |
| 1,024 | 2,048 | 26.34 | 8.90 | 22.34 | 35.27 | 84.97 |
| 8,192 | 0 | 3.07 | 10.24 | 82.78 | 85.55 | 127.26 |
| 8,192 | 2,048 | 25.60 | 29.70 | 102.29 | 105.23 | 110.56 |

Values are medians of per-process summaries. The best CPU rate selects the
fastest of indexed scalar, indexed SIMD, eight-worker SIMD and all-worker SIMD
within each process before taking the median.

For the 1,024-packet, 2,048-rule case, per-round GPU kernel improvements are
2.96x, 2.96x, 2.95x; complete pipeline improvements
are 1.55x, 1.58x, 1.59x. The smaller-batch
algorithm improvement survives actual data movement and completion costs.
It still does not establish a complete-path win over the CPU.

The cooperative approach increases rule parallelism when packet parallelism is
limited, but spends more invocations and synchronization per packet. At 8,192
packets its kernel is slower than the original kernel; zero-rule cases also show
the cooperative overhead. Larger-batch full-path differences are small relative
to variation and host costs. Do not select a universal winner from those medians.
No automatic selector or adaptive policy was added from only these four shapes.

Changing work mapping and shared layout together is an algorithm comparison,
not a causal profile of each hardware mechanism. The field-major layout did not
establish a separate measurable win over the first cooperative layout in the
available observations. No claim of peak GPU utilization follows.

## Correctness and qualification

Each lane validates complete outputs against the independent unindexed CPU
oracle. The same packet/table/output hashes match the earlier experiment.
Fifteen count/seed checks cover packet counts 1, 7, 31, 65 and 4,099 across three
seeds, including incomplete packet teams and workgroups. Five explicit priority
specimens cover matching rules 31/32, 32/63, only 63, no match, and permit rule 0
before deny rule 63. Poisoned device output catches missing writes.

Each of the four qualified lanes contains 512 group samples (16 per coordinate),
32 coordinate/slot output checks, 15 tail/seed checks and five priority checks.
Each group sample contains 32 completed batches. Three lanes provide performance
evidence; the other is validation-only. The integrity reader rejects mismatched
code, shader or fixtures, missing samples/controls, invalid timings and incomplete
priority/tail checks. Separate sustained process-CPU profiles are retained but
are not used to infer total-system power.

Full Aurelian Release build and the 1,526-test / 33-assembly regression passed.
The final cooperative shader also compiled through Copeland -> VD-MIR -> HLSL ->
DXC -> validated SPIR-V and executed with the Khronos validation layer enabled.
The final native logs contain no VUID/validation-error messages.

The exploratory `cooperative-field-major` run overlapped the regression build
and was stopped. Its `REJECTED.md` excludes it from final results. Final evidence
is solely `artifacts/local/router-research/cooperative-final/`, collected after
regression completion. Frozen shader/host sources, emitted HLSL/SPIR-V, raw batch
and device times, process CPU observations and hashes are retained there. The
earlier cooperative layout remains separately under `cooperative-qualified`;
its measurements are not pooled into this report.

The portable [results ledger](router-gpu-classification-results.json) contains
every final per-round result and raw-run hashes. See also the
[original viability study](router-viability-experiment.md) for the host-memory
scope, coherent-cached readback fix and physical-network qualification boundary.

Reproduction requires fresh output directories and no overlapping GPU/build jobs:

```powershell
./tools/Aurelian.RouterResearch/Run-Algorithms.ps1 -Output artifacts/local/router-research/algorithm-reproduction
python tools/Aurelian.RouterResearch/summarize_streaming.py artifacts/local/router-research/algorithm-reproduction artifacts/local/router-research/qualified2 --algorithms
```

The second argument supplies the primary frozen packet/table/oracle hashes.
An independently generated primary run with the same fixtures is also accepted.
These measurements remain synthetic host-memory packet processing; live NIC
forwarding, loss, real arrival-to-egress latency, arbitrary LPM and ASIC TCO are
unqualified.
