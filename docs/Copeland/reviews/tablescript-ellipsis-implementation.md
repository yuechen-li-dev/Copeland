# TableScript initializer repetition: first implementation step

The first step of the reconciled language improvement sequence is implemented.
Postfix ellipsis expresses repeated fixed data in ordinary host arrays, authored
table columns, and table `with` replacements. Existing immutable array and
columnar table ownership remain the semantic authority.

## Authoring

```typescript
function values(): int[] {
    return [1 ... 2, 2 ... 3, 9];
}

record table Marks {
    valid: boolean = [true ...];
    key id: int = [10, 20, 30];
    weight: int = [4 ... 3];
}

function cleared(): Marks {
    return Marks with { valid: [false ...] };
}
```

`value ... N` means exactly N occurrences. Counts use compile-time `int` literals,
unary/binary arithmetic, or closed static parameters. Ordinary generic arrays
can defer static parameter counts until specialization. The existing static
evaluator and integer numeric policy resolve them. Runtime parameters, local
`const` values, arbitrary count calls, and dependent table template initialization
are rejected with a directed diagnostic.

Bare `value ...` must be final. Authored tables infer a shared extent from every
column that states its full length, before resolving fills, regardless of column
order. Those anchors must agree. Empty anchors permit zero rows; all-fill tables
have no extent. A table replacement uses its destination's known extent. Ordinary
`T[]` carries no extent, so bare fills need an explicit count there.

Each occurrence evaluates its value independently, in source order. Zero checks
the value but evaluates it zero times. Nested constructors, calls, ordinary error
propagation, and awaited computations retain that behavior. This is independent
initialization, not deep cloning of existing references. Table cells retain their
existing constant and deep immutability restrictions.

## Compiler and editing path

Syntax retains one value/count node per written segment. Bound arrays and MIR
carry compact segment counts; `InitializerSequence<T>` exposes the same data as
logical cells for table validation, queries, and source editing. Existing effect
and capture walks inspect the segment expression, static evaluation evaluates
each occurrence, and generic specialization resolves deferred counts. Flow
projection rewrites operations inside repeated values. Async lowering uses the
existing explicit state machine and reenters each repeated await.

C# and all three JavaScript emission profiles generate counted construction loops
with the value expression inside the loop. The 65,536-element array/table probe
keeps MIR below 2,000 characters, C# below 20,000, and JS below 30,000, with
byte-identical output across repeated compilations and actual host execution.

CLI `set`, `add-row`, and `delete-row` address logical rows. A cell change splits
only the affected segment. Append after a fill fixes the old count first, and
deletion shrinks a count or follows the new shared extent. Candidate binding,
dry-run, hash guards, and duplicate key/reference validation remain in the real
transaction path. Tests check neighboring cells after first/middle/last edits
and deletions, then verify append and a rejected edit preserves the source.

## Admission boundaries and diagnostics

Initializers using repetition admit at most 65,536 logical elements per literal,
including combined segments. Counts never truncate. Existing static evaluation
budgets still apply.

| Diagnostic | Meaning |
| --- | --- |
| `COPE-INIT-0001` | Count has a type other than `int` |
| `COPE-INIT-0002` | Runtime/call count or dependent table initialization |
| `COPE-INIT-0003` | Negative count |
| `COPE-INIT-0004` | Count/combined extent exceeds budget or static evaluation fails |
| `COPE-INIT-0005` | Bare fill has no fixed extent |
| `COPE-INIT-0006` | Bare fill is not final |
| `COPE-INIT-0007` | Prefix exceeds destination extent |
| `COPE-INIT-0008` | Ellipsis outside postfix initializer position |

Prefix spread, rest/call expansion, iterable expansion, ranges, inferred numeric
sequences, and derived field expansion remain unsupported. This host initializer
step does not extend shader literal grammar or native fixed-array ABI. Authored
array-valued table cells still report `COPE-TABLE-0009`; repetition does not relax
that preexisting boundary. JavaScript derived-table materialization still reports
`COPE-JS-DERIVE-0001`; no browser workbook execution is claimed here.

## Qualification

Run from the repository root:

```powershell
dotnet build Copeland.slnx -m:1 --nologo
dotnet test Copeland.slnx -m:1 --no-build --nologo
pwsh -NoProfile -File tools/copeland-tablescript-ellipsis-audit/qualify.ps1
pwsh -NoProfile -File samples/copeland-ts/tablescript-friction-m12/qualify.ps1 -OutputDirectory artifacts/tablescript-ellipsis-implementation/workbook
```

Evidence is under `artifacts/tablescript-ellipsis-implementation/`. The focused
initializer suite has 20 cases, including per-element runtime counters, zero,
nested arrays, static evaluation/specialization, flow projection, errors, actual
async suspension/resumption, table extent inference, compactness, deterministic
emission, and invalid forms. The real CLI integration test covers source edits.

Final full solution validation passed: build with zero warnings/errors, and
2,304 tests across eight test projects with zero failures or skips. Logs are
`dotnet-build.txt` and `dotnet-tests.txt`; `validation.json` records the totals
and hashes of the tested source files. The CLI probe script passed the workbook
baseline and all three implemented table forms, each with its expected extent.

The café workbook now uses `[Purchasing.Active ... 5, Purchasing.Paused]` and
`[8 ... 2, 6, 12, 8 ... 2]`. Its 13 generated C# consumer assertions and 66
qualification checks preserve the six logical rows, purchasing cases, keyed
joins, formula provenance, immutable snapshots, base 31,210 cents/67 units/3
orders, and edited 12,710 cents/47 units/2 orders. The input data is equivalent to
the existing Excel comparison; native Excel was not operated in this step.

The [next implementation step](tablescript-key-addressed-edits.md) now qualifies
key-addressed edits. Row-oriented authoring and per-row calculation locals,
typed summaries, explicit snapshot calculations, and the browser execution
boundary remain separate milestones.
