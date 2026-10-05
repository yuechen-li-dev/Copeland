# M26 proof tool

The tool shares its corpus and runtime runners with the compiler test project. Accepted fixtures compile real optimized C# with checked host arithmetic and invoke `run`; portable fixtures also execute Node in Production and Symbolic emission profiles. Rejected fixtures assert a single diagnostic, code, authored path/offset/length, and useful repair text. `ExpectedFailure` cases compare the actual runtime message.

```powershell
dotnet build tools/copeland-typescript-instinct-m26/InstinctProof.csproj -c Release -m:1
dotnet tools/copeland-typescript-instinct-m26/bin/Release/net10.0/InstinctProof.dll runtime-proof artifacts/copeland-typescript-instinct-m26 artifacts/copeland-typescript-instinct-m26/baseline-batch.g.cs
dotnet tools/copeland-typescript-instinct-m26/bin/Release/net10.0/InstinctProof.dll artifacts/copeland-typescript-instinct-m26/dogfood-execution-inputs.json artifacts/copeland-typescript-instinct-m26/dogfood-execution.json
python tools/copeland-typescript-instinct-m26/summarize.py
```

Summary generation requires the recorded raw build/test logs, execution JSON, and benchmark trials. It checks counts, failures, and checksums before constructing the required artifacts. It is an evidence projection, not an alternate compiler or test oracle. The manifest excludes its own hash and the changing path inventory.

`runtime-proof` measures actual emitted C# sequential/parallel batch modes at 1, 8, 32, 128, 1024, and 16384 items, with cheap and costly bodies. It uses three warmups and five measured trials, total GC allocation, and checksum checks. The optional baseline generated source is compiled unchecked to reproduce the original host behavior. Baseline measurements have one warmed trial per size and should not be treated as statistical performance distributions. Private scheduling fields are harness seams, not language API.

To recreate the baseline, archive commit `0bffd10afe0f9e5f3a5c1281e7713d799f5c0d3c` with `src/Copeland`, `Directory.Build.props`, and `Directory.Packages.props` into a repository-local temporary directory. Put the preserved [harness source](../../artifacts/copeland-typescript-instinct-m26/raw/baseline-harness.cs.txt) and [project](../../artifacts/copeland-typescript-instinct-m26/raw/baseline-harness.csproj.txt) at `tools/InstinctBaseline/Program.cs` and `tools/InstinctBaseline/InstinctBaseline.csproj` within that snapshot. Build with `dotnet build tools/InstinctBaseline/InstinctBaseline.csproj -m:1` in the snapshot. Its `snapshot <fixtures> <output>` command records original diagnostics; `emit <source> <output>` emits original C# for execution with this tool's `execute-emitted` command. No Git reset or modification to the active checkout is needed.

Dogfood transcripts preserve initial sources, each proposed repair, raw compiler diagnostics, and the unchanged CLR dictionary source that required a compiler import-order fix. Replaying recorded sources proves execution, but does not recreate a fresh LLM authoring context. The original baseline replay leaves three concrete blocked cases; they are not counted as successful repairs.
