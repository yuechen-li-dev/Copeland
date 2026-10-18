# TableScript variable rows

Run from the repository root:

```powershell
./samples/copeland-ts/tablescript-rows-m15/run.ps1
```

Requires .NET 10, Node with native TypeScript support, npm and Edge. Pass
`-BrowserChannel chromium` to use installed Playwright Chromium. The runner uses
locked packages and the existing canonical Copeland browser frame registry.

This sample reuses the canonical M12 workbook, M13 application projection, M14
price policy and editing reducer, and `Copeland.Editing` source. It contains no
second purchasing policy or renderer-owned table implementation.

- `Rows.ts` owns typed create/remove requests, domain validation, atomic product
  and stock changes, and checked calculation. It has no React dependencies.
- `RowEditor.ts` owns creation drafts and optional selection. One accepted view
  lives at the root. The selected product's stock/price editing delegates to the
  existing M14 reducer; its view is assembled transiently for that call.
- `Cafe.tsx` renders the state through existing `stream`, `state`, `on`, explicit
  captures and the React adapter. Foreign inputs forward raw strings unchanged.

Create supplies a positive unique ID, name, supplier, initial stock and exact
decimal price. The application inserts product and stock together. New products
use the stated café policy: active purchasing, reorder below eight units, target
24. Successful creation selects the new key; the next ID shown is only an editable
suggestion. Removal deletes product and stock together. Removing the selected
key clears selection; removing another key preserves the current draft. Reset
restores the initial local scenario and clears creation drafts.

The C# probe and six React StrictMode browser workflows qualify 6 → 7 → 6 rows,
emptying the café, creating from empty, duplicate/reference/domain/parse refusals,
editing an appended row, selection, draft preservation, cancellation and reset.
All three JavaScript profiles run at 1280 and 390 pixels. No page overflow,
missing feedback descriptions, console warnings, page errors or failed responses
are accepted. Table scrolling is checked using the keyboard.

Evidence is under `docs/Copeland/reviews/evidence/tablescript-rows-m15/`.
Generated C#, ESM, browser bundles and screenshots are in ignored `artifacts/`
and `dist/` directories. The ephemeral qualification server closes on completion.

This is a local preview. Remote commits, conflict resolution, first-class stored
change descriptions, rekey/cascade policy, dynamic table TSON encoding, Native AOT
performance and large-table performance are separate work.
