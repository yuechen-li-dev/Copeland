# Oblivion xUnit Function Card — M20f

## Mental model

The Function Card combines a bounded callable unit, inline notebook presentation, and ordinary xUnit execution. Oblivion owns Card identity, selection, orchestration, session state, presentation, navigation metadata, and provenance. Copeland owns `.tsxtest` authoring, compilation, generated wrappers, and `#line` mapping. xUnit and Test Platform own discovery, Fact/Theory semantics, case expansion, filtering, assertions, execution, timing, and failure reporting.

M20f does not introduce another execution runtime or assertion model.

## Durable semantic state

The durable Card kind is `function`. Its source is:

```toml
[function]
kind = "copeland-xunit"
reference = "source/FunctionCardExecution.tsxtest"
test = "passed_result_preserves_xunit_authority"
```

`reference` is workspace-relative, must remain inside the vault, and must end in `.tsxtest`. `test` is one exact authored function name. The vault does not store a generated assembly path or last execution result.

## Session execution state

`OblivionSessionState.FunctionExecutionByCardId` stores `NotRun` by absence and explicit `Running`, `Passed`, `Failed`, `Skipped`, or `Error` results. A result includes discovered identity, runner-provided duration, case counts, failure details, source location, source SHA-256, runner identity, completion time, and bounded diagnostics. Reload clears Function results; it never persists them.

## Materialization and discovery

The App resolves the `.tsxtest` file and its unique nearest project inside the workspace. It runs the existing Copeland build, which executes `CopelandCreateSameProjectTestProject` and creates `obj/CopelandTests/<assembly>.CopelandTests.csproj`. It builds that auxiliary project with `CopelandAuxiliaryTestBuild=true`.

Discovery is ordinary Test Platform discovery:

```text
dotnet test <materialized-project> --no-build --no-restore --list-tests
```

Oblivion selects discovered identities whose method component exactly matches the authored test selector. Zero matches is `OBLIVION-FUNCTION-TEST-NOT-DISCOVERED`; multiple base identities is `OBLIVION-FUNCTION-TEST-AMBIGUOUS`. A Theory remains one Card; discovery reports its runner-expanded case count.

## Exact execution and structured result

The App invokes `dotnet` directly with `ProcessStartInfo.ArgumentList`, a two-minute bound, redirected bounded output, and process-tree termination on timeout. It filters with the exact discovered fully-qualified identity and requests TRX:

```text
dotnet test <materialized-project>
  --no-build --no-restore
  --filter FullyQualifiedName=<discovered-identity>
  --logger trx;LogFileName=result.trx
```

TRX, not pretty console output, supplies test outcomes, test duration, failure message, stack, and case totals. `Failed` means xUnit executed the test and reported a failure. `Error` means source resolution, build, discovery, runner, timeout, or TRX infrastructure failed before a valid test outcome.

The maintained failure proves the user-facing location is `ControlledFailure.tsxtest:8`, the authored function line, and not generated `.g.cs`. Copeland wrapper `#line` mapping was tightened so its wrapper frame names the authored function line; Oblivion chooses that authored wrapper frame from xUnit's structured failure stack.

## UI and CLI

Collapsed Function Cards show title, source/type badges, status when present, and no fake body preview. Expanded Cards show exact source/test identity, Fact/Theory metadata, Run, status, duration, case summary, and bounded failure/source details. Duplicate Run is disabled while `Running`. The Avalonia Run button and CLI use the same `OblivionApplication.BeginFunctionCardRun` / `CompleteFunctionCardRun` operation.

Agent control is:

```text
oblivion card show <card-id> -w <vault> --json
oblivion function run <card-id> -w <vault> --json
oblivion command run function.run -w <vault> --json
```

`card show` performs discovery but never execution. `card content` returns `OBLIVION-CARD-CONTENT-NOT-TEXT`; a Function Card is an executable semantic object, not a copied source document.

## Security and isolation

xUnit executes ordinary project code with the runner process's filesystem and network authority. Function Cards are not sandboxed. External runner isolation prevents a crashing test process from crashing the Oblivion host, but it is not a capability sandbox. M20f adds no scheduler, watcher, daemon, background execution, auto-run, kernel state, dependency graph, or execution cache.

## Non-goals retained

M20f adds no inline editor, custom Assert API, custom Fact/Theory semantics, direct reflection invocation, arbitrary TypeScript/C#/shell/Python execution, Theory-case interaction, table-driven cases, artifact outputs, debugging, cancellation UI, or notebook execution ordering.

## Outcome

Outcome A: exact discovery, execution, rerun, basic Theory summarization, TRX mapping, and authored source mapping are reliable. Dogfood did expose repeated build/discovery startup cost; that is a performance seam, not a semantic blocker and does not justify a broader executable runtime.
