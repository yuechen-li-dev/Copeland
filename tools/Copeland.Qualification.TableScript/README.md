# TableScript qualification

Run from the repository root with PowerShell 7 and .NET 10:

```powershell
./tools/Copeland.Qualification.TableScript/run.ps1
```

The runner builds the real CLI and canonical café workbook once, executes the
workbook consumer, then runs five independently selectable suites:

| Suite | What it proves |
| --- | --- |
| `initializers` | Counted repetition, remainder fill, and immutable table fills through the CLI |
| `row-authoring` | Row/column authoring parity, transitive provenance, guarded row edits, and CSV round trips |
| `keyed-edits` | Key 100 resolves correctly in two input orders; rejected edits preserve source hashes |
| `summaries` | Compiled C# totals and every supplier-summary field agree with canonical CLI queries before/after a stock edit |
| `snapshots` | Compiled immutable previews agree with an independent source-copy CLI calculation; globals and repeated previews stay unchanged |

Choose a subset with ordinary PowerShell array syntax:

```powershell
./tools/Copeland.Qualification.TableScript/run.ps1 -Suite keyed-edits,summaries
```

Output defaults to ignored `artifacts/local/tablescript-qualification/`.
`-OutputDirectory` can choose another location inside `artifacts/local`.
The runner leaves detailed JSON, generated code, copied scenarios, compiled probe
hosts, and a concise `summary.json` there. These are local output, not test inputs
or compiler authority. The authored workbook's hash must remain unchanged.

`Common.ps1` owns only qualification plumbing: CLI invocation with a 30-second
timeout, assertions, source-preserving rejection checks, and generated probe
project setup. Suite assertions live under `cases`; maintained source fixtures
and thin C# projection runners live under `inputs`. All calculation and validation
use the existing compiler, application source, and canonical CLI executor.

This is C#/CLI integration coverage. It makes no unsupported/supported JavaScript
claim: compiler parity tests and browser application runners own that evidence.
The café's separate `qualify.ps1` still owns its 66-check comparison workflow.

This replaces the five former `tools/copeland-tablescript-*` scripts. The old
ellipsis audit's sibling-repository inspection was historical research and is not
part of local qualification. Invalid repetition/budget cases remain covered by
`InitializerRepetitionTests` in the compiler suite. Original result summaries and
raw-file recovery hashes are retained in the
[historical provenance record](../../docs/Copeland/reviews/evidence/tablescript-retired-artifacts/provenance.json).
