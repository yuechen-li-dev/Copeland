# TableScript typed editing

Run from the repository root:

```powershell
./samples/copeland-ts/tablescript-editing-m14/run.ps1
```

Requires .NET 10, Node with native TypeScript support, npm, and Edge. Pass
`-BrowserChannel chromium` for an installed Playwright Chromium. The script uses
locked npm dependencies and the canonical existing Copeland browser frame
registry, materializing its owner fixture through TSPack if necessary.

This is the editing follow-up to M13. It includes the original M12 workbook and
M13 application sources unchanged. Their formulas and stock validation remain
authoritative. `PriceApplication.ts` adds the café's 0..1000000 cents/unit policy
and a keyed editable-product projection. Neither depends on React or text input.

`libraries/Copeland.Editing/Editing.ts` supplies ordinary reusable generic drafts
and exact numeric codecs, included as logical module `src/Editing.ts`.
`Editor.ts` owns both raw drafts, accepted view, selected immutable identity, and
separate typed domain errors. Its same reducer executes under C# and JavaScript.
`Cafe.tsx` presents it using `stream`, `state`, `on`, explicit captures and React.

The foreign `TextField` contract forwards every string unchanged and renders the
controlled value and accessibility props. It has no numeric parser, internal
draft, effect synchronization, reset generation, or business validation.
`PreviewForm` only adapts native submit to the typed callback.

- Apply reveals parse feedback, then calls the domain function if parsing
  succeeds. Successful application rebases that field and preserves the other
  field's unapplied text. Accepted tables and totals change in one state update.
- Cancel restores the field's latest accepted value and clears its feedback.
- Selection discards both drafts and loads the latest accepted keyed snapshot.
- Reset discards all local edits and drafts, including when accepted numbers
  never changed. It needs no host reset key.

The harness checks incomplete/invalid input, negative domain values, exact cents
precision and limits, zero and maximum accepted prices, normalization, independent
field application, Enter submission, cancellation, switching, and reset under
React StrictMode in all three profiles at 1280 and 390 pixels. It checks no page
overflow, keyboard table scrolling, valid description references, and no browser
warnings/errors. Compiler hashes and browser observations are under
`docs/Copeland/reviews/evidence/tablescript-editing-m14/`; emitted C#, bundles and
screenshots stay in ignored `artifacts/` and `dist/` directories.

This is local preview only. Authoritative persistence would receive typed
commands and revalidate against server-owned inputs. Variable row cardinality,
remote conflicts, locale-specific money entry, IME qualification and large-table
performance remain separate work.
