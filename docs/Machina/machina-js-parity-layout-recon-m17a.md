# Machina JS Parity Layout Recon M17a

## Purpose

M17a is a recon-only milestone for the current Machina/Oblivion authoring surface.

Its job is to take the external Machina JS parity audit, compare that audit against the current C# repo facts, classify the reported bugs and cleanup items, and recommend an ordered migration ladder without changing runtime behavior.

M17a does not implement new layout primitives, does not refactor Oblivion renderers onto new layout primitives, and does not change playback behavior or product UI behavior.

## Audit input

Required input for M17a was the uploaded audit at `C:\Users\yuech\source\DevNotes\Machina UI\Claude M17 audit.md`.

That audit's main themes match the current repo state:

- current C# authoring expresses component presence reasonably well, but spatial relationships are harder to read because card and page layout are written as explicit placement arithmetic
- `UI.Anchor(... top: cursorTop, height: X)` plus `cursorTop += X` is the dominant authoring noise source
- `.slot` wrapper ids leak lowering conventions into human-facing authoring code
- badge helpers are duplicated
- manifest writing repeats JSON/text boilerplate
- several layout/render bugs are plausible in the current authoring shape
- the JS version already has stronger layout concepts for stack, grid, guide/reference placement, row variants, proportional lengths, and DeusMachine state authoring

This document paraphrases those findings and maps them to the current repository rather than quoting them at length.

## Current C# authoring pain points

The current C# API is partially readable without running it, but only partially.

- `samples/Machina.Presenter.Sample/OblivionCardRenderer.cs` is structurally understandable. A reviewer can tell that a card has title, subtitle, source, meta badges, tags, body, actions, and artifacts.
- The same file is spatially noisy because those parts are positioned by manual vertical arithmetic rather than by one visible stack declaration.
- `samples/Machina.Presenter.Sample/OblivionWorkbenchCatalog.cs` has the highest layout-authoring noise in the current Oblivion lane. It repeats manual `currentTop` math for inspector sections, compact card lists, compact inspector shells, wide card panes, and wide inspector panes.
- `samples/Machina.Presenter.Sample/PresenterNavigationDocumentFactory.cs` is understandable but still expresses shell layout as explicit left/top/width/height math on `Row.Anchor(...)` calls.
- `samples/Machina.Presenter.Sample/PresenterNavigationLayout.cs` is clean enough as a geometry record. Its arithmetic is centralized and intentional.
- `samples/Machina.Presenter.Sample/PresenterCardLayoutHelper.cs` is clean enough as a measurement helper. It is not noisy by itself, but it exists partly because higher-level authoring does not yet expose the intended stack/frame model directly.
- `src/Machina.Core/Authoring/UI.cs` and `src/Machina.Core/Flat/Row.cs` are low-level and readable, but they expose the low-level placement vocabulary directly. They do not hide wrapper ids or offer an authoring-first stack/grid surface for the targeted sample code.

Current pain points by category:

- Manual cursor math:
  `OblivionCardRenderer.BuildCard`, `OblivionCardRenderer.BuildCollapsedMarkdownBody`, `OblivionCardRenderer.BuildCollapsedPlainBody`, `OblivionWorkbenchCatalog.BuildInspectorRows`, `BuildCompactCardListRows`, `BuildCompactInspectorRows`, `BuildWideCardsPane`, `BuildWideInspectorPane`.
- Manual column/grid math:
  `OblivionPageLayout.CreateWide/CreateCompact` and `PresenterNavigationLayout` compute shell and column geometry centrally, then downstream rows consume explicit coordinates instead of declarative tracks/cells.
- Id scaffolding noise:
  repeated `".slot"` ids in `OblivionCardRenderer`, `PresenterCard`, `OblivionMarkdownRenderer`, and direct `UI.Anchor(...)` authoring across the sample.
- Duplicate helpers:
  badge helpers in `OblivionCardRenderer` and badge-row construction in `PresenterCard`.
- Manifest boilerplate:
  repeated `JsonSerializer.Serialize(...)` plus parallel text manifest writing in `OblivionWorkbenchCatalog`.

Parts that are already clean enough:

- `PresenterNavigationLayout` as a shell geometry record
- `PresenterCardLayoutHelper` as a pure layout-measurement helper
- `UI.Row(...)` and `UI.Column(...)` for simple same-axis flow when content can be authored as direct stack children
- `StandardUI.TextBlock(...)` where rich text wrapping/clipping is already explicit

## Current C# layout primitives

The current C# repo already contains low-level layout primitives in `Machina.Layout`:

- `StackArrange`
- `FillFrame`
- `GridArrange`
- `CellFrame`

Those are implemented and covered by layout tests, and `UI.Row(...)` / `UI.Column(...)` already lower to stack arrangement for direct children.

The M17a gap is not "C# has no stack or grid at all."

The real gap is:

- the target Oblivion authoring path is still mostly written with `UI.Anchor(...)` and `Row.Anchor(...)`
- card/page authoring does not expose a convenient fixed-plus-fill stack surface
- page authoring does not expose a convenient grid/cell surface for the current two-column Oblivion shell
- wrapper-id conventions still leak into authoring code

Current relevant primitives:

- `src/Machina.Core/Authoring/UI.cs`
  low-level `UI.Anchor`, `UI.Row`, `UI.Column`, `UI.Layer`, `UI.Rect`
- `src/Machina.Core/Flat/Row.cs`
  low-level flat `Row.Anchor`, `Row.Fill`, `Row.Fixed`
- `src/Machina.Standard/Authoring/StandardUI.cs`
  component-layer helpers, including `TextBlock`
- `src/Machina.Core/Styling/UiStyle.cs`
  paint metadata only; still not a general layout API

## JS parity concepts

M17a inspected both the local JS source repo at `C:\Users\yuech\source\repos\MachinaLayout.JS` and the checked-in JS reference docs under `docs/Copeland/reference/machinalayout-js/`.

Relevant JS concepts confirmed in source/docs:

- `src/types.ts`
  `StackArrange`, `FillFrame`, `GridArrange`, `CellFrame`, `GuideFrame`, `EdgeRef`, `UiLength`, `LayoutRowVariant`
- `src/resolveLayoutDocument.ts`
  stack child rect resolution and guide/grid resolution logic
- `src/selectLayoutRowsForRoot.ts`
  row-level responsive variant selection
- `src/deus/types.ts`
  row-first DeusMachine shape

Important parity observation:

- JS already exposes these concepts as first-class authoring vocabulary.
- Current C# already has some of the low-level layout engine pieces, but the current Oblivion sample does not author against them at the same semantic level.

## StackArrange + FillFrame

### Current C# equivalent

Low-level equivalent already exists:

- `StackArrange` in `src/Machina.Layout/Frames/StackArrange.cs`
- `FillFrame` in `src/Machina.Layout/Frames/FillFrame.cs`
- `UI.Row(...)` and `UI.Column(...)` lower to stack arrangement for direct children

What is still missing for the target authoring path is an ergonomic authoring surface for mixed fixed/fill vertical sections inside cards and panes.

### Exact files where stack-first authoring would remove noise

- `samples/Machina.Presenter.Sample/OblivionCardRenderer.cs`
  title/subtitle/source/meta/tags/body/footer composition
- `samples/Machina.Presenter.Sample/OblivionWorkbenchCatalog.cs`
  inspector title plus stacked sections, compact shells, and wide pane content
- `samples/Machina.Presenter.Sample/PresenterCard.cs`
  title/badge/body composition, though this file is already cleaner than `OblivionCardRenderer`

### Recommended target C# shape

Recommended first slice: add an authoring-level stack builder over the existing low-level stack/fill machinery instead of inventing a second layout engine.

Representative shape:

```csharp
UI.VStack(
    id: "card.layout",
    gap: 6,
    children:
    [
        UI.VItem(height: 24, child: title),
        UI.VItem(height: 18, child: subtitle),
        UI.VItem(fill: 1, child: body),
    ]);
```

Equivalent lower-level shape would also be acceptable if the public API stays readable:

```csharp
ArrangeSpec.Stack(axis: Axis.Vertical, gap: 6)
FrameSpec.Fixed(height: 24)
FrameSpec.Fill(weight: 1)
```

### Lowering model

Recommended lowering model:

- stay inside existing C# `Machina.Layout` stack/fill primitives
- add authoring helpers that synthesize the direct arranged children and their frames
- keep output deterministic and flattenable
- do not add a second cursor-based helper that still emits visible `top` math to callers

### Tests needed

- authoring/lowering tests that verify new stack authoring lowers to existing `StackArrange` and `FillFrame`
- targeted sample tests around card structure if and when `OblivionCardRenderer` is migrated in M17c
- regression tests proving no behavior change in current playback scenarios after M17b/M17c

### Risks

- wrapper-id and lowering conventions must stay deterministic
- migrating only some card subtrees can temporarily increase mixed-style complexity
- body/footer height semantics need careful preservation because current cards already rely on `PresenterCardLayoutHelper`

### Why this should be first

This is the highest leverage slice because it removes the dominant readability problem without requiring page-level shell rewrites first.

M17b should therefore be "authoring parity over existing stack/fill layout machinery," not "new low-level layout math."

## GridArrange + CellFrame

### Current C# equivalent

Low-level equivalent already exists:

- `GridArrange` in `src/Machina.Layout/Frames/GridArrange.cs`
- `CellFrame` in `src/Machina.Layout/Frames/CellFrame.cs`

Current target sample code does not use them for the Oblivion page shell.

### Current manual grid/column math

The current wide-page shell still computes explicit two-column geometry and then anchors both panes manually.

Relevant areas:

- `OblivionPageLayout.CreateWide/CreateCompact`
  manual width, gap, and left-offset decisions
- `OblivionWorkbenchCatalog.BuildPageRows`
  explicit cards panel and inspector panel anchors
- `PresenterNavigationLayout`
  explicit shell/sidebar/content geometry for the broader presenter shell

### What M17d/M17e should implement

- M17d should expose an authoring-level grid/cell helper over the existing low-level `GridArrange`/`CellFrame`
- M17e should move the Oblivion page shell onto that grid authoring path

Representative shape:

```csharp
UI.Grid(
    columns: [Track.Fill(1), Track.Fixed(332)],
    rows: [Track.Fill(1)],
    columnGap: 24,
    children:
    [
        UI.Cell(col: 0, row: 0, child: cardsPane),
        UI.Cell(col: 1, row: 0, child: inspectorPane),
    ]);
```

### Risks

- current independent-pane scrolling is intentional and must survive the refactor
- compact mode is currently a document-factory swap, not a row-level variant, so grid refactor should not accidentally fold compact/wide into one risky change
- card hit-testing and scroll-region routing depend on stable pane identities and bounds

### Recommendation

Grid should remain second after stack because the current biggest noise source is vertical composition inside cards and panes. Page-shell grid cleanup is important, but it is not the first readability bottleneck.

## UiLength proportional unit

Current C# already has a `UiLength` type for anchor fields, but the current target sample still computes proportional sizes manually in authoring/layout records.

Current examples of manual proportional/clamped sizing:

- `OblivionPageLayout`
  inspector width selection and related column sizing
- `PresenterNavigationLayout`
  shell geometry remains scalar-record driven rather than proportion-driven

Recommended mapping:

- add a proportional helper at the same `UiLength` layer rather than inventing a separate percentage type
- represent proportional intent explicitly, then clamp at the consuming layout API

Representative direction:

- `UiLength.Proportional(0.4)`
- optional min/max constraints at grid-track or frame level rather than ad hoc `Math.Max/Math.Min` at call sites

Priority recommendation:

- land after grid authoring, not before
- proportional lengths become more valuable once grid/stack authoring can consume them directly

## Row variants

Current C# adaptive behavior lives at the document-factory level:

- `PresenterNavigationDocumentFactory` builds different shells for wide versus compact mode
- `PresenterNavigationLayout` resolves shell measurements from a chosen mode
- M12h already established that top-level mode switching is deliberate and deterministic

JS variants are more granular:

- `LayoutRowVariant` allows row-level overrides selected from root size conditions

Recommendation:

- do not prioritize row variants immediately for the M17 stack/grid readability arc
- keep the current M12h shell-mode split for now
- revisit row variants after stack/grid authoring is cleaner and after there is a concrete need for per-row responsive overrides inside one document path

Likely priority: later than proportional lengths.

## GuideFrame + EdgeRef

JS `GuideFrame` solves cross-node reference placement such as "position this relative to another node's edge."

Current C# cross-node positioning is mostly handled by:

- centralized layout-record arithmetic
- explicit left/top offsets
- parent-local anchoring

Current Machina/Oblivion needs do not appear to require guide placement urgently.

Recommendation:

- defer until there is real overlay/popover/tooltip/floating-panel pressure
- do not pull `GuideFrame` into the M17b-M17e stack/grid cleanup arc

This is valuable future vocabulary, but not the current highest-payoff parity slice.

## DeusMachine state machine shape

JS `DeusMachine` is already a row-first state-machine surface with:

- explicit states
- explicit transitions
- optional guards
- optional actions
- optional utility scoring and hysteresis

That shape is relevant to future `Machina.Dominatus.Ui` or other UI state-machine work, but it is separate from the current layout readability problem.

Recommendation:

- document it now
- keep it out of the current layout-refactor milestone ladder until the layout authoring work is further along

Likely separate later milestone, not part of M17b-M17e.

## Bug classification

### Bug A — Inspector title clips

Status after code inspection: confirmed as a likely bug.

Evidence:

- `OblivionWorkbenchCatalog.BuildInspectorRows` and `BuildWideInspectorPane` render `"Selected card inspector"` as raw `TextSize.H1`
- the current code does not express a wrap or truncate policy there
- wide inspector width can shrink, and compact/wide variants still use fixed title heights

Classification:

- quick fix before parity, or at latest the next UI cleanup slice

Recommended eventual fix:

- use a smaller text size or a `TextBlock`/truncate-capable surface for narrow inspector headers
- prefer an explicit overflow policy over relying on current clipping

Do not change it in M17a.

### Bug B — Inspector/card column independent heights

Status after code inspection: mostly design behavior, not a standalone bug.

Evidence:

- wide mode intentionally builds independent card-stack and inspector panes
- M15e explicitly separated scroll ownership and pane behavior
- mismatched bottom edges can happen because the panes are intentionally independent

Classification:

- design behavior / future grid concern

Recommended handling:

- document it clearly
- preserve it through M17d/M17e unless the product direction explicitly changes pane coupling

### Bug C — Footer overlap with Markdown body

Status after code inspection: current audit wording overstates the exact path, but there is still a real layout-coupling risk in the collapsed plain-body path.

Evidence:

- footer rows are added inside `BuildCollapsedPlainBody`
- footer placement depends on `PresenterCardLayoutHelper` footer/body partitioning
- layout height is reserved from untrimmed badge presence rather than from final rendered badge-row shape

Important nuance:

- collapsed Markdown body currently uses `BuildCollapsedMarkdownBody`, not `BuildCollapsedPlainBody`
- the strongest overlap risk is therefore "body/footer coupling in the current card-body/footer composition model," not specifically "Markdown preview always overlaps footer"

Classification:

- fix during M17c stack refactor

Recommended handling:

- move card body and footer into one explicit stack-authored composition so measurement and placement come from the same structure

### Bug D — ComputeLayout overload / LimitLabels mismatch

Status after code inspection: confirmed as a maintainability and measurement risk.

Evidence:

- `ComputeLayout(...)` reserves footer height from raw badge-list presence
- `LimitLabels(...)` can later add an overflow badge
- `OblivionCardRenderer` computes layout and renders badge rows through separate logic paths

Classification:

- fix during M17c stack refactor

Recommended handling:

- compute final visible badge rows once
- feed the same row model into both measurement and rendering
- remove duplicated layout/render assumptions

## Boilerplate cleanup classification

### `.slot` id auto-derivation

Current locations:

- heavy in `OblivionCardRenderer`, `PresenterCard`, `OblivionMarkdownRenderer`, and direct anchor authoring across the sample

Payoff:

- high readability payoff
- moderate authoring churn reduction

Risk:

- medium, because id stability matters for hit-testing, tests, and artifact/debug expectations

Recommended milestone:

- after or alongside M17b, but scoped carefully

Layout-critical or cleanup:

- layout-adjacent readability cleanup, not a layout primitive itself

### Duplicate badge helpers

Current locations:

- `OblivionCardRenderer`
- `PresenterCard`

Payoff:

- medium

Risk:

- low

Recommended milestone:

- M17c when card renderer composition is already being touched

Layout-critical or cleanup:

- cleanup, but helps avoid measurement/render drift

### Generic `ManifestWriter<T>`

Current locations:

- repeated manifest methods in `OblivionWorkbenchCatalog`

Payoff:

- medium for maintainability
- low for layout parity directly

Risk:

- low to medium if over-generalized

Recommended milestone:

- later cleanup milestone, not part of the critical layout parity ladder

Layout-critical or cleanup:

- cleanup only

## Recommended migration ladder

Recommended staged ladder after M17a:

```text
M17a:
  JS parity layout/refactor recon

M17b:
  authoring-level StackArrange + FillFrame parity over existing C# layout primitives

M17c:
  refactor Oblivion card renderer onto stack layout and unify card measurement/render inputs

M17d:
  authoring-level GridArrange + CellFrame parity over existing C# layout primitives

M17e:
  refactor Oblivion page layout onto grid layout while preserving independent panes

M17f:
  UiLength proportional/clamp support in authoring-facing layout APIs

M17g:
  row variants where a real responsive override need remains after grid/stack cleanup

M17h:
  GuideFrame + EdgeRef for overlays/floating alignment

M17i:
  DeusMachine shape parity as a separate state-machine authoring milestone
```

Priority rationale:

- M17b first because the biggest current pain is vertical cursor arithmetic inside cards and panes
- M17c second because it converts the noisiest real renderer to the new authoring shape and can absorb the card-specific bugs
- M17d/M17e next because page-level shell math is the next-largest readability issue
- proportional lengths, variants, guide frames, and DeusMachine all have value, but none of them remove the main current authoring friction as directly as stack/card cleanup

## What changed

M17a changes documentation, planning, deterministic manifest output, and lightweight test coverage only.

It adds:

- this recon document
- a focused layout-authoring backlog document
- an M17a deterministic manifest under `artifacts/m17a`
- lightweight tests that assert the recon-only contract
- roadmap/readme updates so the next implementation slices are explicit

Follow-through note:

- M17b is the first implementation follow-through and adds the authoring-level stack surface over the existing low-level stack/fill primitives
- M17b still does not migrate `OblivionCardRenderer` or page layout
- M17c then applies that surface to `OblivionCardRenderer` internals only, fixes the documented card body/footer and badge-measurement risks, and still does not refactor page layout or implement `UI.Grid(...)`

## What did not change

M17a does not:

- add new C# layout primitives
- change `OblivionCardRenderer`
- change `OblivionWorkbenchCatalog` layout behavior
- change `PresenterNavigationDocumentFactory`
- change playback scenario behavior
- change product UI behavior
- add editor work
- add notebook execution
- add Aurelian work
- add `VD-MIR` work

## Deferred work

Deferred beyond M17a:

- authoring-level stack/frame API implementation
- authoring-level grid/cell API implementation
- card renderer migration
- page shell migration
- proportional/clamped authoring lengths
- row-level responsive variants
- guide/reference placement
- DeusMachine parity in C#
- any bugfix that changes visible runtime behavior
