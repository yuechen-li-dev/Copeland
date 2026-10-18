# TableScript application and React preview

Run from the repository root:

```powershell
./samples/copeland-ts/tablescript-application-m13/run.ps1
```

Requires .NET 10, Node with native TypeScript support, npm, and Microsoft Edge.
The script installs the locked dependencies and reuses the existing TSPack-owned
browser frame registry. If that artifact is missing, it materializes the existing
React fixture with TSPack; it does not introduce another state runtime.
`-BrowserChannel chromium` selects an installed Playwright Chromium instead.

The project has three explicit layers:

- The existing café `Workbook/Workbook.ts` owns tables, references, formulas, and
  summary functions. This sample includes that exact file under logical module
  `src/Workbook.ts`; it does not copy its purchasing policy.
- `src/Application.ts` owns `WorkbookInputs`, `WorkbookView`, `StockError`,
  `setOnHand`, `calculate`, and `applyStockEdit`. It compiles and executes through
  C# with no React packages. JavaScript executes the same application source.
- `src/Editor.ts` owns frontend drafts and accepted presentation state.
  `src/Cafe.tsx` projects typed rows, errors, and summaries using Copeland `View`,
  `stream`, `state`, and `on` with the React adapter. It contains no purchasing
  formulas or nonnegative-stock rule.

`tooling/controls.ts` is an ordinary React control that owns transient input text,
including invalid/incomplete text. It adapts that text to an `int` contract and
uses native form validity to prevent submitting a stale draft when text is malformed.
Negative integers are delivered unchanged to the application. Expected domain
failures are matched as typed Results before publishing one new frontend state.

This is an offline scenario preview. No authoritative inventory service,
persistence, authentication, or remote commit protocol is implemented. A server
would validate edit requests against its own inputs; a browser-produced view is
not authority to commit inventory.

The compiler harness executes the real C# application probe and emits project ESM
and layout artifacts for all three JavaScript profiles. The browser harness mounts
React under StrictMode at 1280 and 390 pixels, edits shuffled stock by identity,
checks refused edits and unchanged accepted data, chains another edit, rejects
malformed numeric text, and resets. Evidence is written to
`artifacts/qualification/`; screenshots and
bundles remain in this sample's ignored `artifacts/` and `dist/` directories.

The small-screen table scrolls horizontally inside its assigned region. It has a
keyboard focus target and a visible scroll instruction; the page itself does not
overflow horizontally.

Browser setup, profile bundling, the loopback server and error collection use the
[shared browser qualification tool](../../../tools/Copeland.Qualification.Browser/README.md).
New compiler/browser reports go to this sample's ignored `artifacts/qualification/`;
committed review evidence remains the historical run. After qualification, inspect
the page with a foreground server (stop with Ctrl+C):

```powershell
npm run serve --prefix samples/copeland-ts/tablescript-application-m13
```

Open <http://127.0.0.1:4191/diagnostic/index.html>; `symbolic` and `production`
use the same URL shape. See the [sample index](../README.md) for the current entry points.
