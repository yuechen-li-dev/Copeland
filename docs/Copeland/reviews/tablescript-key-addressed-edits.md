# TableScript key-addressed source edits

> Historical qualification paths/results. Use the maintained
> [TableScript suite](../../../tools/Copeland.Qualification.TableScript/README.md)
> for current commands. Retired raw bundles are represented by
> [summaries and recovery hashes](evidence/tablescript-retired-artifacts/provenance.json);
> old capability statements below describe their original run.

The second bounded improvement adds declared-key row selection to the existing
compiler-aware source editor. Café stock edits now target product identity
directly:

```powershell
dotnet $cli table set Workbook.ts Stock --key 100 --column onHand --value 12 --dry-run --format json
dotnet $cli table set Workbook.ts Stock --key 100 --column onHand --value 12 --format json
dotnet $cli table delete-row Inventory.ts Inventory --key 100 --format json
```

The same stock edit resolves row 1 in the authored workbook and row 4 after its
stock rows are reversed. Canonical purchase-plan queries produce the same
31,210-cent baseline and 12,710-cent edited scenario in both arrangements. The
edited scenario has 47 units and two orders, with every other product unchanged.
The original M12 qualification also uses key selection now.

## Contract and ownership

`set` and `delete-row` require exactly one `--row` or `--key`. `--key` reads the
compiler's declared key symbol and bound logical scalar cells. It supports the
existing identity types: `int`, `string`, and `boolean`, including transparent
aliases and `key reference` columns. Integer values use the existing command
literal parser, including signs and overflow rejection. Strings compare exactly
and ordinally; booleans use lowercase literals. No key is guessed for unkeyed
tables. An empty keyed table has no matching row.

Resolution feeds the same segment-aware editor introduced for repetition.
Dry-run, candidate compilation, hash checks, atomic publication, and declaration
constraints continue through the existing transaction. Single-cell set and row
deletion preserve neighboring logical values, including counted and remainder
segments. Positional edits retain their previous behavior.

Successful JSON mutations add the resolved selection:

```json
{
  "selection": {
    "row": 1,
    "kind": "key",
    "keyColumn": "productId",
    "value": 100
  }
}
```

The existing `changed`/`deleted` payload and schema version remain available.
For a key-column change, `selection.value` describes the original key and
`changed.newValue` describes the replacement. Positional results have
`kind: "row"`, a null `keyColumn`, and the selected row index as `value`.

| Diagnostic | Meaning |
| --- | --- |
| `COPE-TABLE-TOOL-0021` | Missing selector, or both `--row` and `--key` |
| `COPE-TABLE-TOOL-0022` | Key selection requested for an unkeyed table |
| `COPE-TABLE-TOOL-0023` | No row matches the typed key |
| `COPE-TABLE-TOOL-0010` | Invalid key/command literal |

Duplicate options retain `COPE-TABLE-TOOL-0003`; invalid row indices retain
`0005`/`0006`. Compiler duplicate-key/reference diagnostics and the derived-write
guard remain authoritative. These failures preserve source bytes.

## Concrete boundaries

A row-count change can invalidate another authored initializer. The café
`previewStock()` function supplies six explicit cells in `Stock with {...}`.
Deleting one Stock row therefore reports `COPE-TABLE-0008` at that replacement;
the editor retains all six input rows. Successful key deletions are verified on
tables without an invalidated dependent snapshot. No dependent source is
rewritten to conceal a compiler rejection.

A string probe isolated preexisting lexer behavior in `Syntax/Lexer.cs`:
`LexString` returns the literal interior without decoding escapes. Thus a source
string containing `\"` or `\\` projects those characters literally through
`table rows`. Key selection uses that projected value exactly. Tests cover both
ordinary Unicode/quote/backslash arguments and this existing escaped identity.
Changing escape decoding is a separate language correctness pass; it is not
implemented here, and the CLI does not invent different string identity rules.

This step does not add composite keys, alternate-column selectors, predicates
for bulk mutation, cascading edits, or runtime keyed `with` syntax. Runtime
array/derive/browser boundaries from the initializer step are unchanged.

## Qualification

```powershell
dotnet build Copeland.slnx -m:1 --nologo
dotnet test Copeland.slnx -m:1 --no-build --nologo
pwsh -NoProfile -File tools/copeland-tablescript-key-edits/qualify.ps1
pwsh -NoProfile -File samples/copeland-ts/tablescript-friction-m12/qualify.ps1 -OutputDirectory artifacts/tablescript-key-edits/workbook
```

Evidence is stored under `artifacts/tablescript-key-edits/`. The focused CLI
suite adds 21 cases for reorder stability, repeated neighbors, scalar identity,
string projection, dry-run, selector failures, key changes, reference safety,
and derived write rejection. The café driver compares full canonical rows before
and after the key-addressed edit in both orders, retains source hashes on rejected
edits, and records the snapshot-cardinality rejection. The M12 workbook still
runs its 13 generated C# consumer assertions and 66 qualification checks.

Final validation: full solution build passed with zero warnings/errors; all
2,325 tests across eight projects passed with zero failures or skips. The separate
keyed café driver passed 96 checks and M12 passed 66. `dotnet-build.txt`,
`dotnet-tests.txt`, `key-workbook-check.txt`, and `workbook-check.txt` contain the
execution logs; `validation.json` records these totals and tested source hashes.

Next in the agreed sequence: row-oriented authoring and per-row calculation
locals, followed by typed summaries and explicit snapshot calculations.
