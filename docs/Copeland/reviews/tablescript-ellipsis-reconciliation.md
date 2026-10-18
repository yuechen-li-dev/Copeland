# TableScript ellipsis reconciliation

> Historical qualification paths/results. Use the maintained
> [TableScript suite](../../../tools/Copeland.Qualification.TableScript/README.md)
> for current commands. Retired raw bundles are represented by
> [summaries and recovery hashes](evidence/tablescript-retired-artifacts/provenance.json);
> old capability statements below describe their original run.

## Status and decision

This records the original design reconciliation before implementation. The
[first implementation step](tablescript-ellipsis-implementation.md) now qualifies
counted repetition, remainder fills, host lowering, and logical-cell source edits.
The original audit fixed a prerequisite: unsupported ellipsis in a table column could
leave the parser in an infinite recovery loop. Authored and derived table loops
now make progress after malformed columns, with four regression specimens.

Reserve `...` for postfix initializer repetition, following Concept and Oct:

```typescript
[7 ... 4]                 // 7, 7, 7, 7
[1 ... 2, 2 ... 3, 9]     // 1, 1, 2, 2, 2, 9
[1, 2, 0 ...]            // Fill a destination whose extent is already known.
```

The earlier proposed `...line;` derived projection is withdrawn. Carrying a
finite set of fields forward is a separate projection operation. For the next
pass, per-row locals should remove the need for the second derive used solely
to reuse a calculation; existing explicit projections remain available. We do
not need to invent another projection keyword in this reconciliation.

## What the two implementations actually agree on

| Rule | Concept | Oct | Copeland decision |
| --- | --- | --- | --- |
| Postfix `value ... N` | Exactly N initializations | Exactly N evaluations | Adopt |
| Final `value ...` | Known fixed-array destination extent | Table column, table replacement, or an existing 2D row | Adopt for statically sized table columns and replacements |
| Count | Compile-time nonnegative integer | Int expression; runtime counts checked | Compile-time int in the first pass |
| Zero | Produces no elements | Typed empty result; evaluates no values | Adopt, while still checking the element's type and legality |
| Evaluation | Once per resulting element | Count once, then value once per element | Adopt per-element evaluation; count resolved before execution |
| Large repetition | Compact AST/MIR, generated loop | Compact repeated node, counted MIR loop | Preserve compact segments through lowering |
| Container/argument spread | Rejected | Rejected | Rejected |

Concept's count is also part of fixed-array/table layout and type identity.
Copeland's `T[]` has an element type without a length parameter. Reconcile the
initializer meaning without claiming Concept's native storage or ABI. Copeland
continues to produce its existing immutable managed arrays and columnar tables.

Oct's runtime-count and whole-row-assignment extensions are useful but are
outside this first bounded pass. Its vectors and matrices have their own literal
grammar; this pass does not add that grammar to Copeland's shader language.

## Fixed extent and ordinary TypeScript instincts

Explicit counts work in ordinary array literals, including nested arrays. A
count means the total number of occurrences of that element, not the number of
additional copies. There is no inferred arithmetic sequence: `[1, 2, 3 ... 4]`
means `1, 2, 3, 3, 3, 3`.

The initial count subset is closed `int` literals, parenthesized unary/binary
constant arithmetic, and closed existing static template parameters. Resolve
them with Copeland's existing static evaluator and numeric policy. A runtime
variable or parameter does not become compile-time merely because it is called
`const`. Arbitrary function calls in counts and dependent table templates are
deferred; table bodies currently bind before ordinary function bodies. Do not
reorder that pipeline or add a parallel constant evaluator to admit them.

Use `StaticEvaluationLimits.M1.MaximumArrayLength` (currently 65,536) as the
initial maximum expanded extent for a literal using repetition. Check segment
counts and the combined length with checked arithmetic. Preserve the other
existing static evaluation budgets. This bound is an implementation admission
limit, not an array type or an excuse to truncate values.

An ordinary declaration such as `const values: int[] = [0 ...];` has no extent
to fill. Its repair is an explicit count, for example `[0 ... 6]`. The length of
a previous array value is not implicitly a new initializer's destination size.

For authored tables, determine the shared cardinality from columns that state
their full length. Ordinary entries and counted segments both state a length.
Check all such columns for agreement before resolving any remainder fills,
independently of declaration order:

```typescript
record table Marks {
    valid: boolean = [true ...];
    key id: int = [10, 20, 30];
    weight: int = [4 ... 3];
}

function cleared(): Marks {
    return Marks with { valid: [false ...] };
}
```

The `id` and `weight` columns agree on three rows, so `valid` has three cells.
At least one column must state a length. All-fill tables are ambiguous, including
ones whose prefixes happen to have the same length; report the missing extent.
An explicitly empty column is a legitimate zero-row anchor.

A fill is unique and final within its own literal. Its count is the target
extent minus the lengths of preceding ordinary and counted elements. A prefix
already equal to the extent makes the fill empty. A longer prefix is an error.
Other columns and table replacements must still have exactly the shared extent;
there is no padding without an authored fill, resizing, or truncation.

For table `with`, the target table supplies the extent. Existing restrictions
remain: authored immutable replacement cells, readonly derived tables, deeply
immutable cell types, unique keys, and valid references. `[10 ... 3]` in a key
column is still a duplicate-key error. Repetition never invents identities.

## Evaluation and ownership

Visit literal segments in source order. Resolve each count before runtime
evaluation, then evaluate its value once per resulting element. Zero repetitions
evaluate no value. `[makeRecord() ... 3]` means three calls, including their
ordinary effects and failure behavior; it is not `Array.fill(makeRecord())`.
Nested array constructors execute independently for each outer occurrence.

Independent initialization is not deep cloning. Repeating an already-bound
reference follows the same aliasing laws as writing that reference several
times. The compiler must reject any repetition that violates existing capture,
resource, lifetime, or element-type rules. Every value is checked even if the
count resolves to zero. Table initializer values remain static deeply immutable
constants; supporting runtime array values does not relax table authoring.

`...` is accepted only after an element inside the ordinary array initializer
grammar. Prefix array/object spread, rest parameters, call expansion, iterable
expansion, slicing, ranges, and derived field expansion remain unsupported.
Formatting uses spaces around the token. Lexing must prefer the triple-dot
token while preserving decimal numbers and ordinary member access.

## Compiler and editor ownership

Add a dedicated repeated-initializer syntax node with value, optional count,
and source span. Give the binder one shared initializer-segment resolver with
an optional expected extent; both authored tables and table `with` use it.
The bound plan retains segment counts, total logical length, and the mapping
from logical cell ranges to authored spans. Error recovery must remain bounded.

Preserve repetition as a compact MIR initializer segment. C# and JavaScript
lower to ordinary construction with private writes and the existing immutable
array boundary. Do not duplicate N expression nodes or N generated statements,
or implement the language separately inside each backend. Static evaluation,
effects/capture analysis, generic substitution, suspension analysis, MIR
validation, diagnostics, and artifacts must all understand the same node.
Table consumers may enumerate logical cells; compact plans should remain the
authority until a consumer actually requires materialized values.

The CLI cannot index `Cells.Elements[row]` after this change. At audit time,
`table set` did exactly that, and add/delete helpers also assumed one written
element per logical row. The implementation routes these operations through the binder's segment
mapping. Setting row 2 of `[true ... 5, false]` to false can produce:

```typescript
[true ... 2, false, true ... 2, false]
```

Drop zero-length segments and preserve unaffected text. Deleting a row shortens
its containing segment, rather than deleting an arbitrary written element.
For insertion after a remainder fill, materialize its old *count* before
appending: a three-row `[true ...]` becomes `[true ... 3, false]` when a false row
is appended. Do not put an element after a bare fill or silently retarget earlier
cells. Recompute all columns against the proposed new cardinality before
publishing an edit. Dry runs and rejected edits retain the existing source-hash
guarantees. Key-addressed editing later reuses this same logical-row path.

## Directed repairs and acceptance

New diagnostics should distinguish: non-int count, non-static count, negative
count, extent-limit/overflow, missing destination extent, non-final/multiple
fill, and forbidden spread location. Missing extent should suggest an explicit
count or a table column with a stated length. Keep existing column-length,
duplicate-key, missing-reference, and immutable-cell diagnostics where they own
the failure. The pre-implementation audit returned generic parser diagnostics; those were
evidence of missing support, not the proposed final diagnostic experience.

Implementation acceptance requires:

1. Mixed, nested, zero, negative, fractional, runtime, and overflowing counts;
   type inference from a zero-count value; preservation of decimal/member lexing.
2. Order-independent table anchors, zero-row anchors, ambiguous all-fill tables,
   exact/under/overfilled replacements, and unchanged key/reference validation.
3. C# and JS runtime probes with a deterministic counter proving one call per
   element and none at zero, plus nested constructors and ordinary failure
   behavior. Avoid probabilistic random-value tests for evaluation counts.
4. A 65,536-element proof with compact syntax/MIR/generated source, and repeated
   byte-identical compiler output. Stored table values may require proportional
   data; compiler syntax and executable initializer code should not.
5. Real CLI set/add/delete and dry-run/rejected edits across counted and filled
   segments. Editing the first, middle, and last logical occurrence must preserve
   every other cell and leave the source valid.
6. Replace the cafe workbook's purchasing column with
   `[Purchasing.Active ... 5, Purchasing.Paused]` and its reorder points with
   `[8 ... 2, 6, 12, 8 ... 2]`. Preserve all row identities, six purchasing cases,
   31,210 base cents, 12,710 edited cents, formula provenance, and immutable
   snapshots through the existing C# execution path. JS derived-table execution
   remains a separate known blocker; this operator does not qualify it.

Then resume the agreed friction sequence: keyed edits, row-oriented authoring
and per-row locals, typed summaries, explicit snapshot calculations, and the
browser execution boundary. Ellipsis handles repeated fixed data; it does not
replace the reusable-calculation work.

## Audit evidence

Current implementation authority was inspected directly:

- Concept: `docs/language/INITIALIZERS.md`, `docs/language/TABLES.md`,
  `internal/concept/parse.go`, `validate.go`, `generate.go`, and
  `table_repetition_test.go`. These define compile-time counts, independent
  initialization, ownership rejection, fixed table layout, and compact lowering.
- Oct: `Language/reference/language/07-arrays.md`, `11-records.md`,
  `internal/typecheck/typecheck.go`, `internal/interpret/interpret.go`,
  `internal/build/lower_expr.go`, and `Language/Expressions/RepeatedElements/`.
  These establish runtime counts, table anchor ordering, per-element evaluation,
  zero-count behavior, and the existing-row fill extension.
- Copeland: `Syntax/Lexer.cs`, `Parser.cs`, `SyntaxKind.cs`, table/array/with
  binding in `Semantics/Binder.cs`, `StaticEvaluation.cs`, compact-array seams in
  MIR/lowering/backends, and editing in `Copeland.Cli/TableToolCommand.cs`.

Run `tools/copeland-tablescript-ellipsis-audit/qualify.ps1` to qualify the implemented
forms. It rebuilds the actual CLI, validates the M12 workbook, and requires counted
repetition, an earlier filled column, and table-with fill to succeed with three rows.
Each child probe has a 15-second timeout. Current evidence defaults to
`artifacts/tablescript-ellipsis-implementation/`; the original pre-implementation
results below are preserved separately. No external repositories are modified.

`artifacts/tablescript-ellipsis-reconciliation/summary.json` records the results
and audited source hashes/commits. Before the recovery fix, the counted table
probe did not terminate and its process was stopped; after it, all three forms
return diagnostics. Full build/test evidence is recorded beside that summary.
`dotnet build Copeland.slnx -m:1 --nologo` passed with zero warnings/errors;
`dotnet test Copeland.slnx -m:1 --no-build --nologo` passed all 2,283 tests,
with zero failed or skipped. Four new cases cover table recovery; the audit
baseline and three intentionally unsupported forms also passed their checks.
Concept/Oct suites were not rerun; their source/tests were inspected, and their
historical validation totals are not claimed as current validation.
