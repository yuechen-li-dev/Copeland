# Machina/Oblivion Usability Re-entry Audit M15a

## Purpose

M15a is an audit-only milestone.

Its purpose is to inspect the current Machina presenter shell and the hosted Oblivion workbench surface against direct user feedback, isolate the concrete owners of the usability failures, and define a minimal, prioritized M15b implementation scope.

No main usability fixes are implemented in M15a beyond local audit exports and deterministic audit records.

## User feedback

Captured feedback:

```text
Good:
  It is fast. Literally one of the snappiest UI I've ever used.

Bad:
  Currently completely unusable for anything other than a workspace demo.
  Can't resize the window at all, or resize behavior is not usable.
  The window takes up less than a quarter of the screen.
  Text inside cards is not readable; it appears like little bars.
  Card previews cannot actually be read.
  No word wrap.
  Text overflows.
  Black-on-black or dark-on-dark text makes content unreadable.
```

Workbench doctrine for this audit:

```text
A workbench surface is not valid until its primary content is readable.

Speed does not compensate for unreadable content.

Cards must be useful before selection, not only after inspection.

Text must wrap, elide, or clip intentionally; it must never become accidental bars.
```

## Current strengths

- Presenter interaction is still extremely fast.
- The shell/page/card stack remains deterministic and easy to trace.
- Scroll state, selection state, and shell mode are explicit.
- Render-session caching already has the right basic shape for later size invalidation.

## Current blockers

- Runtime window size is fixed to startup export dimensions and runtime resize is disabled.
- Runtime layout is resolved once at startup and is not recomputed from later viewport changes.
- Card preview readability is inconsistent and often unreadable before selection.
- Plain preview text is clipped per line, not wrapped.
- Markdown preview text uses mixed rendering paths with inconsistent foreground rules.
- Dark preview backgrounds combine with default dark text in some Markdown summary paths.
- Presenter card `ClipContent` naming does not currently enforce clipping semantics.
- Inspector content is more readable than previews, but still relies on fixed section heights and partial overflow conventions.

## Window sizing and resizing

Current ownership:

- `samples/Machina.UI/Machina.Presenter.Sample/Program.cs`
- `samples/Machina.UI/Machina.Presenter.Sample/PresenterNavigationExportOptions.cs`
- `samples/Machina.UI/Machina.Presenter.Sample/PresenterNavigationLayout.cs`
- `samples/Machina.UI/Machina.Presenter.Sample/PresenterAdaptiveShell.cs`
- `samples/Machina.UI/Machina.Presenter.Sample/PresenterExporter.cs`
- `tools/Export-MachinaPresenter.ps1`

Findings:

- Resizing is explicitly disabled in runtime with `CanResize = false`.
- The presenter window is created from `PresenterNavigationExportOptions.Width/Height`, which default to `1120x760`.
- Runtime rendering then forces `Width` and `Height` back to the composed frame size on every redraw.
- The current adaptive-shell resolver is width-driven, but in runtime it resolves from startup options only, not from later live window bounds.

Answers:

- Is resizing disabled: yes.
- Is resizing technically allowed but layout not recomputed: not in the current runtime path, because resize is disabled first; if it were re-enabled naively, layout still would not recompute.
- Is the initial window size hard-coded: yes, through default presenter navigation options and the startup layout path.
- What files own the size: `Program.cs`, `PresenterNavigationExportOptions.cs`, `PresenterNavigationLayout.cs`, `PresenterAdaptiveShell.cs`, and the export script.
- What minimal M15b fix is likely: make the runtime window resizable, stop forcing window size on each redraw, recompute shell mode and layout from current client size, and rerender only when the resolved viewport actually changes.

## Layout recomposition

Viewport flow today:

- Width and height enter through `PresenterNavigationExportOptions`.
- `Program.cs` builds one `PresenterNavigationLayout` in the window constructor.
- `PresenterNavigationRenderSession` receives that layout and uses it for page render width, viewport height, scrollbar geometry, shell chrome geometry, and composition.

Invalidation behavior:

- Cached page render keys already depend on `ShellMode`, `ContentVisibleWidth`, selected card, and compact pane.
- Cached shell render keys already depend on the full `PresenterNavigationLayout`.
- If a fresh layout were supplied, the cache keys would invalidate correctly.

What breaks under resize:

- `_navigationLayout` is created once and stored as a readonly field.
- There is no size-changed handler that rebuilds layout from live client bounds.
- `PresenterShellModeResolver.Resolve(...)` is only applied from startup width.
- Scroll offset normalization uses viewport height, but only during render with the frozen startup layout.
- Card list and inspector geometry recompute only when page render is called with a new width, which does not happen during runtime resize today.

Likely minimal fix:

- Make runtime layout mutable.
- Recompute layout from current client size on window-size changes.
- Re-resolve shell mode from current width.
- Re-normalize per-page scroll offsets under the new viewport height.
- Re-render only after size changes that alter width, height, or shell mode.

## Card preview readability

Current owners:

- `samples/Machina.UI/Machina.Presenter.Sample/OblivionCardRenderer.cs`
- `samples/Machina.UI/Machina.Presenter.Sample/OblivionMarkdownRenderer.cs`
- `samples/Machina.UI/Machina.Presenter.Sample/PresenterCard.cs`
- `samples/Machina.UI/Machina.Presenter.Sample/PresenterCardLayoutHelper.cs`
- `samples/Machina.UI/Machina.Presenter.Sample/OblivionCardHandlers.cs`

Why previews currently fail:

- Compact cards are short by design, usually `168px`, so body space is very limited before metadata and footer rows are placed.
- Plain preview bodies use small single-line `UI.Text` nodes with clipping-by-ellipsis, not wrapping.
- Markdown preview bodies use mixed paths:
  - heading/code/diagnostic preview rows use single-line `UI.Text`
  - summary preview rows use `StandardUI.TextBlock`
- The preview frame background is hard-coded dark.
- Markdown summary previews inherit the default light-surface theme foreground through `StandardUI.TextBlock`, which produces dark text on a dark preview frame.

Why the preview can look like bars:

- the available preview body height is small
- text size is small
- wrapping is inconsistent
- clipped single-line fragments lose semantic value quickly
- dark-on-dark summary text removes readable glyph contrast almost completely

Selected-state black bar behavior:

- The dark card-body frame is intentional.
- The unreadable state on that frame is accidental because the text foreground contract is inconsistent across preview render paths.

Minimal M15b fix likely:

- keep the card model and renderer architecture
- make preview foreground explicit for dark preview bodies
- use one consistent bounded preview text strategy
- prefer wrapped summary or intentional multi-line elision over raw single-line overflow

## Word wrap and clipping

Current state:

- Word wrap exists in the Machina rich text layout engine and is used by `StandardUI.TextBlock`.
- Plain compact previews do not use that path; they use `PresenterCardLayoutHelper.ClipLinesToFit(...)`.
- Markdown summary preview entries wrap because they use `StandardUI.TextBlock`.
- Markdown heading/code/diagnostic preview entries do not wrap because they use raw `UI.Text`.

Important clipping finding:

- `PresenterCardOptions.ClipContent` currently does not implement clipping semantics.
- In `PresenterCard.BuildTextCard(...)` it only toggles whether a dark body background is painted.
- This means the codebase has clipping intent in names, but not a reliable clipping contract at the presenter-card level.

Answers:

- whether word wrap exists in card previews: partially, only for Markdown summary text rendered through `StandardUI.TextBlock`
- whether only inspector/body renderer wraps: mostly yes; inspector Markdown is the clearest wrapped path
- whether clipping is wrong: clipping is mostly simulated through line trimming, not enforced as a general rendered clip region
- whether overflow text paints outside card body: it can, because clip intent is not a hard render contract here
- what minimal M15b fix is likely: use one bounded preview strategy per preview body, either wrapped summary text with explicit line cap or explicit ellipsis at the preview renderer boundary

## Contrast and theme readability

Primary dark-on-dark source:

- `OblivionCardRenderer.BuildBody(...)` and `OblivionMarkdownRenderer.BuildPreviewBody(...)` paint a dark preview frame (`0x0B1220FF`).
- `OblivionMarkdownRenderer.BuildPreviewBody(...)` uses `StandardUI.TextBlock(...)` for summary rows without overriding foreground.
- `StandardTheme.Default.Colors.Foreground` is near-black (`0x09090BFF`).

That produces a direct dark-text-on-dark-background failure for Markdown summary previews.

Secondary contrast concerns:

- plain preview text uses muted light-gray text and is better than the Markdown summary path, but still too small and too compressed to be useful
- code and diagnostics colors are brighter, but still rely on single-line bounded rows
- selected-card visual treatment is acceptable as a border state; the preview body content inside it is the problem

Minimal safe contrast rules for M15b:

- any dark preview frame must also set explicit light foreground and light-muted foreground tokens
- preview text should not inherit light-surface defaults implicitly
- selected/active states must keep a minimum readable foreground-to-background contrast before any accent styling is applied

## Inspector readability

Inspector status:

- The inspector is better than the compact preview.
- Markdown inspector content uses `StandardUI.TextBlock` on light card surfaces, so wrapping and contrast are materially better there.
- The selected card body section is marked `ClipContent: false`, which matches the current non-clipping reality but does not create a scrollable local content region.

Remaining inspector problems:

- inspector sections use fixed heights
- long metadata/action/effect rows remain dense
- body readability depends on the section height budget chosen in handlers
- there is no separate inspector-local scroll region

Minimal fixes aligned with preview work:

- keep inspector typography and contrast rules consistent with preview rules
- widen readability margins before adding more dense metadata
- defer inspector-local scrolling unless preview readability and resizing are already fixed

## Scroll and adaptive shell behavior

Current state:

- Scrollbar behavior is deterministic and cached.
- Shell wide/compact mode still exists and is useful.
- Runtime adaptive behavior is startup-width driven, not live viewport driven.
- Oblivion page height currently floors to `1440`, so several pages remain scroll-oriented even when natural content is shorter.

Audit reading:

- scroll behavior itself is not the primary usability blocker
- the larger blocker is that resize and recomposition do not feed current viewport size back into shell/page assembly
- adaptive shell doctrine from M12h still exists, but it is not currently wired to live runtime window changes

## Export evidence

Local audit exports generated under `artifacts/m15a`:

- `m15a-oblivion-cards-current.png`
- `m15a-oblivion-docs-current.png`
- `m15a-oblivion-docs-compact-current.png`

These are audit evidence only, not pixel-golden tests.

## Prioritized fix plan

```text
P0:
  make presenter window resizable and make runtime layout recompute correctly.

P1:
  make card preview body text readable at default size.

P2:
  implement or apply word wrap / intentional elision in card previews.

P3:
  eliminate black-on-black / dark-on-dark unreadable text states.

P4:
  improve inspector readability consistently with card preview fixes.

P5:
  add density/font scale controls later, after default readability works.
```

## Proposed M15b scope

```text
M15b:
  Presenter resizing and readable card previews

Must include:
  resizable window,
  viewport/layout recompute,
  card preview word wrap or intentional elision,
  readable contrast,
  no accidental text bars,
  no text overflow outside card body,
  tests/exports.
```

## What changed

- audited runtime window sizing ownership
- audited layout recomposition ownership
- audited compact card preview rendering
- audited Markdown preview rendering and contrast mismatch
- generated local M15a presenter exports
- added deterministic M15a audit documentation and manifest records

## What did not change

- no runtime resize fix
- no card preview wrap fix
- no new text renderer
- no editor mode
- no Markdown editing
- no notebook execution
- no Roslyn or xUnit execution work
- no Aurelian or VD-MIR work
- no renderer architecture change

## Deferred work

- full M15b implementation
- density/font scale controls
- inspector-local scrolling if still needed after M15b
- broader shell visual refinement after readability and resize correctness are fixed

## M15b follow-through

M15b now implements the narrow follow-through recommended by this audit:

- controlled live runtime resizing through a letterboxed `16:9` presenter surface
- minimum usable runtime surface sizing
- runtime/export sizing separation
- live layout recomposition from the effective presenter surface
- live shell-mode resolution from the effective presenter width
- readable compact card previews with wrap-or-elide behavior and explicit contrast fixes

It still does not implement arbitrary freeform `2D` layout, editor work, execution work, Aurelian work, or `VD-MIR` work.
