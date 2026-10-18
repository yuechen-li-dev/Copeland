# JavaScript emission benchmark

Run from the repository root with .NET 10 and Node:

```powershell
dotnet run --project tools/Copeland.Benchmarks.Emission -c Release
```

The tool compiles the maintained `Application`, `Tables`, `Flow`, and
`AsyncBatchGenerator` burn-in sources. It compares Production JavaScript emission
with generated-definition reachability disabled/enabled and requires identical
Node output for each pair. It records emitted bytes, reachable/removed definition
counts, output hashes, C# emission size, emission timings, and Node startup timings.
C# execution is not part of this tool's parity claim.

Results belong in ignored `artifacts/local/copeland-emission-benchmark/`.
Generated JavaScript is isolated under a unique `generated/<run-id>` directory
and removed after a successful run; failed runs retain diagnostic material.
The compact manifest and detailed measurements remain local. Timings depend on
the machine and protocol; do not copy historical performance claims into a new run.

This is the maintained form of `Copeland.CtsOptM2.Measure`. The retired
`cts-opt-m1-benchmark.ps1` measured frozen earlier JavaScript; the retired
`cts-opt-m1-measure.ps1` compiled synthetic table shapes and estimated payload
size from source literals. Their historical reports remain records. This tool
compares fresh output on maintained fixtures; it does not reproduce either
earlier measurement protocol or provide a table-dimension scaling sweep.
