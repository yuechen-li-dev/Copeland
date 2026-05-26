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

- **Machina.Dominatus**  
  render command bridge and Dominatus-driven runtime proofs/orchestration.

- **Machina.Renderer.Raster**  
  CPU raster surface + primitive pixel operations.

- **Machina.Renderer.Raster.Text**  
  raster text seam/debug text rendering.

- **Machina.Renderer.Raster.Dominatus**  
  Dominatus actuator adapter targeting raster backend and artifact flows.

- **Machina.Pipeline**  
  reusable `UiNode`-to-`MachinaFrame` service (lowering/layout/hit-test/render/raster).

- **samples/Machina.Presenter.Sample**  
  Avalonia bitmap presenter sample and interactive proof slice.

## Current Vertical Slice

Current proven path (with some steps still partial depending on scenario breadth):

`UiNode`  
-> `UiLoweringResult`  
-> `LayoutDocument`  
-> `ResolvedLayoutDocument`  
-> `UiHitTestIndex`  
-> render commands  
-> Dominatus `ActuatorHost`  
-> `RasterFrame`  
-> Avalonia bitmap window  
-> pointer hit-test  
-> `UiAction`  
-> dispatch table/runtime proof  
-> redraw.

Manual GUI validation depth across broader component scenarios is **Partial / needs continuing audit**.

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
| Scrolling | Layout/Runtime/Presenter | Planned | None yet | Define contract and input coupling.
| Z/painter ordering | Layout/Renderer | Partial | Contract-level coverage | Expand explicit tests across render adapters.
| UI.Text | Machina.Core | Implemented | Core tests | Deterministic measurement seam integrated.
| UI.Rect | Machina.Core | Implemented | Core tests | Style + child lowering path.
| UI.Row | Machina.Core | Implemented | Core tests | Lowers via stack arrangement.
| UI.Column | Machina.Core | Implemented | Core tests | Lowers via stack arrangement.
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
| Badge | Machina.Standard | Partial | Standard snapshot tests | Validate runtime semantics needs.
| Separator | Machina.Standard | Partial | Snapshot-level evidence | Confirm layout/render conventions.
| Label | Machina.Standard | Partial | Snapshot-level evidence | Formalize semantic behavior.
| Field | Machina.Standard | Partial | Standard form tests | Expand interaction states.
| Input shell | Machina.Standard | Partial | Standard form tests | Text editing not yet complete runtime path.
| Checkbox | Machina.Standard | Implemented | Standard form tests | Dispatch-friendly selection control available.
| Switch | Machina.Standard | Implemented | Standard form tests | Dispatch-friendly selection control available.
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
| Keyboard input | Runtime/Presenter | Planned | None | Define focus + key routing.
| Focus model | Runtime/Core | Planned | None | Required for interactive controls.
| Hover/pressed state | Runtime/Core | Planned | None | Add pointer state lifecycle.
| Text editing | Runtime/Standard | Planned | None | Required for real input components.
| Routing/navigation | Runtime/Presenter | Deferred | None | Later app-shell milestone.
| Dominatus runtime scopes | Machina.Dominatus | Partial | Dominatus tests/docs | Baseline proof exists; expand authoring patterns.
| Modal stack | Runtime/Dominatus | Planned | None | Coupled with focus/layer routing.
| Async effects | Dominatus | Planned | None | Needed for richer app behaviors.
| Render command bridge | Machina.Dominatus | Implemented | Dominatus tests | Active bridge layer.
| Snapshot backend | Dominatus/Renderer | Implemented | Snapshot/artifact tests | Good for deterministic proofs.
| Raster FillRect | Machina.Renderer.Raster | Implemented | Raster tests | Primitive rectangle fill available.
| Raster DrawText debug | Machina.Renderer.Raster.Text | Implemented | Text raster tests | Debug seam exists.
| Rectangular clipping | Raster stack | Partial | Renderer tests/docs | Expand clipping edge cases.
| PPM artifact/golden harness | Raster.Dominatus tests | Implemented | Golden artifact tests | Regression path in place.
| Borders | Renderer/Core styles | Planned | None | Roadmap M1f candidate.
| Rounded rects | Renderer/Core styles | Planned | None | Roadmap M1f candidate.
| Images | Renderer/Core | Deferred | None | Later asset pipeline milestone.
| Real text backend | Raster.Text adapter | Planned | Debug text tests only | Roadmap M1g candidate.
| PNG output | Renderer tooling | Planned | None | Add output adapter policy.
| Dirty rects | Renderer/runtime | Deferred | None | Performance milestone later.
| GPU backend | Future renderer | Deferred | None | Future platform strategy.
| Avalonia static bitmap window | Presenter sample | Implemented | Presenter docs/proofs | M0 presenter baseline.
| Avalonia click-to-action | Presenter sample | Partial | Presenter/runtime docs | Expand robust input loops.
| Avalonia redraw loop | Presenter sample | Partial | Presenter docs | Harden for multi-event scenarios.
| Scaling/DPI conversion | Presenter sample | Partial | Presenter M1d mapper/tests | Explicit image-to-root mapping landed for None/Fill/Uniform math; broader DPI policy still pending.
| Window resize handling | Presenter sample | Planned | None | Needed for practical desktop UX.
| MonoGame presenter | Future presenter | Deferred | None | Future backend.
| Web presenter | Future presenter | Deferred | None | Future platform milestone.

## Layer Roadmaps

- **Layout**: preserve deterministic math core; add targeted behavior only when contract-proven.
- **Core**: maintain declarative authoring surface and stable lowering artifacts.
- **Standard**: grow from tested essentials (Button/Form shell) toward composable control suite.
- **Runtime**: evolve from hit-test + deterministic transitions into focus, keyboard, and modal coordination.
- **Renderer**: keep snapshot confidence while adding style/text fidelity.
- **Presenter**: harden one reference shell before multiplying platforms.

## Runtime Control Model

Tiering rule:

- **Imperative local state**  
  tiny one-off code where introducing infrastructure would add noise.

- **DispatchTable**  
  simple deterministic field transitions (`Set`, `Toggle`, `Increment`, namespaced actions).

- **Dominatus**  
  mount/unmount equivalents, runtime scopes, async effects, orchestration, side effects, replay-oriented flows.

React mapping guide:

- mount/unmount -> Dominatus push/pop.
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
| Selection controls (`Checkbox`, `Switch`) | Implemented | Used in presenter settings sample with dispatch-table transitions.
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
- **M1c**: settings/counter presenter sample using dispatch-table transitions (implemented).
- **M1d**: image-to-root coordinate mapping for presenter clicks (implemented).
- **M1e**: Standard component + presenter visual tuning pass (flat deterministic polish, no new renderer primitives).
- **M1f**: scaling/DPI pointer conversion follow-through and resize behavior hardening.
- **M1g**: border/radius style support.

## Open Questions

- What minimum focus model unblocks keyboard + text input without over-scoping runtime?
- Which Standard components are truly required for “usable app UI” in next milestone?
- Should window-resize and DPI concerns be solved in presenter helper layer or platform-specific adapters?
- Where should async side effects first land: DispatchTable extensions vs Dominatus-only patterns?

## References

- `docs/layout-port-contract.md`
- `docs/ui-core-contract.md`
- `docs/standard-components-contract.md`
- `docs/machina-runtime-hit-testing.md`
- `docs/machina-runtime-dispatch.md`
- `docs/machina-dominatus-rendering.md`
- `docs/raster-renderer-contract.md`
- `docs/raster-dominatus-renderer.md`
- `docs/raster-text-renderer-contract.md`
- `docs/render-artifact-policy.md`
- `docs/machina-presenter-m0a.md`
- `docs/machina-presenter-m0b.md`
- `docs/machina-presenter-m0c.md`
- `docs/machina-presenter-m0d.md`
- `docs/machina-presenter-m1c.md`
- `docs/machina-presenter-m1d.md`
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
