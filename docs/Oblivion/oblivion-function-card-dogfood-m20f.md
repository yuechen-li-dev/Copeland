# M20f Function Card dogfood

## Contextual task

The maintained `M20fFunctionCards.oblivion` vault places explanatory Markdown beside real Copeland `.tsxtest` Function Cards. The primary Fact checks that a runner-owned `Passed` result remains a projection instead of being reinterpreted by Oblivion. A two-case Theory proves truthful basic runner expansion. A controlled failure qualifies failure text and source mapping.

## Real workflow

1. `failing-function` ran through Copeland materialization, xUnit discovery, exact filtering, and TRX.
2. xUnit returned `Failed`, `Assert.True() Failure`, and `ControlledFailure.tsxtest:8`; no `.g.cs` path was presented.
3. The assertion was repaired from the intentionally wrong `"Passed"` expectation to the actual `"Failed"` value.
4. Explicit rerun returned `Passed` with the same discovered identity.
5. The controlled failing source was restored so maintained failure/source-mapping coverage remains reproducible.
6. `passing-function` was run twice and returned `Passed` twice with stable identity and no hidden notebook state.

Representative dogfood timings were approximately 3.4 seconds for Copeland/project materialization, 1.2 seconds for discovery, 1.3 seconds for Test Platform startup/execution, and 2–131 ms of runner-reported test duration. Results are never cached; each Run executes xUnit.

## Comparison

`dotnet test --filter` remains the execution authority and is faster to type when the developer already knows the fully-qualified identity. Test Explorer remains richer for bulk discovery and per-case browsing. Oblivion materially improves the contextual workflow: the exact test sits beside the reason for running it, keeps one stable Card identity, offers the same operation to a human button and structured agent CLI, and surfaces the authored failure location inline.

Nothing important was missing for a single Fact. A source editor, persistent kernel, custom assertion layer, artifact channel, test dependency graph, stdout cell model, debugger, or arbitrary callable runtime would not have improved this dogfood task.

## Desire log

| Desire | Evidence | Classification |
| --- | --- | --- |
| Reuse materialized project/discovery within a session | Every rerun repeated ~4.6 s of build plus discovery before runner startup | REPEATED |
| Theory case list and one-case rerun | Two-case summary was truthful; no case needed individual action | NICE |
| Inline source editor | Existing coding tools completed repair cleanly | NOT_NEEDED |
| Captured xUnit output | Failure message and stack were sufficient | NOT_NEEDED |
| Traits/filter UI | Exact Card identity was sufficient | NOT_NEEDED |
| Cancel | Runs completed within the bounded timeout | NICE |
| Table-driven cases | InlineData covered the maintained Theory | NOT_NEEDED |

## Exact M20g recommendation

Add session-scoped reuse of the existing materialized Copeland test project and discovered descriptor, invalidated by source/project realization inputs. Every explicit Run must still execute xUnit and produce fresh TRX; do not cache outcomes. Measure warm rerun latency before adding Theory-case interaction. This recommendation is narrower and better supported by dogfood than a new runtime or editor.

## Proof inventory

- `artifacts/m20f/function-card-not-run.png`
- `artifacts/m20f/function-card-passed.png`
- `artifacts/m20f/function-card-failed.png`
- `artifacts/m20f/function-card-vertical-split.png`
- matching `.viewport.json` files
- `artifacts/m20f/m20f-xunit-function-card-manifest.json`
- `artifacts/m20f/canonical-playback/playback-suite-report.json` (14 passed, 0 failed, 0 skipped)

The playback exporter exhibited its known post-success path barnacle: the suite correctly wrote its summary beside the `playback` scenario directory, then the wrapper checked for a nested copy. The generated report above is the authoritative result.
