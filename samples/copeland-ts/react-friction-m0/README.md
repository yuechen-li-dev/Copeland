The current state/input pass owns the React application in `src/Application.tsx`
with `state: AppState` and typed `on Apply(event: AppEvent)` / `on.Apply`.
The existing canonical browser frame registry owns input delivery and state;
React subscribes to presentation snapshots. `src/Main.ts` only mounts the application.
The default run writes `react-state-on-m6` evidence, including mounted lifecycle
and FLOW probes in all three JavaScript profiles. See
[the M6 report](../../../docs/Copeland/reviews/cts-react-state-on-m6.md).

The semantic API authors issue triage in `src/IssuesPage.ts`
through choice, notice, action, header, and reading records from `src/UI.tsx`.
It contains no intrinsic HTML tags. Runtime immutable issue rows support repeated
insertion, removal, filtering, and reorder; React owns keyed reconciliation.
Existing layout templates and contribution streams still own arrangement.
Focused callback/identity browser probes remain in the default run. See
[the M5 report](../../../docs/Copeland/reviews/cts-react-ui-semantic-api-m5.md)
for source accounting, validation, and supported boundaries.

# CTS-REACT-FRICTION-M0

The current fixture includes the
[contribution stream and named capture grammar pass](../../../docs/Copeland/reviews/cts-react-ui-grammar-m3.md).
The M0, grammar, layout-template, and
[stabilization](../../../docs/Copeland/reviews/cts-react-ui-stabilization-m1.md)
corpora and reports retain their historical results. Current sources qualify M6.

Three paired webpages exercise article reading, issue triage, and workspace
settings. Copeland compiles through TSPack's current managed browser target.
The ordinary TypeScript counterpart passes strict `tsc` and builds with
esbuild. Both pages import the same `src/Content.ts` and use the same
TSPack-materialized React singleton and Base UI dialog modules.

The baseline report is [CTS React friction M0](../../../docs/Copeland/reviews/cts-react-friction-m0.md).

## Run

Requires .NET 10, Node 22.18+ with native TypeScript support, npm, TSPack, and
Microsoft Edge on Windows. This qualification used Node 26.2.0 and TSPack
0.1.14-m80i.1. All package versions are locked.

From the repository root:

```powershell
./samples/copeland-ts/react-friction-m0/run.ps1
```

The script builds the repository compiler, installs missing baseline tooling
with `npm ci`, type-checks both the baseline and TypeScript tooling, stages the
existing website browser-host source into an ignored local package artifact,
syncs locked packages, builds both Copeland fixtures, runs the compiler corpus,
and qualifies the pages. Browser servers use ephemeral loopback ports and are
closed in `finally`; browser contexts are disposable.

Use `-FullValidation` to also run the full `Copeland.slnx` build and tests.
Use `-RefreshLock` only to intentionally resolve dependencies again. If
using Playwright Chromium instead of Edge, install its browser once:

```powershell
node samples/copeland-ts/react-friction-m0/baseline/node_modules/playwright/cli.js install chromium
./samples/copeland-ts/react-friction-m0/run.ps1 -BrowserChannel chromium
```

To inspect both webpages after building:

```powershell
node samples/copeland-ts/react-friction-m0/tooling/server.ts
```

Open `http://127.0.0.1:4178/` for Copeland or
`http://127.0.0.1:4178/baseline/` for ordinary TypeScript. Ctrl+C closes the
temporary host. Navigation exposes Article, Issues, Settings, and an
intentional assigned-height overflow specimen.

## Exercise

Change copy length; insert the fourth issue; select an issue; change the
arrangement; search `Keyboard` and an absent title; select loading and error
states; retry; save an empty and then a valid workspace name; open the Base UI
dialog; close with Escape; reopen and close with its button.

The issue specimen activates one predeclared fourth row in Copeland. It does
not claim general runtime collection rendering. Loading/error are authored
state scenarios, not network requests. The ordinary TS version derives,
filters, and maps an array with stable keys. Copeland uses a curated string
callback contract for the real Base UI `Input` because native input callback
typing is not available. No input DOM-reading bridge is retained.

## Evidence

- `friction-corpus.json`: ten explicit compiler probes, including accepted
  local `fit`, separate layout/bind, and rejected collection/input cases.
- `stabilization-corpus.json`: current expectations; numeric keys and static
  enum callbacks compile, while native input events produce one repair diagnostic.
- `grammar-corpus.json`: eighteen probes, adding named capture expressions,
  module/local/class contribution streams, expression binding, and rejection cases.
- `state-on-corpus.json`: fifty expectations, preserving the semantic UI corpus
  and adding real application compilation, state/endpoint diagnostics, and
  mounted FLOW/lifecycle specimens across Diagnostic, Symbolic, and Production.
- `lifecycle-corpus.json`: current 55 expectations, typed effects, explicit child ownership,
  and cross-module probes in all three emission profiles.
- `docs/Copeland/reviews/evidence/react-effects-children-m7/`: current validation,
  twelve lifecycle observations per profile, screenshots, and regression evidence.
- `docs/Copeland/reviews/evidence/react-state-on-m6/`: historical compiler, browser,
  lifecycle, source-accounting, original-website, and full .NET validation evidence.
- `docs/Copeland/reviews/evidence/react-friction-m0/`: tracked browser,
  compiler, original-website, authoring-accounting, and validation baseline reports.
- `docs/Copeland/reviews/evidence/react-ui-stabilization-m1/`: stabilization evidence.
- `docs/Copeland/reviews/evidence/react-ui-grammar-m3/`: current evidence.
- `artifacts/screenshots/`: ignored screenshots generated by qualification.
- `artifacts/react-ui-grammar-m3/screenshots/`: current screenshots.

The browser harness checks content parity, native lists/labels, prose bounds,
selection retention, actual controlled inputs, validation, dialog focus,
Escape, and reopening at 1280, 768, and 390 CSS pixels. It tests 200% root text
size and a monospace fallback; that is not a browser-zoom or font-network
qualification. Fixed-height overlap is a required negative witness, not a
successful layout assertion. The original website cards remain an independent
regression specimen at 1440, 768, and 390 pixels.

Stabilization additionally asserts the declared 12px article gap and 16px
padding, exact 300px
wide-profile list track, compact mobile arrangement, and full keyboard access
to the original website cards' paragraph tails through their declared scroll
surfaces. The fixture's viewport state selects its existing compact layout
below 600px. A manual arrangement toggle applies to the wider profiles.
Live desktop/mobile resizing verifies that selection survives and the exact
wide track returns when the viewport widens.

## Effects and child ownership page

The default runner now qualifies M7. To inspect its separate editable-results page
following the build:

```powershell
node samples/copeland-ts/react-friction-m0/tooling/lifecycle.ts --serve
```

Open the printed URL. Edit both drafts, reverse the order, save, remove/restore
blue, and search `one`, `none`, or `all`. Drafts survive reordering; removal ends a
child lifetime. The sample HTTP service is deterministic and echoes saves rather
than retaining them. Ctrl+C closes the foreground host.

The application declares effects and typed result matching in
`inputs/lifecycle-demo.tsx`. State ownership uses `ownChild(slot, key, Child(...))`.
See [the M7 contract](../../../docs/Copeland/reviews/cts-react-effects-children-m7.md)
for phase ordering, expected diagnostics, and the unqualified concurrency and
attachment-settlement boundaries. M7 updates the state/on corpus to accept declared effects. Earlier M6 reports
retain the historical rejection evidence; `lifecycle-corpus.json` qualifies the
current phase and completion behavior.
