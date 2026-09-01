# Oblivion warm Function dogfood — M20g

## Result

Outcome A: project/test callable realization is warm and every invocation remains fresh. The maintained `passed_result_preserves_xunit_authority` Fact was run cold, warm twice, after a `.tsxtest` edit, and warm again. All runs Passed through the real Copeland → xUnit/Test Platform → TRX path.

## Timings

| Run | Setup | Total ms | Materialization ms | Discovery ms | Execution ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| cold | cold | 4672.104 | 2724.102 | 912.052 | 1000.756 |
| warm 1 | warm | 1010.322 | 0 | 0 | 991.395 |
| warm 2 | warm | 1017.629 | 0 | 0 | 1002.012 |
| source invalidation | cold | 4696.370 | 2743.137 | 914.588 | 1003.017 |
| post-invalidation | warm | 1018.668 | 0 | 0 | 1001.742 |

The first warm rerun was 4.62 times faster end to end and removed 3636 ms of build/discovery work. Fingerprinting remained 19–35 ms. The remaining approximately one-second cost is fresh `dotnet test` process/Test Platform startup and execution.

## Correctness evidence

The first three runs shared fingerprint `C223A6FE1921C1B64CFF9B927544207D40EB07609814B8D176A3C0C544E482FF`. Appending a controlled comment to the authored `.tsxtest` changed it to `0749A93B679EBDBB44BD410A2DCC620BED6EAA023A3C69BB9F72ACCD9174698C`, invoked materialization and discovery, and Passed. The following run reused the new realization warm. The fixture source bytes were restored in `finally`.

Every run reported `executionInvoked: true` and a different GUID result identity. Unit coverage independently proves `.tsxtest`, production `.tsx`, and project-file changes invalidate; missing assembly invalidates; a failed rebuild does not publish or execute stale realization; and passive inspection invokes no process.

The real two-case Theory ran warm from the same project realization and retained two passed runner-expanded cases. Two Function Cards in that project reused one discovery list: the passing Fact cold-realized it, while Theory and controlled-failure descriptors were selected without another `--list-tests`. Different project paths are distinct cache keys by construction.

The controlled failing Fact ran twice warm. Both outcomes were fresh xUnit Failed results, both mapped to authored `ControlledFailure.tsxtest:8`, neither exposed `.g.cs`, and the distinct result identities proved no prior TRX was reopened. Failure did not invalidate the callable.

The Avalonia UI, focused command, and CLI all enter the same `OblivionApplication.BeginFunctionCardRun` / `CompleteFunctionCardRun` realization path. CLI JSON contains the bounded setup evidence; passive `card show` cannot materialize or discover.

The retained Avalonia capture `artifacts/m20g/function-card-warm-passed.png` proves the expanded Passed surface did not regress; warm/cold state is intentionally kept out of the visual Card chrome, so the machine-readable run files carry the warm proof. Canonical Presenter playback passed 14/14 with zero failures and zero skips. `Oblivion.slnx`, `Copeland.slnx`, `JointTaskForce.slnx`, regular and slow Machina lanes, the Machina build, and Aurelian validation passed. `git diff --check` reported no whitespace errors.

## Recommendation

Runner startup now dominates warm latency, but roughly one second is not evidence for the complexity of a resident VSTest host. Do not add one yet. A broader executable runtime is also unjustified.

Checkpoint Oblivion executable Function Cards here. Theory UX is intentionally deferred while the next work detours into Copeland TS types/records/interfaces/templates. After that detour, reassess the executable-card ladder from measured product needs; retain this project-level warm realization unless new evidence shows persistent runner startup is materially painful.
