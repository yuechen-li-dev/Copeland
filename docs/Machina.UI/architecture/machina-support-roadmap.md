# Machina Support Roadmap

## Purpose

This roadmap tracks support status across Machina packages so implementation branches can coordinate on one shared matrix for layout, core declarations, runtime control, rendering, and presenter integration.

## Status Legend

- **Implemented**: feature exists with tests or concrete runtime proof.
- **Partial**: capability exists but lacks full behavior, integration, or hardening.
- **Planned**: accepted direction, not implemented yet.
- **Deferred**: intentionally postponed.
- **Unknown**: needs audit before claiming support.

## Package Map

- **Machina.Layout**  
  layout rows, frame model (`RootFrame`, `AbsoluteFrame`, `AnchorFrame`, `FixedFrame`, `FillFrame`, `CellFrame`), stack/grid arrangement, layout compile/resolve documents, resolved tree projection.

- **Machina.Core**  
  UI declarations/builders (`UI.*`), styles/semantics/actions metadata, deterministic measurement seam, lowering into layout rows/snapshots.

- **Machina.Standard**  
  standard component layer (current subset implemented; broad catalog in-progress).

- **Machina.Runtime**  
  hit-testing and dispatch-table runtime transitions.

- **Integrations/Machina.Dominatus:** optional Dominatus-hosted coarse UI behavioral-scope proof. It is outside Machina core, not used by the general samples, and does not revive the retired render-command bridge.

  Reference-only upstream Dominatus source is available under `reference/dominatus`. The active adapter remains pinned to NuGet `Dominatus.Core` and `Dominatus.OptFlow` `0.4.0`.

- **Machina.Renderer.Raster**  
  CPU raster surface + primitive pixel operations.

- **Machina.Renderer.Raster.Text**  
  raster text seam/debug text rendering.

- **Machina.Renderer.Raster.Dominatus**  
  Dominatus actuator adapter targeting raster backend and artifact flows.

- **Machina.Pipeline**  
  reusable `UiNode`-to-`MachinaFrame` service (lowering/layout/hit-test/render/raster).

- **Machina.Fonts**
  production font records, generation adapters, atlas artifacts, and proof-path rendering substrate.

- **Machina.Fonts.Tooling**
  human-facing and LLM-facing font diagnostics, CAD-style overlays, artifact export, and numeric/visual diff workflows.

- **samples/Machina.UI/Machina.Presenter.Sample**
  Avalonia bitmap presenter sample and interactive proof slice.

- **samples/Machina.UI/Machina.ComponentGallery.Sample**
  canonical local widget wall / visual workbench for StandardUI.

## Current Vertical Slice

Current proven path (with some steps still partial depending on scenario breadth):

`UiNode`  
-> `UiLoweringResult`  
-> `LayoutDocument`  
-> `ResolvedLayoutDocument`  
-> `UiHitTestIndex`  
-> `MachinaPresentationFrame`<br>
-> `Aurelian.Machina` translation<br>
-> Aurelian resolved-2D backend
-> Avalonia bitmap window  
-> pointer hit-test  
-> `UiAction`  
-> dispatch table/runtime proof  
-> redraw.

Manual GUI validation depth across broader component scenarios is **Partial / needs continuing audit**.

M7a adds a dedicated component gallery so broader StandardUI visual inspection no longer has to overload the presenter sample. M7b formalizes its repeatable PNG export contract and local artifact workflow. M7c uses that workflow for evidence-backed visual defect triage and deferral documentation. M7d closes the deferred badge intrinsic-size / label-placement issue with a Badge-local contract and no general layout-engine changes. M7e records the current stable baseline and known limitations so the gallery can keep serving as the canonical local visual workbench without pretending typography and interaction fidelity are finished.

M11g closes out the current Oblivion substrate. Markdown cards are next, and Roslyn/xUnit execution is now explicitly deferred to M13+ or later unless explicitly re-prioritized.

## Visionary / Aurelian topology note

M13b stabilizes the imported `Aurelian.slnx` as a separate build lane. That work changes dependency topology only:

- Aurelian uses Dominatus NuGet packages for active dependencies.
- `reference/dominatus` remains reference-only.
- Machina production packages do not gain `Aurelian.Runtime`, `Aurelian.Graphics`, or Vulkan dependencies in M13b.
- no `Machina.Aurelian` bridge is implemented yet.

M13c then follows with test normalization and docs dogfood only:

- the remaining Aurelian shader test issue is fixed at the assertion boundary by line-ending normalization only
- selected `docs/Aurelian/...` files now compile through `Copeland.Markdown` and appear as generated cards under `Oblivion -> Docs`
- Machina production packages still do not gain Aurelian runtime or Vulkan dependencies
- no `Machina.Aurelian` bridge is implemented yet

M13d then clarifies the compiler-side doctrine that Machina depends on conceptually but does not own:

- Copeland is the compiler workshop for Visionary
- Copeland hosts explicit compiler lanes rather than one universal IR mandate
- `Copeland.Shaders` is too narrow to name the whole architecture
- Machina should present compiler artifacts and diagnostics, not own compiler semantics

## Support Matrix

| Feature | Package | Status | Tests/Proof | Notes / Next Step |
|---|---|---|---|---|
| RootFrame | Machina.Layout | Implemented | Layout tests | Core frame primitive.
| AbsoluteFrame | Machina.Layout | Implemented | Layout tests | Direct absolute geometry.
| AnchorFrame | Machina.Layout | Implemented | Layout tests | Constraint validation implemented.
| FixedFrame | Machina.Layout | Implemented | Layout tests | Stack/grid child primitive.
| FillFrame | Machina.Layout | Implemented | Layout tests | Weighted fill support.
| CellFrame | Machina.Layout | Implemented | Layout tests | Grid placement primitive.
| StackArrange | Machina.Layout | Implemented | Stack arrange tests | Deterministic arithmetic layout.
| GridArrange | Machina.Layout | Implemented | Grid arrange tests | Deterministic explicit grid model.
| ResolvedLayoutDocument | Machina.Layout | Implemented | Resolver tests | Canonical flat resolved geometry.
| ResolvedLayoutTree | Machina.Layout | Implemented | Tree builder tests | Derived projection for adapters/debug.
| Clipping/overflow semantics | Layout/Renderer | Partial | Raster contract/docs | Basic rectangular behavior exists; richer semantics pending.
| Scrolling | Layout/Runtime/Presenter | Partial | Presenter shell tests + export proof | M10a adds explicit presenter-local viewport/content/offset state and deterministic scrollbar geometry; M10b adds sample-local wheel routing; M10c makes that shell the canonical presenter sample surface; M10d adds sample-local track click and thumb dragging without changing shared layout semantics; M11c replaces nullable drag routing with explicit interaction states and cached composition so scroll offset changes do not full-rerender page content or shell chrome.
| Z/painter ordering | Layout/Renderer | Partial | Contract-level coverage | Expand explicit tests across render adapters.
| UI.Text | Machina.Core | Implemented | Core tests | Deterministic measurement seam integrated.
| UI.Rect | Machina.Core | Implemented | Core tests | Style + child lowering path.
| UI.Row | Machina.Core | Implemented | Core tests | Lowers via stack arrangement.
| UI.Column | Machina.Core | Implemented | Core tests | Lowers via stack arrangement.
| UI.Stack | Machina.Core | Implemented | Core tests + layout resolution tests | M17b adds explicit fixed/fill stack authoring over existing `StackArrange`/`FillFrame`; M17c then uses that surface in `OblivionCardRenderer` internal card composition without refactoring the page shell; M17f closes the stack/grid adoption arc and records stack authoring as part of the current baseline.
| UI.Grid | Machina.Core | Implemented | Core tests + existing grid arrange tests + playback regression suite | M17d adds authoring-level grid/cell helpers over the existing `GridArrange`/`CellFrame` engine, including explicit sparse cells and matrix authoring. M17e then uses that same surface for the Oblivion wide page shell without adding new grid primitives. M17f closes the stack/grid adoption arc and records grid authoring as part of the current baseline.
| UI.Container | Machina.Core | Implemented | Core tests | Alignment data modeled; behavior partly deferred.
| UI.Button | Machina.Core | Implemented | Core+Standard tests | Action/semantics integration baseline.
| HSpace/VSpace | Machina.Core | Implemented | Core tests | Deterministic fixed spacer lowering.
| Styles | Machina.Core | Implemented | Style record tests | Immutable records, no CSS cascade.
| Text styles | Machina.Core | Implemented | Style/measurement tests | Deterministic size tokens.
| Semantics | Machina.Core | Implemented | Core tests | Text/button semantics emitted.
| Actions | Machina.Core | Implemented | Core/runtime tests | Metadata intent with typed `UiActionId`; runtime handles dispatch.
| Measurement seam | Machina.Core | Implemented | Measurement tests | Deterministic fake measurer default.
| Lowering snapshots | Machina.Core | Implemented | Snapshot tests | Stable artifact surface.
| Explicit ids | Machina.Core | Implemented | Core tests | Deterministic generation + validation.
| Standard Button | Machina.Standard | Implemented | Standard tests | Current stable standard control.
| Card | Machina.Standard | Partial | Standard snapshot tests | Verify final API/behavior contract.
| Badge | Machina.Standard | Implemented | Standard badge geometry tests + gallery geometry/render tests + local PNG inspection | M7d adds deterministic intrinsic width/height and local label-region placement without changing general layout semantics.
| Separator | Machina.Standard | Partial | Snapshot-level evidence | Confirm layout/render conventions.
| Label | Machina.Standard | Partial | Snapshot-level evidence | Formalize semantic behavior.
| Field | Machina.Standard | Partial | Standard form tests | Expand interaction states.
| Input shell | Machina.Standard | Implemented | Standard + pipeline style contract tests | M5c2 fully wires Input style record; full text editing runtime remains planned.
| Checkbox | Machina.Standard | Implemented | Standard form tests | Dispatch-friendly selection control available.
| Switch | Machina.Standard | Implemented | Standard form tests | Dispatch-friendly selection control available.
| Standard TextBlock | Machina.Standard | Implemented | Standard + Dominatus + presenter tests + local Windows visual audit | First visible Standard rich text surface; broad control migration deferred.
| Checkbox checked-state visual fix | Machina.Standard | Implemented | Standard + presenter tests + local Windows visual audit | M6e.1 hardens checked mark contrast for the current raster renderer before M7a gallery work. |
| Tabs | Machina.Standard | Deferred | None | Post-core interaction milestone.
| Dialog | Machina.Standard | Deferred | None | Depends on modal/runtime stack.
| Popover | Machina.Standard | Deferred | None | Depends on layering/focus model.
| Tooltip | Machina.Standard | Deferred | None | Depends on hover/timing model.
| Dropdown | Machina.Standard | Deferred | None | Requires focus + text/input infra.
| Select | Machina.Standard | Deferred | None | Same dependency set as dropdown.
| Combobox | Machina.Standard | Deferred | None | Requires editing + filtering + focus.
| Table/DataTable | Machina.Standard | Deferred | None | Needs virtualization/sizing strategy.
| Sidebar | Machina.Standard | Deferred | None | Depends on routing/nav model.
| Toast | Machina.Standard | Deferred | None | Depends on async/effect orchestration.
| Calendar/DatePicker | Machina.Standard | Deferred | None | Later feature milestone.
| Hit testing | Machina.Runtime | Implemented | Runtime tests | Pointer hit index exists.
| Dispatch Set/Toggle/Increment | Machina.Runtime | Implemented | Dispatch tests | Deterministic state transitions.
| Prefix/suffix dispatch | Machina.Runtime | Implemented | Dispatch tests | Supported by runtime dispatch model.
| Pointer click action | Runtime + Presenter | Partial | Presenter/runtime proofs | Broaden scenario tests.
| Keyboard input | Runtime/Presenter | Implemented | Presenter M12g tests + export proof | Backend-neutral keyboard routing exists for presenter navigation and deferred selected-card actions.
| Focus model | Runtime/Core | Planned | None | Required for interactive controls.
| Hover/pressed state | Runtime/Core | Planned | None | Add pointer state lifecycle.
| Text editing | Runtime/Standard | Planned | None | Required for real input components.
| Routing/navigation | Runtime/Presenter | Partial | Presenter shell tests + export proof | M10a adds sample-local sidebar + local tabs + page selection state; M10b adds sample-local hit testing and explicit input-to-action routing; M10c makes the organized shell the default presenter surface; M12h adds top-level wide/compact shell document selection without introducing a generic responsive solver. Generic routing remains deferred.
| Dominatus runtime scopes | Integrations/Machina.Dominatus | Partial | Integration smoke tests | Reserved only for coarse event-spanning scopes; no lifecycle API is implemented.
| Modal stack | Runtime/Dominatus | Planned | None | Coupled with focus/layer routing.
| Async effects | Dominatus | Planned | None | Needed for richer app behaviors.
| Render command bridge | Retired | Historical | M3d migration record | Replaced by Machina presentation and Aurelian backend ownership.
| Snapshot backend | Retired | Historical | M3d migration record | No active Dominatus renderer path remains.
| Raster FillRect | Machina.Renderer.Raster | Implemented | Raster tests | Primitive rectangle fill available.
| Raster DrawText debug | Machina.Renderer.Raster.Text | Implemented | Text raster tests | Debug seam exists.
| Rectangular clipping | Raster stack | Partial | Renderer tests/docs | Expand clipping edge cases.
| PPM artifact/golden harness | Raster.Dominatus tests | Implemented | Golden artifact tests | Regression path in place.
| Borders | Renderer/Core styles | Planned | None | Roadmap M1f candidate.
| Rounded rects | Renderer/Core styles | Planned | None | Roadmap M1f candidate.
| Images | Renderer/Core | Deferred | None | Later asset pipeline milestone.
| Real text backend | Raster.Text adapter | Planned | Debug text tests only | Roadmap M1g candidate.
| PNG output | Renderer tooling | Partial | Gallery export tests + local script | M7b adds deterministic PNG export for the component gallery sample without introducing pixel-diff enforcement; M7e keeps the default local audit path on `artifacts/m7e/`. |
| Font diagnostics toolkit | Machina.Fonts.Tooling | Implemented | Toolkit tests + local export script | M9a establishes the toolkit boundary and CAD overlays; M9b adds configurable layers and named presets; M9c adds clean export hygiene, source-availability contracts, and deterministic manifests; M9d formalizes direct-outline as the default static proof backend while keeping MSDF explicit and experimental. |
| Direct-outline gallery proof | ComponentGallery sample + Machina.Fonts proof bridge | Implemented | Gallery tests + local export script + deterministic PNG crops | M9e adds an opt-in `DirectOutlineStatic` proof section and backend comparison panel to the gallery without changing the production text default. |
| Direct-outline text layout contract | Machina.Fonts reference rendering + ComponentGallery sample | Implemented | Fonts tests + gallery tests + local export script | M9g adds a deterministic proof-only text-in-rect contract for `DirectOutlineStatic`, including padding, content rects, line metrics, alignment, clipping, explicit newline layout, and gallery exports without changing production UI text behavior. |
| Direct-outline render bridge contract | Machina.Fonts reference rendering + ComponentGallery sample | Implemented | Fonts tests + gallery tests + tooling boundary tests + local export script | M9h adds a renderer-facing static text request/result contract, a direct-outline bridge that maps UI-ish text intent into the M9g layout API, and an opt-in gallery proof without changing production UI text behavior or adding `Machina.Fonts.Tooling` as a production dependency. |
| Gallery-driven visual defect sweep | Machina.Standard + ComponentGallery sample | Implemented | Local PNG inspection + docs | M7c formalizes gallery-based visual triage and explicit deferral documentation; small safe fixes remain opportunistic. |
| Gallery baseline and limitation register | ComponentGallery sample + docs | Implemented | Local PNG inspection + docs + export-contract tests | M7e marks the current gallery baseline stable enough for routine audits and records intentional renderer/sample limitations. |
| Dirty rects | Renderer/runtime | Deferred | None | Performance milestone later.
| GPU backend | Future renderer | Deferred | None | Future platform strategy.
| Avalonia static bitmap window | Presenter sample | Implemented | Presenter docs/proofs | M0 presenter baseline.
| Avalonia click-to-action | Presenter sample | Partial | Presenter/runtime docs | Expand robust input loops.
| Avalonia redraw loop | Presenter sample | Partial | Presenter docs + M10b/M10c/M10d shell tests | Shell mode composes sidebar/tabs/page content, keeps the original settings page reachable under `Legacy`, routes sample-local pointer/wheel input through an Avalonia adapter, and now supports sample-local scrollbar thumb dragging through the same seam.
| Component gallery visual workbench | ComponentGallery sample | Implemented | Dedicated gallery tests + local Windows visual audit + repeatable export script | Canonical “wall of widgets” page; local-first, not a Storybook clone; M7b formalizes deterministic PNG export and generated-artifact policy. |
| Scaling/DPI conversion | Presenter sample | Partial | Presenter M1d mapper/tests | Explicit image-to-root mapping landed for None/Fill/Uniform math; broader DPI policy still pending.
| Window resize handling | Presenter sample | Partial | Presenter M12h tests + export proof + M15a audit | M12h adds deterministic width-driven wide/compact shell selection and export/runtime width options, but live runtime resize is still disabled and layout is still startup-width driven; M15b should make resize and recomposition real without introducing a generic responsive solver.

## M15a update

M15a is audit-only and returns main-lane focus to Machina and Oblivion usability after the Aurelian closeout.

- runtime presenter speed remains a documented strength
- the current workbench is not yet valid for real work because readable primary content is still missing
- runtime resize is explicitly disabled and shell/page layout is still resolved from startup width and height only
- compact card previews still mix clipped single-line text, partial wrapping, and dark-on-dark Markdown summary rendering
- inspector readability is better than preview readability, but still depends on fixed section heights and does not solve the preview problem
- `docs/Machina.UI/history/machina-oblivion-usability-reentry-audit-m15a.md` and `docs/Oblivion/oblivion-card-readability-audit-m15a.md` are now the canonical audit notes
- `artifacts/m15a/machina-oblivion-usability-audit-manifest.json|txt` record that M15a performs no usability implementation fixes, editor work, execution work, Aurelian work, or VD-MIR work

Recommended next milestone:

```text
M15b:
  Presenter resizing and readable card previews
```

## M15b update

M15b is the controlled usability implementation pass that follows the M15a audit.

- the runtime presenter window is now resizable
- the runtime presenter surface now stays on a centered `16:9` surface with neutral letterboxing outside that surface
- the runtime presenter now enforces a minimum usable surface of `960x540` and defaults to `1280x720`
- runtime sizing is separated from export frame sizing
- layout and shell mode now recompute from the live effective presenter surface
- compact Oblivion card previews now wrap or intentionally elide inside bounded body regions
- known dark-on-dark Markdown preview states are fixed with explicit preview foregrounds

This remains a bounded pass:

- no arbitrary freeform `2D` layout solver
- no editor or execution work
- no Aurelian work
- no `VD-MIR` work

## M15c update

M15c is the reading-flow follow-through after M15b.

- the Oblivion card stack is now the primary reading surface for Markdown cards
- expansion state is explicit, page-local, and separate from selected-card state
- collapsed cards stay compact and scannable
- expanded cards render Markdown body content inline in the stack
- long expanded card bodies now use deterministic local scroll regions
- the inspector remains metadata/actions/diagnostics/artifacts rather than the primary body surface

This remains a bounded pass:

- no Markdown editing
- no notebook execution
- no Roslyn execution
- no Aurelian work
- no `VD-MIR` work
- no arbitrary freeform `2D` layout solver

## M15d update

M15d is the expanded Markdown reading-surface hardening pass after M15c.

- expanded Markdown cards now use an explicit immutable reading-style record
- expanded Markdown contrast is now deliberately readable on a dark document surface
- expanded Markdown cards now use document-scale height instead of a short preview panel
- local body scrolling remains in place for long documents
- the inspector no longer renders formatted Markdown body content
- the inspector now shows raw Markdown source text in a bounded scrollable source block
- one expanded Markdown card per page keeps the reading model deterministic

## M15e update

M15e is scroll-pane and document-viewport hardening only.

- the main card stack and inspector are now independent scroll panes in wide Oblivion mode
- inspector scroll, raw-source scroll, and expanded-body drag are now explicit local state paths
- wheel routing now prefers the deepest scrollable region under the pointer
- expanded Markdown and raw-source viewports now clip content intentionally
- partially visible Markdown blocks now render instead of disappearing wholesale
- no Markdown editing, execution, Aurelian work, or `VD-MIR` work was added

This remains a bounded pass:

- no Markdown editing
- no notebook execution
- no Roslyn execution
- no Aurelian work
- no `VD-MIR` work
- no CSS-like style cascade
- no arbitrary freeform `2D` layout solver

## M15f update

M15f is regression stabilization only.

- the wide main card stack no longer routes through the generic page-scroll clamp path
- wheel and thumb drag now update the main stack offset again through a dedicated main-stack action
- inspector scroll lag was traced, not guessed
- the narrow safe lag fix caches prepared raw-source layout across repeated inspector scroll ticks
- independent panes, deepest-region routing, and partial viewport culling remain in place
- no Markdown editing, execution, Aurelian work, or `VD-MIR` work was added

This remains a bounded pass:

- no new UX features
- no Markdown editing
- no notebook execution
- no Roslyn execution
- no Aurelian work
- no `VD-MIR` work
- no broad scroll-architecture rewrite

## M15g update

M15g closes out the M15 reading-surface arc as documentation, backlog, and planning only.

- the current Machina/Oblivion reading loop is now documented as the golden path baseline
- M15a through M15f are summarized as one coherent reading-surface arc
- the current presenter sizing, card preview, expandable-card, Markdown reading, inspector, scroll/input, and document viewport models are explicitly recorded
- the known limitation that inspector scroll is not composition-only yet is carried forward without a speculative fix
- the remaining UX papercuts are organized into a backlog
- the recommended next primary direction is `M16a — Oblivion reading navigation and focus affordances`

M15g is closeout/planning only:

- no runtime behavior changed
- no new feature work was performed
- no Markdown editing
- no notebook execution
- no Roslyn/xUnit execution
- no Aurelian work
- no `VD-MIR` work

## M16a update

M16a adds internal deterministic presenter playback as sample-local tooling under `samples/Machina.UI/Machina.Presenter.Sample`.

- playback scenarios now use TOML via `*.machina-playback.toml`
- scenarios are artifacts, not disposable scripts
- playback routes through the presenter's internal input model rather than native OS automation
- starter scenarios can write normalized scenario TOML, trace JSON, manifest JSON/TXT, and final PNG output under `artifacts/m16a/playback/<scenario-id>/`
- assertions now require non-empty human-readable reasons and parser tests enforce that policy
- current semantic targets cover the M15 Oblivion reading-surface regions: main stack, card header, expanded body, inspector pane, raw source, and current scrollbar thumbs

Current status is meaningful progression rather than full closure because the playback seam still has two known parity gaps:

- main-stack wheel playback does not yet match the older direct M15f interaction seam
- raw-source wheel playback does not yet match the older direct M15f interaction seam

## M16b update

M16b is playback parity stabilization only.

- `main-stack` wheel playback now preserves the dedicated M15f action result through the real render path
- `raw-source` wheel playback now resolves and routes through the real visible shell path instead of falling back to inspector-pane scrolling
- starter scenarios now pass under `artifacts/m16b/playback`
- trace now records deterministic target-resolution, hit-test, dispatched-action, and scroll-delta evidence
- TOML remains linear data only and now rejects programming-like fields such as `if`, `loop`, `script`, and `eval`

Current M16b status is full closure for the original parity blockers:

- `oblivion-main-stack-scroll` passes through the internal presenter input/routing path
- `oblivion-raw-source-scroll` passes through the internal presenter input/routing path
- no direct state mutation is used for interaction steps

M16b still does not add native OS automation, pixel-golden diffing, Markdown editing, notebook execution, Roslyn execution, Aurelian work, or `VD-MIR` work.

## M16c update

M16c is playback regression-suite tooling only.

- playback scenarios are now organized as starter cassettes plus canonical regression cassettes
- the sample runner can now execute a scenario directory or a suite manifest
- deterministic per-scenario outputs still include normalized TOML, trace JSON, manifest JSON/TXT, and final PNG
- deterministic suite outputs now include aggregate JSON/TXT reports plus an M16c milestone manifest
- assertion reasons remain mandatory
- TOML remains linear data and still does not add loops, conditionals, variables, or scripting features

M16c still does not add native OS automation, pixel-golden diffing, Markdown editing, notebook execution, Roslyn execution, Aurelian work, or `VD-MIR` work.

This remains bounded:

- no native OS automation
- no pixel-golden diffing
- no Markdown editing
- no notebook execution
- no Roslyn execution
- no Aurelian work
- no `VD-MIR` work

## M16d update

M16d is xUnit playback integration only.

- starter playback scenarios now run as normal generated xUnit theory cases
- regression playback scenarios now run as normal generated xUnit theory cases
- canonical scenario discovery now lives in C# and can use the existing M16c suite manifest for deterministic ordering
- xUnit owns loops, scenario selection, aggregate runs, failure formatting, and any future environment guards
- TOML remains a cassette and still does not add loops, conditionals, variables, or scripting features
- deterministic xUnit playback artifacts now write under `artifacts/m16d/xunit-playback/<suite>/<scenario-id>/`
- failed xUnit playback runs now write `failure.txt` alongside normalized TOML, trace JSON, manifest JSON/TXT, and final PNG
- the M16c suite runner remains available for non-xUnit suite/report workflows

M16d still does not add native OS automation, pixel-golden diffing, Markdown editing, notebook execution, Roslyn execution, Aurelian work, or `VD-MIR` work.

## M17a update

M17a is JS parity layout/refactor recon only.

- the external Machina JS audit is now mapped onto current C# repo facts
- the current C# gap is identified as authoring-surface ergonomics more than missing low-level layout math
- existing low-level `StackArrange` / `FillFrame` / `GridArrange` / `CellFrame` support remains in place, but current Oblivion authoring still relies heavily on `UI.Anchor(...)`, `Row.Anchor(...)`, manual cursor math, and manual pane math
- `OblivionCardRenderer`, `OblivionWorkbenchCatalog`, `PresenterCard`, and the presenter shell/document factory are now documented as the highest-value readability and refactor targets
- StackArrange + FillFrame authoring parity is recorded as the first recommended implementation slice
- GridArrange + CellFrame authoring parity is recorded as the second implementation slice
- M17d has now landed that second slice as `UI.Grid(...)` authoring over the existing low-level grid engine, while the page-shell migration remains explicitly deferred to M17e
- M17e has now landed the follow-through: the wide Oblivion page shell is grid-authored as left fill cards, right fixed inspector, and page gap while preserving independent panes, compact behavior, and playback coverage
- row variants, proportional lengths, guide frames, and DeusMachine parity remain staged follow-up work rather than one-shot port scope
- playback xUnit coverage from M16d remains the intended safety net for future authoring refactors

M17a does not change runtime behavior:

- no new layout primitive implementation
- no Oblivion renderer refactor
- no page-shell refactor
- no playback scenario behavior change
- no Markdown editing
- no notebook execution
- no Aurelian work
- no `VD-MIR` work

## M17c update

M17c is the focused Oblivion card-renderer follow-through after M17b.

- `OblivionCardRenderer` internal compact-card composition now uses stack-authored sections for header and body/footer layout
- visible `cursorTop` authoring is sharply reduced in the main card path
- explicit `".slot"` wrapper authoring is sharply reduced in renderer-internal paths
- collapsed preview cards now use one explicit body/footer stack, removing overlap risk
- compact footer measurement now uses the same final badge-row model that rendering uses
- page shell layout is intentionally unchanged
- `UI.Grid(...)` is still not implemented

M17c keeps scope narrow:

- no page-shell refactor
- no Markdown editing
- no notebook execution
- no Aurelian work
- no `VD-MIR` work

## M17f update

M17f is doc-only closeout for the M17 layout authoring parity arc.

- the M17 stack/grid authoring parity arc is now closed
- authoring-level `UI.Stack(...)` and `UI.Grid(...)` are now the current C# baseline over the existing low-level layout engine
- `OblivionCardRenderer` stack authoring and the wide Oblivion page-shell grid authoring are now recorded as the canonical migrated paths
- the original external audit complaint has materially changed from missing ergonomic stack/grid authoring to deferred parity concepts and cleanup pressure
- playback xUnit remains the safety net for future layout work
- remaining gaps are now explicitly classified as concrete-now versus later pressure
- the default recommended next step is `Option E: Layout cleanup and bugfix pass`
- the strongest immediate primitive-parity alternative remains `Option A: UiLength proportional/clamp authoring`

M17f does not change runtime behavior:

- no new layout primitive is implemented
- no changes are made to `UI.Stack(...)` or `UI.Grid(...)`
- no further `OblivionCardRenderer` or page-layout refactor is performed
- no editor work, notebook execution, Aurelian work, or `VD-MIR` work is performed

## M18a update

M18a is the focused cleanup follow-through from the M17f recommendation.

- the known Oblivion inspector title clipping risk is fixed
- high-value duplicated ROALoop-style helpers in `tests/Machina.UI/Machina.Presenter.Sample.Tests` are consolidated into shared setup, manifest, and region helpers
- no tests are deleted and no coverage is intentionally removed
- no product feature or new layout primitive is added
- future Machina presenter tests should prefer shared setup helpers/builders where they reduce duplication, while keeping behavior assertions readable and explicit

| MonoGame presenter | Future presenter | Deferred | None | Future backend.
| Web presenter | Future presenter | Deferred | None | Future platform milestone.

## Layer Roadmaps

- **Layout**: preserve deterministic math core; add targeted behavior only when contract-proven.
- **Core**: maintain declarative authoring surface and stable lowering artifacts.
- **Standard**: grow from tested essentials (Button/Form shell) toward composable control suite.
- **Runtime**: evolve from hit-test + deterministic transitions into focus, keyboard, and modal coordination.
- **Renderer**: keep snapshot confidence while adding style/text fidelity.
- **Presenter**: harden one reference shell before multiplying platforms.
- **Gallery workbench**: keep one explicit local visual page for StandardUI smoke checks without introducing web tooling.

## Runtime Control Model

Tiering rule:

- **Imperative local state**  
  tiny one-off code where introducing infrastructure would add noise.

- **DispatchTable**  
  simple deterministic field transitions (`Set`, `Toggle`, `Increment`, namespaced actions).

- **Dominatus integration:** coarse Push/Pop behavioral scopes, async effects, orchestration, side effects, and replay-oriented flows when an explicitly integration-owned host is approved.

React mapping guide:

- coarse screen/dialog/workflow scope -> optional Dominatus push/pop; not per-widget mount/unmount.
- patch -> imperative update or DispatchTable transition.
- effect -> Dominatus actuation/effect orchestration.

## Rendering Backend Matrix

| Backend | Status | Notes |
|---|---|---|
| Snapshot backend | Implemented | Deterministic test artifacts.
| Raster backend | Implemented | CPU pixel primitives active.
| Raster text debug backend | Implemented | Debug text seam available.
| Avalonia presenter | Implemented/Partial | Runs vertical slice; needs interaction hardening.
| Future SixLabors/Skia/MonoGame/Vulkan/etc. | Deferred | Out of immediate milestone scope.

## Standard Component Matrix

| Component Group | Status | Notes |
|---|---|---|
| Core baseline (`Button`, primitive text/layout wrappers) | Implemented | Current tested center.
| Form shell (`Field`, input wrapper pieces) | Partial | Needs full text edit/focus runtime.
| Selection controls (`Checkbox`, `Switch`) | Implemented | Used in presenter settings sample with plain C# dispatch over typed action IDs (M5b).
| Overlay/navigation/data controls | Deferred | Needs focus/modal/routing foundations first.

## Presenter / Platform Matrix

| Platform Path | Status | Notes |
|---|---|---|
| Avalonia bitmap sample | Implemented | M0 vertical proof path exists.
| Avalonia interactive loop | Partial | Needs DPI, resize, and richer input behavior.
| MonoGame | Deferred | Future.
| Web presenter | Deferred | Future.

## Near-Term Roadmap

- **M1a**: pipeline extraction / reusable `UiNode`-to-frame helper.
- **M1b**: presenter sample cleanup using pipeline helper.
- **M1c**: settings/counter presenter sample baseline (implemented).
- **M5b**: presenter sample dispatch simplified to plain C#; DispatchTable retained as advanced option (implemented).
- **M1d**: image-to-root coordinate mapping for presenter clicks (implemented).
- **M1e**: Standard component + presenter visual tuning pass (flat deterministic polish, no new renderer primitives).
- **M1f**: scaling/DPI pointer conversion follow-through and resize behavior hardening.
- **M1g**: border/radius style support.
- **M7a**: dedicated Machina component gallery sample and headless+local visual proof path (implemented).
- **M7b**: gallery export contract, local export script, stable PNG artifact names, and artifact policy cleanup (implemented).
- **M7c**: gallery visual defect sweep, shared defect triage, and visual audit documentation (implemented).
- **M7d**: Badge intrinsic sizing and local text placement contract, with gallery regression coverage and artifact revalidation (implemented).
- **M7e**: gallery stabilization ledger, known-limitation register, current audit workflow cleanup, and small export-contract hardening (implemented).
- **M9a**: `Machina.Fonts.Tooling` consolidation, CAD-style diagnostic grid, unified artifact export, and production-vs-tooling boundary documentation (implemented).
- **M9b**: configurable diagnostic layers, named compositions/presets, preset-driven export workflow, and LLM/human inspection ergonomics (implemented).
- **M9c**: export hygiene, clean-mode guardrails, source-availability contract, strict-vs-partial preset policy, and deterministic export manifests (implemented).
- **M9d**: direct-outline static text backend formalized as the default diagnostic/static proof path, stable direct-outline renderer API, and explicit MSDF scalable/experimental labeling (implemented).
- **M9e**: opt-in direct-outline static text proof integration in the component gallery, deterministic gallery comparison artifacts, and proof-only reuse of the direct-outline renderer (implemented).
- **M9f**: MSDF alignment repair against the direct-outline oracle, including scalable field-resolution sizing for the experimental path, texel-center UV sampling, before/after reports, and no production UI text default change (implemented).
- **M9g**: direct-outline static text box/layout contract, proof-only text-in-rect placement API, gallery text-layout artifacts, and no production UI text default change (implemented).
- **M9h**: direct-outline static render bridge contract, renderer-facing request/result API, opt-in gallery bridge proof, and dependency-direction guardrails with no production UI text default change (implemented).
- **M10a**: presenter navigation shell with sidebar, local tabs, scrollable pages, deterministic scrollbar visuals, and opt-in export/runtime integration (implemented).
- **M10b**: presenter navigation interaction wiring with Avalonia as the current sample input backend, backend-neutral shell hit testing, explicit action routing, and interaction export proof states (implemented).
- **M10c**: presenter page organization cleanup, shell-as-default behavior, legacy single-card preservation page, and canonical sample-surface docs/artifacts (implemented).
- **M12g**: presenter keyboard input seam, backend-neutral key events, and deferred selected-card keyboard routing (implemented).
- **M12h**: presenter adaptive shell modes with one top-level breakpoint, wide/compact shell documents, compact sidebar rail, and compact Oblivion card-list/inspector swap without continuous scaling (implemented).

## Open Questions

- What minimum focus model unblocks keyboard + text input without over-scoping runtime?
- Which Standard components are truly required for “usable app UI” in next milestone?
- Should window-resize and DPI concerns be solved in presenter helper layer or platform-specific adapters?
- Where should async side effects first land: DispatchTable extensions vs Dominatus-only patterns?

## References

- `docs/Machina.UI/reference/layout-port-contract.md`
- `docs/Machina.UI/architecture/ui-core-contract.md`
- `docs/Machina.UI/reference/standard-components-contract.md`
- `docs/Machina.UI/architecture/machina-runtime-hit-testing.md`
- `docs/Machina.UI/architecture/machina-runtime-dispatch.md`
- `docs/Machina.UI/architecture/machina-dominatus-rendering.md`
- `docs/Machina.UI/reference/raster-renderer-contract.md`
- `docs/Machina.UI/reference/raster-dominatus-renderer.md`
- `docs/Machina.UI/reference/raster-text-renderer-contract.md`
- `docs/Machina.UI/reference/render-artifact-policy.md`
- `docs/Machina.UI/history/machina-presenter-m0a.md`
- `docs/Machina.UI/history/machina-presenter-m0b.md`
- `docs/Machina.UI/history/machina-presenter-m0c.md`
- `docs/Machina.UI/history/machina-presenter-m0d.md`
- `docs/Machina.UI/history/machina-presenter-m1c.md`
- `docs/Machina.UI/history/machina-presenter-m1d.md`
- `docs/dominatus-authoring-footguns.md`

## M1b support matrix update

Rectangular border/stroke support is implemented for the current M1b scope.

Deferred visual features remain deferred: border radius, per-side border styling, dashed/dotted styles, and shadow effects.

## M2a update

- Added placement-first authoring primitives in Machina.Core: `UI.Surface`, `UI.Layer`, `UI.At`, `UI.Anchor`.
- Presenter sample moved from spacer-based panel placement to explicit placement-frame composition.
\n\n## M3a flat authoring note\nRow-first UiDocument/UiRow authoring is canonical for top-level screens; nested UiNode trees remain optional sugar.

## M3b status update

M3b hardens row-first authoring with deterministic `UiDocument` snapshots, flat-path validation tests, broader `StandardView` metadata helpers, and canonical row-first documentation.
\n\n### M3d text alignment\nTextStyle now includes horizontal (TextAlignX) and vertical (TextAlignY) alignment metadata. Defaults remain Left/Top for backward compatibility. Alignment only changes glyph paint origin inside the resolved text rectangle; layout geometry is unchanged. M3d does not add wrapping, ellipsis, multiline layout, baseline typography, kerning, anti-aliasing, or external font dependencies.

## Recent milestone note (M3e)

Presenter sample form controls are now re-proven with flat row-first composition, including checkbox box and switch track/thumb sub-rows, with pipeline hit-testing coverage for composed control parts.


## M3f status update

M3f hardens standard control chrome for composed checkbox/switch controls using only existing rectangular primitives, while preserving flat row-first authoring and pipeline hit-test structure.
\n## M4a hybrid note\nRow-hosted components are now supported: top-level placement stays flat rows, while local component internals use nested UiNode/StandardUI under a host row boundary.

## M4b note (2026-05-26)
Reference audit aligns this document with imported MachinaLayout.JS frame/stack semantics in \.
\n## M4c layout-padding hardening note\n\nM4c clarifies that style padding is paint metadata only. Components that host child layout (for example Card, Input text content) must create an explicit inset content region with placement rows (AnchorFrame), rather than relying on  to move children. Stack behavior remains ordered arithmetic () and is not Flexbox.\n

## M4c layout-padding hardening note

M4c clarifies that style padding is paint metadata only. Components that host child layout (for example Card, Input text content) must create an explicit inset content region with placement rows (`AnchorFrame`), rather than relying on `UiStyle.Padding` to move children. Stack behavior remains ordered arithmetic (`StackArrange`) and is not Flexbox.

| Standard component explicit internals (Button/Checkbox/Switch) | Machina.Standard + tests | Implemented | Standard/Pipeline headless geometry tests | M4d: shell/content rows and deterministic action hit regions. |
\n- M4e note: presenter sample geometry is now validated with headless resolved-rectangle assertions; manual GUI checks are secondary.


## M4f note
M4f adds semantic-text separation and state-stable control geometry. Semantic labels are not paint; explicit text visuals emit draw text. Checkbox/switch state changes should preserve row identity/shape and adjust stable style/geometry values instead of adding/removing rows.

- M5c update: StandardTheme now carries typed component style records and supports explicit root theme handoff with `with` customization.


## M5c1 status

- Completed: Button style record fully wired into `StandardUI.Button`.
- Completed: Card style record fully wired into `StandardUI.Card`.
- Completed: Presenter root theme handoff demonstrates immutable `with` customization for Button + Card.
- Deferred intentionally: Input/Checkbox/Switch full style wiring (outside M5c1 scope).


## M5c3 Checkbox and Switch style wiring

M5c3 fully wires `StandardCheckboxStyle` and `StandardSwitchStyle` into `StandardUI.Checkbox` and `StandardUI.Switch`. Checkbox and switch geometry, visual style, gap spacing, and label text style now resolve deterministically from the selected style record (`style:` if supplied, otherwise theme default). Checked/on state changes values (for example mark fill and thumb X) without changing row identity.

- **M5c4**: style model consolidation/docs/test cleanup pass; canonical StandardUI vs StandardView guidance and presenter sample positioning (implemented).

## M5d contract cleanup note
Historical row-only checkbox/switch decomposition guidance is superseded for canonical app authoring. Prefer `StandardUI` controls/components in app documents; keep `StandardView` for leaf metadata and advanced custom sub-part composition. See `docs/Machina.UI/history/standard-ui-vs-standard-view-m5d.md`.



## M5e headless harness
M5e standardizes component/document headless assertions through tests/Machina.UI/Machina.Testing/GeometryHarness.cs so component tests can assert resolved rectangles, row presence, metadata, and hit targets without repeating lowering/resolve plumbing.

- M5f update: presenter sample is the canonical reference app and is contract-tested in tests/Machina.UI/Machina.Presenter.Sample.Tests (document shape, hosted component boundary, localized StandardUI internals, plain C# dispatch, theme propagation, and geometry/hit-target stability).


## M5g presenter visual regression audit
M5g requires converting presenter screenshot regressions into headless geometry/render-command tests. Verify UI.Text visibility by DrawTextCommand presence + visible text color + in-card rect; verify default button text-style size fits default shell; verify checkbox checked mark via explicit mark geometry and visible fill when checked (transparent when unchecked). Dynamic text fitting remains deferred.

## M6a Machina.Text boundary note

M6a establishes `Machina.Text` as a separate subsystem contract. Frame/stack/table layout still places component rectangles; `Machina.Text` will lay out text only inside those assigned boxes.

Wrap, overflow, leading, block/list spacing, and text alignment are text-domain primitives and must not be added to general layout semantics.

Headings remain a component/layout responsibility (for example title variant selection in `StandardUI.Card`), not a supported inline markup mechanism inside restricted Machina text source.

The current simple `UI.Text` path is transitional until `Machina.Text` parser/model/layout integration milestones (M6b+) are complete.

## Machina.Standard.Text M6b status

M6b lands the rich text model and restricted parser under `Machina.Standard.Text`. This replaces the earlier near-term plan to create a standalone production `Machina.Text` project. The standalone package is deferred until Standard typography policy and integration needs are better proven.

Current status:

- Model, policy enums, diagnostics, parser, and helper constructors exist in Standard.
- Core `UI.Text(string)` remains unchanged.
- StandardUI controls and raster text rendering are not migrated.
- Renderer/layout integration is deferred to M6c+.

Next work remains measurement, text layout inside assigned boxes, renderer bridging, and StandardUI adoption.

## Machina.Standard.Text M6c status

M6c now lands deterministic text measurement/layout inside assigned text boxes.

Current status:

- `MachinaTextLayoutResult` exposes renderer-independent box/line/run geometry.
- `MachinaTextLayoutEngine` consumes `MachinaTextSpec` or `MachinaTextDocument` plus assigned box and measurer.
- Paragraphs, bullet lists, wrap `none|word`, overflow reporting, align/valign, leading, block gap, and list gap are implemented.
- Parse diagnostics are preserved on layout results.
- Renderer integration is still deferred.
- `UI.Text` and StandardUI control migration are still deferred.

Next work is M6d: consume layout results from a renderer bridge without changing general layout ownership of text boxes.

## Machina.Standard.Text M6d status

M6d lands the bridge proof and measurement audit.

Current status:

- `MachinaTextLayoutResult` can now be converted into deterministic `DrawTextCommand` output through `MachinaTextRenderBridge`.
- Standard/Core/raster deterministic measurement paths now share one bitmap measurement seam.
- Representative strings are audited in tests for title/body/label/mono-style cases, spaces, and punctuation.
- Primitive `UI.Text` remains unchanged.
- StandardUI controls are not broadly migrated.
- Rich inline style metadata is preserved in layout, but renderer styling remains limited to current command/style support.

Next work is M6e: adopt this bridge in one controlled authored rich text surface before any broader StandardUI migration.

## Machina.Standard.Text M6e status

M6e lands that controlled authored surface.

Current status:

- `StandardUI.TextBlock(...)` exists as the first Standard-owned rich text component.
- It accepts `MachinaTextSpec` from `Machina.Standard.Text.Text.*` helpers.
- Dominatus lays it out in assigned bounds and emits existing `DrawTextCommand` output.
- Presenter sample now includes one rich text probe with local Windows visual validation.
- Primitive `UI.Text` remains unchanged.
- Existing Standard controls are not broadly migrated.

Deferred:

- rich inline style fidelity in the current raster renderer
- ellipsis/scroll/clip fidelity improvements
- broad Standard control migration

## M5g status

- Completed: presenter sample ergonomics cleanup into `SettingsScreen`, `SettingsCard`, `SettingsActions`, `DemoState`, and `DemoStateDispatch`.
- Completed: canonical sample uses `StandardUI.Card` multi-children/gap authoring instead of manually wrapping `UI.Column` inside Card.
- Completed: sample demonstrates explicit theme handoff from screen to component to child `StandardUI` controls with no hidden theme cascade.
- Completed: dedicated presenter sample tests cover canonical document shape, localized component ids, action ownership, theme propagation, geometry, hit targets, and render-command regressions.

## M5i validation note

M5i fixed the unrelated Windows blockers in `Copeland.Script.Tests` and `Copeland.Cli.Tests`, so `dotnet test Machina.UI.slnx` is green again on Windows. See `docs/Copeland/history/copeland-windows-test-triage-m5i.md`.


## M8a font atlas architecture

M8a documents the future `Machina.Fonts` direction: async runtime MSDF atlas generation, immutable snapshot consumption, TOML/PNG atlas export/import, and headless gallery-export preflight. Implementation is staged for M8b+; M8a intentionally does not replace the current bitmap renderer or add Vulkan/native font dependencies. See `docs/Machina.UI/history/machina-font-atlas-architecture-m8a.md`.

## M8b Machina.Fonts fake architecture

M8b adds the standalone `Machina.Fonts` project and focused tests for validated font atlas records, immutable snapshots, async channel-based fake generation, deterministic fake packing, pending/ready/missing resolution, and export-style preflight waiting. It is intentionally architecture-only: no MSDF, no real font parsing, no TOML or PNG output, no renderer integration, no native dependency, and no active dependency on the Dominatus reference submodule. See `docs/Machina.UI/history/machina-fonts-m8b.md`.

### Machina M8c — Font atlas TOML metadata

M8c lands `.font-atlas.toml` document records, a deterministic writer, loader/parser, validation diagnostics, and snapshot conversion helpers inside standalone `Machina.Fonts`. It is metadata-only and defers real MSDF generation, font loading, PNG writing, and renderer integration to later milestones.

### Machina M8d — fake atlas artifacts

M8d lands the fake atlas artifact pipeline: ready fake-worker snapshots export to deterministic `.font-atlas.toml` plus `.fakepage` page files, import validates existence, content hashes, and fake page dimensions, and tests prove roundtrip equivalence. Real MSDF, PNG, font parsing, and renderer integration remain deferred. See `docs/Machina.UI/history/machina-font-atlas-artifacts-m8d.md`.

### Machina M8e — MSDF and outline dependency audit

M8e is a research/design milestone only. It audits current public sources for `MSDF-Sharp.Core`, `LayoutFarm/Typography`, `SixLabors.Fonts`, and `SharpFont`, then recommends a strict Machina-owned adapter boundary. Current recommendation:

- prefer `Typography.OpenFont` for outline extraction,
- prefer `MSDF-Sharp.Core` for MSDF generation,
- avoid `MSDF-Sharp.Extensions` in the first real path because it pulls in `SixLabors.ImageSharp`,
- avoid `SixLabors.Fonts` for now because of the split-license policy,
- keep native FreeType as fallback only.

No package references or implementation behavior change land in M8e. See `docs/Machina.UI/history/machina-font-msdf-dependency-audit-m8e.md`.

### Machina M8f — generation adapter seam

M8f lands that strict Machina-owned adapter boundary in code without adopting any real dependency yet.

- Machina-owned outline records and generation diagnostics are implemented in `Machina.Fonts.Generation`.
- `IGlyphOutlineSource` and `IGlyphDistanceFieldGenerator` now exist as compile-checked seams.
- `FakeGlyphOutlineSource`, `FakeGlyphDistanceFieldGenerator`, and `GlyphGenerationPipeline` prove deterministic output, cancellation behavior, missing-outline short-circuiting, and diagnostic aggregation.
- No `Typography.OpenFont`, `MSDF-Sharp.Core`, `SixLabors`, `FreeType`, renderer integration, or native dependency is added.

See `docs/Machina.UI/history/machina-font-generation-adapters-m8f.md`.

### Machina M8g — Typography outline extraction proof

M8g lands the first real managed outline adapter inside standalone `Machina.Fonts`.

- `Machina.Fonts` now consumes `WycliffeAssociates.Typography.OpenFont` `1.0.0`.
- `TypographyGlyphOutlineSource` implements `IGlyphOutlineSource`.
- one checked-in OFL fixture font is loaded from an explicit file path only.
- real glyph metrics and contours now translate into Machina-owned outline records.
- whitespace, missing glyphs, cancellation, determinism, and pipeline integration are covered by focused tests.

Still deferred:

- no `MSDF-Sharp.Core`
- no real distance-field generation
- no atlas integration
- no artifact export integration
- no renderer/TextBlock/gallery integration
- no OS font lookup
- no shaping/bidi/ligature work
- no native dependency

See `docs/Machina.UI/history/machina-typography-outline-adapter-m8g.md`.

### Machina M8h — MSDF-Sharp distance-field generation proof

M8h lands the first real managed distance-field generator inside standalone `Machina.Fonts`.

- `Machina.Fonts` now consumes `MSDF-Sharp.Core` `1.0.2`.
- `MsdfSharpDistanceFieldGenerator` implements `IGlyphDistanceFieldGenerator`.
- Machina-owned outlines convert into `Msdfgen.Shape`, `Contour`, and edge segments.
- real `SDF`, `PSDF`, `MSDF`, and `MTSDF` output is covered by focused tests.
- Typography fixture outlines now generate real distance fields through the existing pipeline.

Still deferred:

- no `MSDF-Sharp.Extensions`
- no SixLabors
- no FreeType/native dependency
- no atlas integration
- no artifact export integration
- no PNG output
- no renderer/TextBlock/gallery integration

See `docs/Machina.UI/history/machina-msdf-sharp-generator-m8h.md`.

### Machina M8i — deterministic generated-field atlas packing and real page artifacts

M8i lands the first real atlas asset pipeline inside standalone `Machina.Fonts`.

- `GeneratedFieldAtlasPacker` now turns real `GeneratedGlyphDistanceField` outputs into deterministic shelf-packed pages.
- packed pages now produce real `GlyphAtlasEntry` rects, UVs, and metrics.
- `.dfpage` artifacts now store deterministic float/channel page data with SHA-256 hashes.
- import validation now checks missing files, hash mismatches, invalid headers, page index mismatches, dimension mismatches, channel mismatches, and payload-length mismatches.
- Typography + `MSDF-Sharp.Core` now roundtrip through pack/export/import successfully.
- whitespace remains metrics-only and is intentionally excluded from atlas entries.

Still deferred:

- no renderer/TextBlock/gallery integration
- no PNG output
- no Vulkan/Aurelian dependency

See `docs/Machina.UI/history/machina-distance-field-atlas-packing-m8i.md`.

### Machina M8k — CPU reference MSDF string renderer proof

M8k proves that packed `.dfpage` atlas data can be sampled back into visible multi-glyph string pixels inside standalone `Machina.Fonts`.

- `Machina.Fonts.ReferenceRendering` adds a tiny RGBA image model, distance-field page reader, CPU sampling helpers, single-line text layout, string renderer, and deterministic `.ppm` writer.
- `Sdf`/`Psdf` sampling uses the scalar channel.
- `Msdf` sampling uses median RGB.
- `Mtsdf` currently also uses median RGB and explicitly defers alpha-channel usage.
- whitespace now advances as metrics-only spacing without atlas entries.
- focused tests cover sampling, threshold behavior, glyph placement, baseline/bearing policy, `.ppm` output, and a real Typography + `MSDF-Sharp.Core` + packing + artifact-read + string-render proof.

Still deferred:

- no renderer/TextBlock/gallery integration
- no PNG output
- no Vulkan/Aurelian dependency

See `docs/Machina.UI/history/machina-cpu-msdf-text-renderer-m8k.md`.

### Machina M8l — CPU MSDF text proof audit and convention stabilization

M8l turns the M8k proof path into a repeatable local audit workflow without integrating it into UI code.

- `FontProofExporter` now exports a deterministic proof set from one shared real Typography + `MSDF-Sharp.Core` atlas.
- `tools/Export-MachinaFontProofs.ps1` writes local proof artifacts to `artifacts/m8l`.
- visual audit found and fixed two small convention issues: the real proof path needed `FlipY = true` for upright output, and the longest proof strings needed a wider deterministic canvas.
- docs now explicitly record coordinate orientation, baseline/bearing placement, centered field compensation, smoothing/threshold behavior, and whitespace/missing-glyph policy.

Still deferred:

- no `TextBlock` integration
- no component gallery integration
- no renderer replacement
- no Vulkan/Aurelian work
- no shaping/kerning/fallback policy expansion

See `docs/Machina.UI/history/machina-cpu-msdf-text-proof-audit-m8l.md`.

### Machina M8m — component gallery MSDF font proof mode

M8m brings the standalone CPU MSDF proof path into the component gallery as an opt-in export-only audit mode.

- `GalleryProgramOptions` and `tools/Export-MachinaComponentGallery.ps1` now accept an explicit proof flag.
- proof mode adds a dedicated gallery section with current bitmap text on the left and an MSDF proof slot on the right.
- the gallery sample renders proof strings through `Machina.Fonts.ReferenceRendering.DistanceFieldTextPipeline`.
- the sample blits the resulting proof image into the exported gallery PNG after normal rasterization.
- local artifacts now support `artifacts/m8m/component-gallery-msdf-proof.png`.

Still deferred:

- no `UI.Text` replacement
- no `StandardUI.TextBlock` migration
- no control-label migration
- no renderer/Vulkan/Aurelian integration
- no production UI package dependency on `Machina.Fonts`

See `docs/Machina.UI/history/machina-component-gallery-msdf-proof-m8m.md`.

### Machina M8n — CPU MSDF spacing, bearings, and kerning audit/fix

M8n hardens the standalone CPU MSDF proof path before any UI text integration.

- `DistanceFieldTextLayout` now accepts optional Machina-owned adjacent pair adjustments.
- `TypographyGlyphOutlineSource` now exposes optional pair adjustment through low-level Typography/OpenFont `GPOS` pair-position lookups.
- `SpaceMono-Regular.ttf` remains the deterministic monospaced proof fixture, while `CrimsonText-Regular.ttf` was added as a checked-in OFL proportional fixture to prove kerning pairs.
- CPU field placement now recomputes the generator's fit-to-drawable-area padding instead of assuming raw symmetric leftover field size.
- `artifacts/m8n` now holds the current local spacing/kerning proof outputs and refreshed gallery proof export.

Still deferred:

- no `TextBlock` integration
- no production renderer integration
- no shaping, ligatures, bidi, fallback, or multiline layout

See `docs/Machina.UI/history/machina-cpu-msdf-spacing-kerning-m8n.md`.

### Machina M8o — MSDF reference-oracle comparison fixture

M8o adds the local oracle needed before another proof-path spacing or placement change.

- `tools/Export-MachinaFontReferenceComparison.ps1` now exports browser-canvas reference renders, Machina MSDF proof renders, side-by-side compare PNGs, and a glyph placement report under `artifacts/m8o`.
- the reference path uses checked-in fixture fonts plus local headless Edge/Chrome canvas rendering, staying independent from Machina placement logic.
- the current evidence shows the dominant mismatch is not missing kerning data but oversized/underconstrained glyph render quads relative to the advances in use.

Still deferred:

- no production renderer integration
- no `StandardUI.TextBlock` migration
- no visual pixel-diff gate
- no new production dependency on browser tooling

See `docs/Machina.UI/history/machina-msdf-reference-oracle-m8o.md`.
## M8p update

M8p fixes the field placement contract exposed by M8o.

- generated fields now preserve explicit `GlyphFieldPlacement`
- atlas entries and `.font-atlas.toml` roundtrip that metadata
- the CPU proof renderer now draws from stored plane bounds instead of fixed tile assumptions
- regenerated `artifacts/m8p` comparisons show contiguous strings without the prior oversized field-tile overlap

This milestone remains proof-path only and still defers any `TextBlock`, Standard, or production renderer integration.

## M8q update

M8q adds vertical-metrics evidence on top of the M8o/M8p oracle path.

- `tools/Export-MachinaFontReferenceComparison.ps1` now also writes `artifacts/m8q/browser-text-metrics.json`
- the browser fixture exports `measureText(...)` baseline and bounding-box metrics with explicit `alphabetic` baseline mode
- `glyph-placement-report.txt/json` now merge browser vertical metrics with Machina plane/ink/baseline data
- current evidence shows the proof renderer is already using the correct baseline-relative plane convention and is not double-applying `BearingY`
- the remaining visible difference is a small proof-only lower-edge ink extent mismatch, not a baseline-origin bug

M8q remains proof-path only:

- no `TextBlock` integration
- no `Machina.Standard.Text` integration
- no production renderer integration
- no browser dependency at runtime
- no magic vertical offset

See `docs/Machina.UI/history/machina-msdf-vertical-metrics-m8q.md`.

## M8q.1 update

M8q.1 fixes one narrow CPU proof-renderer raster rounding issue that M8q exposed.

- the baseline bug was verified in `ComputeDrawBounds`, not in a second explicit baseline path inside `RenderGlyphInto`
- `drawY` had been rounded from `PlaneTop` independently of the baseline position implied by the rounded output tile height
- the fix now computes baseline position inside the rounded output tile first, then derives `drawY` from that one invariant
- CrimsonText regression coverage now locks the known fractional-case behavior

This remains proof-path only:

- no atlas architecture change
- no MSDF generation change
- no `TextBlock` integration
- no production renderer integration
- no magic vertical offset

See `docs/Machina.UI/history/machina-msdf-baseline-rounding-fix-m8q1.md`.

## M8q.2 update

M8q.2 adds an evidence-first baseline guide overlay on top of the M8o/M8q/M8q.1 proof tooling.

- browser oracle renders now draw a red 1 px baseline guide at the active `baselineY`
- Machina CPU MSDF proof renders now draw the same red baseline guide
- compare artifacts and the opt-in gallery MSDF proof export now show that guide explicitly
- proof reports now include baseline-guide enablement and Y metadata

This remains proof-path only:

- no glyph spacing change
- no kerning change
- no plane-bounds math change
- no MSDF generation change
- no `TextBlock` integration
- no production renderer integration
- no arbitrary vertical-offset fix

See `docs/Machina.UI/history/machina-msdf-baseline-guide-overlay-m8q2.md`.

## M8r update

M8r turns the browser-oracle proof stack into direct overlay-diff tooling instead of another speculative rendering change.

- `tools/Export-MachinaFontReferenceDiff.ps1` now captures browser reference pixels and metrics, exports browser and Machina source PNGs separately, and writes overlay, absolute-diff, threshold-diff, wireframe, and side-by-side compare artifacts under `artifacts/m8r`.
- `artifacts/m8r/diff-report.txt` and `.json` now summarize ink bounds, overlap areas, IoU, mismatch counts, and per-axis deltas for each proof string.
- wireframe artifacts overlay browser bounds, Machina bounds, the baseline guide, browser metric bounds, and Machina glyph draw rects.
- current evidence shows the remaining mismatch is mostly a lower-and-wider Machina extent problem, not a baseline-line mismatch and not obviously a pure coverage-only issue.

This remains diagnostic-only:

- no MSDF sampling or threshold change
- no baseline placement change
- no kerning or `GlyphFieldPlacement` change
- no `TextBlock` integration
- no production renderer integration
- no CI pixel gate

See `docs/Machina.UI/history/machina-msdf-reference-diff-overlay-m8r.md`.

## M8s update

M8s adds a direct Typography-outline mask oracle and turns the proof stack into a three-way browser/direct-outline/MSDF diagnostic.

- `src/Machina.UI/Machina.Fonts/ReferenceRendering` now includes deterministic outline flattening, supersampled direct-outline mask rasterization, shared `InkMask` extraction, edge extraction, pairwise shape metrics, and overlay helpers.
- `.\tools\Export-MachinaFontShapeDiff.ps1 -OutputDir artifacts\m8s` now exports browser, direct-outline, and MSDF artifacts at `32px`, `48px`, and `64px`.
- pairwise reports now cover browser-vs-direct, direct-vs-MSDF, and browser-vs-MSDF using IoU, bounds deltas, unique-area counts, above/below-baseline mismatch areas, and symmetric edge-distance summaries.
- current aggregate evidence shows browser-vs-direct staying relatively stable while direct-vs-MSDF degrades sharply at `64px`, so the next proof-only fix most likely belongs on the MSDF generation/rendering side.

This remains diagnostic-only:

- no MSDF sampling or smoothing fix is applied here
- no baseline placement change
- no kerning or `GlyphFieldPlacement` change
- no `TextBlock` or production renderer integration
- no browser dependency at runtime
- no CI pixel gate

See `docs/Machina.UI/history/machina-msdf-three-way-shape-diff-m8s.md`.

## M9a update

M9a consolidates the late-M8 proof/debug stack into an explicit toolkit boundary.

- `src/Machina.UI/Machina.Fonts.Tooling` now holds the human-facing and LLM-facing overlay/export orchestration.
- `tests/Machina.UI/Machina.Fonts.Tooling.Tests` now cover grid, bounds, and deterministic export behavior.
- `.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9a -ShowGrid -GridStep 8 -ShowBounds` is the new consolidated local workflow.
- the toolkit adds X/Y axes, configurable grid density, optional unit labels, explicit baseline display, and stable bounds/wireframe overlays.
- browser kerning remains useful context in older M8 workflows, but is no longer treated as the primary target oracle for horizontal spacing.
- direct-outline text with Machina's own kerning is the current internal geometry reference for diagnostics.

This remains tooling-only:

- no production text rendering behavior changed
- no Typography outline extraction change landed here
- no MSDF generation or atlas packing change landed here
- no `TextBlock` or `Standard.Text` integration changed

See `docs/Machina.UI/history/machina-font-toolkit-m9a.md`.

## M9b update

M9b builds on M9a by replacing hardcoded diagnostic compositions with a reusable layer model inside `Machina.Fonts.Tooling`.

- `DiagnosticLayerComposition` and `DiagnosticLayer` now model export surfaces as ordered, toggleable layers.
- image, mask, bounds, grid, axis, baseline, label, difference, and glyph-wireframe views can now be composed without changing production font behavior.
- `LayerPresets` now provide named views such as `direct-vs-msdf`, `browser-vs-direct`, `three-way`, `cad-debug`, and `msdf-debug`.
- `.\tools\Export-MachinaFontDiagnostics.ps1` now accepts `-Preset` and writes M9b preset artifacts plus `layer-composition-report.txt/json`.
- browser comparison layers remain diagnostic context only; browser horizontal kerning is still not the primary success oracle.
- direct-outline text with Machina's own kerning remains the current internal geometry reference.

This remains tooling-only:

- no production renderer change
- no MSDF generation change
- no atlas packing change
- no `TextBlock` or production UI integration change
- no new native or forbidden dependency

See `docs/Machina.UI/history/machina-font-toolkit-layers-m9b.md`.

## M9c update

M9c keeps the M9a/M9b diagnostic-only boundary and focuses on export workflow hygiene.

- `FontDiagnosticArtifactExporter` now supports clean output mode with safety guardrails for repository root, drive root, and user-profile root.
- `.\tools\Export-MachinaFontDiagnostics.ps1` now accepts `-Clean` and `-AllowPartial`.
- source availability is now recorded structurally for browser, direct-outline, MSDF, masks, placement reports, and shape-diff reports.
- required-source presets now fail clearly by default when required inputs are missing, while `-AllowPartial` records explicit degradation warnings instead of relying on ambiguous placeholders.
- each export folder now writes `font-diagnostic-export-manifest.json` and `.txt`.

This remains tooling-only:

- no production renderer change
- no MSDF sampling change
- no direct-outline rasterization change
- no atlas packing change
- no `TextBlock` or production UI integration change
- no new native or forbidden dependency

See `docs/Machina.UI/history/machina-font-toolkit-export-hygiene-m9c.md`.

## M9d update

M9d formalizes the backend naming and default proof policy inside the toolkit stack.

- `DirectOutlineStatic` is the default static/UI-text proof backend.
- `MsdfScalableExperimental` remains explicit and experimental.
- `cad-debug` defaults to direct-outline imagery.
- `msdf-debug` remains MSDF-only.

This remains tooling-only and does not change production UI text behavior.

See `docs/Machina.UI/history/machina-direct-outline-static-text-m9d.md`.

## M9e update

M9e carries that direct-outline backend into the component gallery as an explicit proof integration step.

- `samples/Machina.UI/Machina.ComponentGallery.Sample` now accepts `--include-direct-outline-text-proof`.
- the proof section renders real UI-ish strings through `DirectOutlineStaticTextRenderer` with `CrimsonText-Regular.ttf`.
- backend comparison labels are explicit: `Bitmap/current`, `DirectOutlineStatic`, and optional `MSDF experimental`.
- deterministic standalone artifacts now include `component-gallery-text-backend-comparison.png` and `direct-outline-static-text-proof.png`.

This remains proof-only:

- no production default text renderer switch
- no `Standard.Text` semantic change
- no `Machina.Core` document-model change
- no MSDF repair

See `docs/Machina.UI/history/machina-direct-outline-text-proof-m9e.md`.

## M9f update

M9f is the first real MSDF repair milestone after the proof/tooling groundwork.

- `DirectOutlineStatic` remains the geometry oracle.
- `MsdfScalableExperimental` remains explicit and experimental.
- the repaired experimental MSDF path now scales field dimensions with em size instead of reconstructing larger proof text from a fixed `32x32` field.
- atlas UV sampling now uses a texel-center contract.
- `.\tools\Export-MachinaMsdfAlignmentRepairM9f.ps1 -OutputDir artifacts\m9f -Clean` writes before/after direct-vs-MSDF evidence and alignment reports.

This remains proof/tooling-only:

- no browser-kerning oracle swap
- no arbitrary visual offsets
- no production default text renderer switch
- no `Standard.Text` or `Machina.Core` runtime integration change

See `docs/Machina.UI/history/machina-msdf-alignment-repair-m9f.md`.

## M9g update

M9g keeps the M9d/M9e proof-only boundary and formalizes deterministic UI-rectangle text layout for `DirectOutlineStatic`.

- `src/Machina.UI/Machina.Fonts/ReferenceRendering` now exposes direct-outline text box options, padding, alignment, clip mode, line-height mode, layout results, and proof rendering helpers.
- Typography-backed proof fonts now provide ascent, descent, line-gap, and units-per-em through a direct-outline metrics seam.
- explicit newline splitting is supported for proof multi-line layout.
- clipping is proof-side pixel clipping to the computed content rect.
- `samples/Machina.UI/Machina.ComponentGallery.Sample` now accepts `--include-direct-outline-text-layout-proof`.
- deterministic standalone artifacts now include `direct-outline-text-box-layout-proof.png` and `direct-outline-text-alignment-grid.png`.

This remains proof-only:

- no production default text renderer switch
- no `Standard.Text` semantic change
- no `Machina.Core` document-model change
- no `Machina.Layout` resolver change
- no new MSDF repair work

See `docs/Machina.UI/history/machina-direct-outline-text-layout-contract-m9g.md`.

## M9h update

M9h keeps the M9d/M9e/M9g proof-only boundary and adds the renderer-facing seam that sits between UI-ish text intent and the direct-outline proof backend.

- `src/Machina.UI/Machina.Fonts/ReferenceRendering` now exposes `StaticTextRenderRequest`, `StaticTextRenderResult`, and `DirectOutlineStaticTextRenderBridge`.
- the bridge validates renderer-facing text intent, maps it into `DirectOutlineTextBoxOptions`, and reuses the M9g layout/rendering contract.
- `samples/Machina.UI/Machina.ComponentGallery.Sample` now accepts `--include-direct-outline-render-bridge-proof`.
- deterministic standalone artifacts now include `direct-outline-render-bridge-proof.png` and `direct-outline-render-bridge-layout-grid.png`.
- tests now also guard the production dependency boundary so `Machina.Fonts.Tooling` does not leak into production packages.

This remains proof-only:

- no production default text renderer switch
- no `Standard.Text` semantic change
- no `Machina.Core` document-model change
- no new production dependency on `Machina.Fonts.Tooling`
- no MSDF backend change beyond preserving the existing explicit experimental/scalable path

See `docs/Machina.UI/history/machina-direct-outline-render-bridge-m9h.md`.

## M9i update

M9i is the closeout step for the current Machina font phase.

- `samples/Machina.UI/Machina.Presenter.Sample` now accepts `--include-direct-outline-render-bridge-proof`.
- the presenter sample exports a deterministic opt-in proof through `.\tools\Export-MachinaPresenter.ps1`.
- `DirectOutlineStatic` remains the static/reference path across diagnostics, layout, bridge proof, gallery proof, and presenter proof.
- MSDF remains explicit experimental/scalable after the M9f repair.
- `docs/Machina.UI/history/machina-font-phase-closeout-m9i.md` and `artifacts/m9i/font-phase-closeout-manifest.json|txt` define the canonical closeout commands, artifact paths, and deferred work.

This remains proof/tooling-only:

- no production default text renderer switch
- no `Standard.Text` semantic change
- no `Machina.Core` document-model change
- no word wrapping
- no production renderer integration

## M10a update

M10a begins presenter organization work after the M9 font closeout.

- the presenter sample now has an opt-in navigation shell with app-level sidebar sections and tabs local to the selected section
- the first/default shell page keeps the original settings-card presenter content alive instead of replacing it
- presenter page selection and per-page scroll offsets are explicit immutable state
- scrollbar geometry is deterministic and sample-local
- exports can now target representative shell pages and write a navigation manifest under `artifacts/m10a`

This remains sample-local:

- no production renderer default changed
- no `Machina.Core` document-model semantic changed
- no `Machina.Layout` resolver behavior changed
- no new font work was resumed
- no generic routing framework was introduced

## M10b update

M10b wires input onto the M10a shell without changing its architectural boundary.

- the presenter sample now translates Avalonia pointer and wheel events through a sample-local `AvaloniaPresenterInputBackend`
- backend-neutral shell hit testing covers sidebar items, local tabs, content viewport, scrollbar track, and scrollbar thumb
- input routes into explicit presenter navigation actions and the existing immutable navigation reducer
- wheel scrolling now updates selected-page scroll offsets with deterministic clamping and per-page preservation
- exports can now target representative interacted states under `artifacts/m10b`

This remains sample-local:

- Avalonia is still only the current backend, not the architecture
- no production renderer default changed
- no `Machina.Core` document-model semantic changed
- no `Machina.Layout` resolver behavior changed
- no new font work was resumed
- no generic routing framework was introduced

## M10c update

M10c makes the M10a/M10b shell the canonical presenter sample surface.

- the presenter now opens in the shell by default
- the old M1e single-card sample is preserved under `Legacy -> M1e Card`
- current presenter content is organized under `Overview`, `Components`, `Text`, `Diagnostics`, and `Legacy`
- shell exports now default to canonical shell states and write `presenter-shell-manifest.json|txt`

This remains sample-local:

- no production renderer default changed
- no `Machina.Core` document-model semantic changed
- no `Machina.Layout` resolver behavior changed
- M9 font work remains closed unless a concrete integration need appears
- no new component family was introduced

## M10d update

M10d stabilizes the existing presenter shell before any new workbench or card-system work resumes.

- `Text -> DirectOutlineStatic` now renders inside a bounded presenter proof card with corrected page-height accounting.
- `Text -> Proofs` no longer throws from negative remaining stack space inside a fixed-height sample card.
- presenter sample cards now use bounded title/body regions with sample-local clipped/truncated copy so content stays cell-like.
- scrollbar track paging remains, and sample-local thumb dragging now works through the same Avalonia adapter and backend-neutral routing seam introduced in M10b.

This remains sample-local:

- no production renderer default changed
- no `Machina.Core` document-model semantic changed
- no shared `Machina.Layout` resolver behavior changed
- no new component family was introduced
- M9 font work remains closed

## M11a update

M11a adds the first static Oblivion notebook/workbench card substrate inside the existing presenter shell.

- `Oblivion` is now the notebook/card/workbench layer.
- the presenter shell now contains `Oblivion -> Cards`, `Execution Roadmap`, and `Artifacts`.
- a sample-local deterministic card model now covers note, status, UI-preview placeholder, artifact placeholder, code-fact placeholder, and code-theory placeholder cards.
- bounded card rendering follows the M10d containment rule with explicit header/body regions and local clipping/truncation.
- deterministic local exports now include `artifacts/m11a` PNGs plus `oblivion-card-model-manifest.json|txt`.
- `Visionary` is documented only as the future code editor/source workspace layer.

This remains sample-local and static:

- no Roslyn execution
- no xUnit execution runtime
- no markdown editor
- no code editor implementation
- no reopened M9 font or MSDF milestone work

## M11b update

M11b is test topology and slow-pipeline cleanup only.

- `tests/Machina.UI/Machina.Fonts.Tooling.Unit.Tests` now holds the fast font-tooling unit coverage and is included in `Machina.UI.slnx`.
- `tests/Machina.UI/Machina.Fonts.Tooling.Tests` stays in `Machina.UI.Slow.slnx` for real export/MSDF/smoke coverage.
- output cleaner, source availability, preset requirement evaluation, and manifest building now have pure seams that can be tested without full rendering.
- MSDF before/after regression exports now run once per fixture instead of once per test method.
- script export workflows are documented as intentional smoke validation, not ordinary fast-loop unit tests.

This remains infrastructure-only:

- no Roslyn execution
- no xUnit notebook/runtime execution
- `[Fact]` / `[Theory]` execution remains deferred to M12 or later
- no font rendering behavior change
- no presenter/runtime behavior change

## M11d update

M11d lands Oblivion workspace persistence without adding execution.

- `workspace.oblivion.json` is now the JSON workspace root for the Oblivion tree/graph.
- `*.page.toml`, `*.card.toml`, and `*.artifact.toml` are the human-editable page/card/artifact metadata units.
- the presenter sample now loads a static sample workspace from disk into `Oblivion -> Cards`, `Execution Roadmap`, and `Artifacts`.
- load failures now render bounded error cards instead of crashing the presenter shell.
- deterministic manifest output now records the JSON/TOML split and workspace load counts under `artifacts/m11d`.

This remains persistence-only:

- no Roslyn execution
- no xUnit notebook/runtime execution
- no markdown editor
- no Visionary editor
- execution remains deferred to M12+

## M11e update

M11e hardens presenter and Oblivion card layout authoring after the M11d persistence landing.

- shared card-layout helpers now compute outer/content/header/body/footer regions for presenter and Oblivion cards
- presenter body text no longer loses height to mixed coordinate frames or overly tight body width math
- bullet clipping now reserves prefix width before clipping content
- legacy hosted-card background bleed is removed by keeping hosted wrappers transparent by default
- scrollbar track/thumb geometry is clamped fully inside the visible viewport chrome while preserving the M11c cached-composition model
- persisted Oblivion cards keep using the same JSON/TOML workspace assets through the hardened layout path

This remains bug-fixing and authoring-hardening only:

- no Roslyn execution
- no xUnit notebook/runtime execution
- no markdown editor
- no Visionary editor
- no new notebook/editor/runtime behavior
- no reopened font or MSDF work

## M11g update

M11g is the phase closeout and roadmap-hardening step for the current Oblivion substrate.

- M11 now closes with static persisted cards, workspace persistence, bounded layout, selection, and inspector behavior all in place.
- the sample workspace now includes explicit substrate-status, Markdown-first roadmap, Markdown-readiness audit, execution-deferred, execution-readiness audit, and Visionary-future cards.
- deterministic closeout manifests now record `markdownNext=true`, `executionEnabled=false`, and `factExecutionDeferredUntil=M13+`.
- M12 is now the recommended Markdown document/card support milestone.
- trusted local C# execution proof is deferred to M13+ or later unless explicitly re-prioritized.

This remains closeout-only:

- no Roslyn execution
- no xUnit notebook/runtime execution
- no Markdown renderer implementation beyond static planning copy
- no Markdown editor
- no Visionary editor

## M12a update

M12a lands the first Copeland Markdown frontend as the recommended follow-through to the M11g Markdown-first plan.

- `src/Copeland/Copeland.Markdown` now provides a deterministic `.md` lexer/scanner, parser, Markdown AST, diagnostics, backend-neutral document MIR, and text/json dump output.
- `tests/Copeland/Copeland.Markdown.Tests` now cover block parsing, inline parsing, diagnostics, MIR lowering, corpus smoke, and milestone boundary checks.
- `Copeland.Cli` now exposes `markdown parse` and `markdown export-corpus`.
- `tools/Export-CopelandMarkdownCorpus.ps1 -OutputDir artifacts\m12a` now writes local proof artifacts and corpus reports.
- existing docs under `README.md` and `docs/*.md` now serve as the first real Markdown corpus.
- `Machina.Standard.Text` remains unchanged; M12a documents future convergence through document MIR rather than forcing risky parser integration now.

This remains frontend-only:

- no external Markdown parser dependency
- no Markdown editor
- no production Oblivion Markdown rendering integration
- no Roslyn execution
- no xUnit notebook/runtime execution
- no Visionary implementation

## M12b update

M12b connects that frontend to Oblivion as a text-card body integration step.

- card TOML now supports `format = "copeland-markdown"` body entries
- text/note cards can load external workspace-root-relative `.md` body files
- those files compile through `Copeland.Markdown` into `DocumentMir`
- compact cards and the inspector now render Markdown-derived body content and diagnostics
- the canonical Oblivion model still remains JSON/TOML typed-card storage
- single-file Markdown remains a future export/import target only

Still deferred in M12b:

- Markdown editor
- Roslyn execution
- xUnit `[Fact]` / `[Theory]` execution
- Visionary

## M12c update

M12c is the Markdown rendering dogfood pass for Oblivion.

- `DocumentMir` now lowers into presenter-side Machina UI nodes in the sample/Oblivion layer
- compact cards now show clearer Markdown previews
- the inspector now renders headings, paragraphs, bullet lists, ordered lists, static code fences, inline code, strong/emphasis, and links distinctly enough for dogfooding
- diagnostics are now visible as badges plus a readable inspector diagnostics panel
- selected doc-derived Markdown samples now render from the sample workspace

Still deferred in M12c:

- Markdown editor and keyboard input
- file watcher / live editing
- Roslyn execution
- xUnit execution
- Visionary
- full CommonMark
- future image/table/video/code typed-card expansions beyond the current body integration

## M12d update

M12d turns selected existing repo docs into real Oblivion Markdown dogfood cards.

- the presenter sample now adds `Oblivion -> Docs`
- a deterministic curated docs list loads existing `docs/*.md` files as generated `note` cards
- each selected doc compiles through `Copeland.Markdown`
- each generated card preserves repo-relative source paths plus per-doc diagnostics
- a synthetic `docs-dogfood-index` status card summarizes loaded docs, generated cards, and diagnostic counts
- the canonical page model still remains JSON/TOML typed-card storage

Still deferred in M12d:

- Markdown editor and keyboard input
- file watcher / live editing
- single-file Markdown export/import implementation
- Roslyn execution
- xUnit execution
- Visionary

## M13c follow-through

M13c extends the M12d docs dogfood lane with a curated Aurelian slice while keeping the same doctrine.

- `Oblivion -> Docs` now includes selected Aurelian architecture and audit docs
- those docs compile through the existing `Copeland.Markdown` frontend
- repo-relative source paths and per-doc diagnostics remain preserved
- the docs index now summarizes Aurelian docs loaded and Aurelian diagnostics separately

Still deferred in M13c:

- SDSL-V migration into Copeland
- `Copeland.Shaders`
- `Machina.Aurelian` bridge work
- Vulkan presenter integration
- repo rename

## M13d follow-through

M13d is architecture doctrine only. It does not change Machina runtime behavior.

- `docs/Copeland/history/copeland-compiler-workshop-architecture-m13d.md` defines Copeland as the compiler workshop for Visionary.
- `docs/Copeland/history/copeland-compiler-lane-taxonomy-m13d.md` defines compiler-lane terminology and lane categories.
- `docs/Copeland/reference/machinalayout-js/text/README.md` and `docs/Copeland/architecture/copeland-roadmap.md` add the Copeland-side docs index and recommended sequence.
- `artifacts/m13d/copeland-compiler-workshop-manifest.json|txt` record that no migration, no `Copeland.Shaders` implementation, and no Machina/Aurelian/Vulkan bridge work were performed.

Still deferred in M13d:

- SDSL-V migration into Copeland
- `Copeland.Shaders` implementation
- GPU TypeScript frontend implementation
- PTX backend implementation
- Oct reimplementation work
- `Machina.Aurelian` bridge work
- Vulkan presenter integration
- repo rename

## M12e update

M12e is the card-architecture and locality hardening step for Oblivion.

- every Oblivion card is now treated as a self-contained applet
- the shell keeps navigation, selection, scrolling, routing, ordering, and persistence loading
- card kinds now route through a handler registry and own model, local state, actions, diagnostics, artifacts, compact view, inspector view, and future effect metadata
- existing Markdown notes and docs-dogfood cards continue through the note-card handler
- `CodeFact` and `CodeTheory` remain placeholder-only and non-executing

Still deferred in M12e:

- Roslyn execution
- xUnit notebook/runtime `[Fact]` / `[Theory]` execution
- runtime action dispatch
- Dominatus effect execution
- Markdown editor and file watcher
- Visionary code editor/source workspace

## M12f update

M12f adds the action/effect routing skeleton without adding execution.

- card actions now route through an explicit invocation contract
- handlers now create localized effect requests
- the shell/router now stores the last request/result per card id
- known effects route to deterministic deferred results
- unknown/custom effects route to deterministic rejected results
- the inspector now shows available actions plus the latest routed request/result state

Still deferred in M12f:

- Roslyn execution
- xUnit execution
- Dominatus-backed real effect execution
- artifact opening/export side effects
- Visionary

## M12g update

M12g adds the presenter keyboard input backend without turning M12 into an editor or execution phase.

- the presenter shell now translates Avalonia `KeyDown`, `KeyUp`, and `TextInput` through a sample-local adapter
- backend-neutral `PresenterKey`, modifier, and keyboard-input records now sit on the shell side of the boundary
- keyboard routing now supports section navigation, tab navigation, page scrolling, selected-card clearing, and deferred selected-card action routing
- pointer, wheel, and scrollbar behavior stay on the same existing reducer path
- text input now exists as shell plumbing only and is intentionally ignored as a deterministic no-op until a future editor target exists

Still deferred in M12g:

- full Markdown editor
- text buffer, caret, and selection model
- scaling and zoom input
- Roslyn execution
- xUnit execution
- Visionary
