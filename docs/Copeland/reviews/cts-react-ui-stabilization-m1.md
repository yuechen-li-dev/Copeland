# CTS-REACT-UI-STABILIZATION-M1

## Outcome

**Success.** The named correctness defects from the React friction run are
fixed through the existing compiler and projection owners. The motivating
website cards now expose their complete prose in all three profiles, and the
triage page realizes its declared fixed width. No grammar change landed.

| Motivating observation | M0 | Stabilized path |
|---|---|---|
| Wide triage list declared 300px | 234.41px desktop; 211.77px tablet | Exactly 300px in both profiles |
| Article `gap: 12px; padding: 16px` | Accepted local facts were dropped | Emitted spacing; actual gap asserted as 12px |
| Mobile triage | Lateral layout squeezed its fixed region | Typed viewport state selects existing compact layout below 600px |
| Website paragraph access | Four hidden paragraphs on tablet; four on mobile | All 12 card paragraphs reachable; keyboard End reveals every tail |
| Imported enum constructor inside explicit callback | False `COPE-CALL-0017` | Compiles; eleven adapter helpers removed |
| `Workspace: {name}` | Space before interpolation removed | Space retained; real save text matches ordinary TS |
| Integer `key` / `tabIndex` | Key rejected; string-only prop assumption | Typed integer props emitted directly |
| Native input event outside the admitted contract | Four cascading diagnostics | One `COPE-REACT-0017` with structured unsupported-capability repair |
| Runtime stream-root width | Same dimension error twice | One diagnostic for the authored property |
| Enum case/pattern sharing a name with a foreign helper | False module-privacy error | Syntax-classified enum names; real foreign calls still require import |

## Owning changes

`LayoutJavaScriptProjectEmitter` now projects stack tracks along their actual
main axis: fixed tracks use a non-shrinking basis, fill receives remaining
space, and fit preserves content sizing. Cross-axis fill remains host-relative.
It projects local gap, padding, and explicit overflow, and retains affine
length information instead of discarding its normalized component. Physical
hosts remain whenever they carry layout policy; neutral single-content hosts
can still flatten when that preserves their meaning.

Style projection reuses the existing `MachinaBrowserLowerer` style declarations
for both fixed and host-flow layouts. The change adds no alternate geometry
resolver or style model. The `LayoutDataCompiler` binds root facts once and
reuses those immutable facts for the stream wrapper and root, eliminating
duplicate diagnostics without globally suppressing errors.

Private CSS emission is staged per presentation. Unsupported style lengths
produce the existing named `COPE-MACHINA-STYLE-0001` diagnostic, and a failed
presentation publishes no partial CSS. Existing layout style authoring remains
the bounded fill/border subset; this does not introduce additional style fields.

The React binder preserves meaningful inline spaces and ignores indentation
introduced solely by formatting newlines. Integer keys and focus order retain
their numeric type. Native `<input>` is admitted as an intrinsic; native
`onChange` / `onInput` event objects remain outside the bounded contract and
receive a precise diagnostic with `repairKind: unsupported-capability` and a
suggested contracted `Input.onValueChange: (string) => void` alternative. The
real controlled-input fixture continues to use that existing Base UI contract.

Capture classification distinguishes static enum declarations by symbol
identity. Imported constructors need no runtime environment slot. Actual enum
values and parameters shadowing enum names still require explicit capture.
Module privacy uses the parsed enum-case and match-pattern token positions to
distinguish declarations/patterns from function invocations. A real function
call inside a match arm still requires a named import.

## Application policy and observable behavior

The website retains its assigned, bounded card frames. Each private card stream
declares `overflow: scrollY`; the article owns natural content height and a
keyboard focus target. The previous component-local hidden overflow is removed.
The compiler preserves the physical scroll host and projects its declared
overflow policy. This is intentional scrolling inside the assigned frame,
not a claim that static page layouts now infer arbitrary intrinsic content size.

Desktop paragraphs fit without scrolling. Tablet card scroll extents are 34–67px,
and mobile extents are 21–48px. The browser proof focuses each actual card,
presses End, and verifies that the paragraph tail lies inside its scroll frame.
It checks all four cards at each width. The original full website proof also
passes its component-state, renderer-attachment, locality, teardown/recovery,
and profile checks. Its containment assertion now measures the assigned scroll
frame rather than requiring tall scroll content to be smaller than that frame.

The triage fixture selects its existing stacked layout below 600px through
typed state, a typed resize event, and the existing browser viewport contract.
The source chooses the topology; the backend preserves exact track semantics.
The wider-profile manual arrangement toggle retains selection. The whole
fixture continues to qualify search, bounded fourth-row insertion,
empty/loading/error/retry, validation, controlled inputs, dialog focus, Escape,
focus return, and reopening.

The intentionally undersized 180px prose specimen remains a negative witness
in both implementations. Fixed height plus visible overflow still requires an
appropriate author-declared policy; the backend does not silently replace that
height or clip text to manufacture a passing result.

## Validation and evidence

Final local validation on 2026-10-12:

- Full `Copeland.slnx` build: zero warnings/errors.
- Full solution tests: **2,127 passed**, zero failed/skipped; includes 14 new
  focused regressions for projection, whitespace, prop typing, diagnostics,
  capture boundaries, and module privacy.
- Strict ordinary TS and TypeScript tooling checks: passed.
- Ten current compiler corpus cases: all expected accept/reject results matched.
- Paired React browser corpus: 74 observations; shared copy matches and diagnostics
  remain clean across 1280, 768, and 390px, including 200% root text size and
  the alternate-font exercise.
- Live desktop → mobile → desktop resizing preserves selection and restores the
  exact 300px track in both implementations. Both article gap and padding are
  checked against their declared values in the actual browser.
- Website readability: 12 keyboard-readable card paragraphs at 1440, 768, 390px.
- Original full website browser proof: desktop/tablet/mobile passed.

The full .NET test run follows the final root-fact and diagnostic-metadata
changes. Browser evidence uses disposable Playwright contexts against real
generated artifacts in Microsoft Edge 153.0.4234.48. This remains a bounded
Windows/Chromium qualification, not universal browser or component-library
compatibility.

Evidence is separate from the historical M0 baseline:

- [Browser report](evidence/react-ui-stabilization-m1/browser-report.json)
- [Website paragraph access](evidence/react-ui-stabilization-m1/website-regression.json)
- [Compiler corpus](evidence/react-ui-stabilization-m1/compiler-report.json)
- [Authoring accounting](evidence/react-ui-stabilization-m1/authoring-accounting.json)
- [Full .NET validation](evidence/react-ui-stabilization-m1/validation.json)
- [Original website host proof](evidence/react-ui-stabilization-m1/website-host-proof.json)

The current application has 260 nonblank/noncomment source lines versus 288 in
the M0 baseline, despite adding typed viewport selection. All eleven event
adapter helpers are gone. This is source accounting for this fixture, not a
timed usability or general complexity claim. Remaining repeated layout bindings
still provide evidence for the subsequent authoring/grammar comparison.

## Rerun and remaining boundary

From the repository root:

```powershell
./samples/copeland-ts/react-friction-m0/run.ps1 -Stabilization
```

Use `-FullValidation` for the full .NET build/tests. The original website's
broader proof can be run from its directory after the managed build:

```powershell
$env:FRICTION_BROWSER_CHANNEL = 'msedge'
$env:TSPACK_PLAYWRIGHT_MODULE = '../react-friction-m0/baseline/node_modules/playwright'
node browser-proof.mjs
```

Runtime `ReactNode[]` children, variable stream collections, general native
React event-object contracts, and arrangement/content binding reuse remain
explicitly deferred. These are capability or authoring changes rather than the
basic correctness fixes in this round. The preserved corpus and restored
projection behavior now give the next grammar comparison a reliable baseline.
