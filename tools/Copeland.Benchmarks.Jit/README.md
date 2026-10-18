# Copeland JIT benchmarks

This maintained tool compiles five source workloads with the current Copeland
compiler, emits C# and JavaScript, executes both, checks their checksums, and
records cold/warm measurements. Workload source lives in `Workloads/`; generated
hosts and measurements belong in ignored local output.

From the repository root:

```powershell
dotnet run --project tools/Copeland.Benchmarks.Jit --configuration Release -- --output artifacts/local/copeland-jit-benchmarks
```

For a quick execution check rather than a performance measurement:

```powershell
dotnet run --project tools/Copeland.Benchmarks.Jit -- --output artifacts/local/copeland-jit-smoke --cold-runs 1 --warmup-rounds 1 --measured-rounds 1
```

Requires .NET 10 and Node. `--javascript-profile` accepts `symbolic` (the
default) or `production`. Output includes environment/protocol metadata,
checksums, generated sources/hosts, timings, and allocation observations. A
one-trial smoke run establishes execution only; it is not performance evidence.

The historical tool name was `CtsJitM0`. Historical measurements and reports
retain that name; the active project, namespace, generated host, and tests now
use responsibility-based names. The workload and measurement policy are unchanged.
