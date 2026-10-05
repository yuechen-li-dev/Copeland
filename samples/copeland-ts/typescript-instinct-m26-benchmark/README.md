# M26 benchmark twins

The eight original kernels and JavaScript implementations came from the supplied `copebench_1.tar.gz`. The C# host invokes Copeland functions emitted by the real MSBuild task. `NativeStrings.ts` and `Maps.ts` add matched native-string and CLR-dictionary/JavaScript-Map kernels. Maps do not qualify a native Copeland map.

From the repository root:

```powershell
dotnet build src/Copeland/Copeland.TS.MSBuild/Copeland.TS.MSBuild.csproj -c Release -m:1
dotnet build samples/copeland-ts/typescript-instinct-m26-benchmark/CopeBench.csproj -c Release -m:1
pwsh -File tools/copeland-typescript-instinct-m26/run-benchmarks.ps1
```

Each benchmark runs in its own process, with three full-size trials. The reported comparison uses the fastest trial, while raw JSON retains every trial. There is no separate untimed warmup. CLR allocation is `GC.GetTotalAllocatedBytes(precise: true)`; Node heap delta is retained heap change, not total allocated bytes. CLR GC counts and both process peak working sets are included. Node GC counts are unavailable. Timing includes result formatting; process startup is excluded. Peak working set includes runtime startup and all trials.

Original algorithm/data layouts are preserved: the objects kernel uses a row array on both sides, and the original strings kernel retains its CLR-detour implementation. The native strings variant still fills, freezes/copies, joins, splits, and hashes identical values in both twins. Different runtime carriers and collection implementations are part of this comparison. These single-process kernel results do not rank whole languages or qualify concurrent application performance.
