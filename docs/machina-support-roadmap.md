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

  Reference-only upstream Dominatus source is available under `reference/dominatus`, especially `src/Dominatus.Assets.Toml` and `src/Dominatus.SpriteForge`. The active build remains pinned to NuGet `Dominatus.Core` and `Dominatus.OptFlow` `0.4.0`.

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

- **samples/Machina.ComponentGallery.Sample**
  canonical local widget wall / visual workbench for StandardUI.

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

M7a adds a dedicated component gallery so broader StandardUI visual inspection no longer has to overload the presenter sample. M7b formalizes its repeatable PNG export contract and local artifact workflow. M7c uses that workflow for evidence-backed visual defect triage and deferral documentation. M7d closes the deferred badge intrinsic-size / label-placement issue with a Badge-local contract and no general layout-engine changes. M7e records the current stable baseline and known limitations so the gallery can keep serving as the canonical local visual workbench without pretending typography and interaction fidelity are finished.

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
| PNG output | Renderer tooling | Partial | Gallery export tests + local script | M7b adds deterministic PNG export for the component gallery sample without introducing pixel-diff enforcement; M7e keeps the default local audit path on `artifacts/m7e/`. |
| Gallery-driven visual defect sweep | Machina.Standard + ComponentGallery sample | Implemented | Local PNG inspection + docs | M7c formalizes gallery-based visual triage and explicit deferral documentation; small safe fixes remain opportunistic. |
| Gallery baseline and limitation register | ComponentGallery sample + docs | Implemented | Local PNG inspection + docs + export-contract tests | M7e marks the current gallery baseline stable enough for routine audits and records intentional renderer/sample limitations. |
| Dirty rects | Renderer/runtime | Deferred | None | Performance milestone later.
| GPU backend | Future renderer | Deferred | None | Future platform strategy.
| Avalonia static bitmap window | Presenter sample | Implemented | Presenter docs/proofs | M0 presenter baseline.
| Avalonia click-to-action | Presenter sample | Partial | Presenter/runtime docs | Expand robust input loops.
| Avalonia redraw loop | Presenter sample | Partial | Presenter docs | Harden for multi-event scenarios.
| Component gallery visual workbench | ComponentGallery sample | Implemented | Dedicated gallery tests + local Windows visual audit + repeatable export script | Canonical “wall of widgets” page; local-first, not a Storybook clone; M7b formalizes deterministic PNG export and generated-artifact policy. |
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
- **Gallery workbench**: keep one explicit local visual page for StandardUI smoke checks without introducing web tooling.

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
Historical row-only checkbox/switch decomposition guidance is superseded for canonical app authoring. Prefer `StandardUI` controls/components in app documents; keep `StandardView` for leaf metadata and advanced custom sub-part composition. See `docs/standard-ui-vs-standard-view-m5d.md`.



## M5e headless harness
M5e standardizes component/document headless assertions through tests/Machina.Testing/GeometryHarness.cs so component tests can assert resolved rectangles, row presence, metadata, and hit targets without repeating lowering/resolve plumbing.

- M5f update: presenter sample is the canonical reference app and is contract-tested in tests/Machina.Presenter.Sample.Tests (document shape, hosted component boundary, localized StandardUI internals, plain C# dispatch, theme propagation, and geometry/hit-target stability).


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

M5i fixed the unrelated Windows blockers in `Copeland.Script.Tests` and `Copeland.Cli.Tests`, so `dotnet test Copeland.slnx` is green again on Windows. See `docs/copeland-windows-test-triage-m5i.md`.


## M8a font atlas architecture

M8a documents the future `Machina.Fonts` direction: async runtime MSDF atlas generation, immutable snapshot consumption, TOML/PNG atlas export/import, and headless gallery-export preflight. Implementation is staged for M8b+; M8a intentionally does not replace the current bitmap renderer or add Vulkan/native font dependencies. See `docs/machina-font-atlas-architecture-m8a.md`.

## M8b Machina.Fonts fake architecture

M8b adds the standalone `Machina.Fonts` project and focused tests for validated font atlas records, immutable snapshots, async channel-based fake generation, deterministic fake packing, pending/ready/missing resolution, and export-style preflight waiting. It is intentionally architecture-only: no MSDF, no real font parsing, no TOML or PNG output, no renderer integration, no native dependency, and no active dependency on the Dominatus reference submodule. See `docs/machina-fonts-m8b.md`.

### Machina M8c — Font atlas TOML metadata

M8c lands `.font-atlas.toml` document records, a deterministic writer, loader/parser, validation diagnostics, and snapshot conversion helpers inside standalone `Machina.Fonts`. It is metadata-only and defers real MSDF generation, font loading, PNG writing, and renderer integration to later milestones.

### Machina M8d — fake atlas artifacts

M8d lands the fake atlas artifact pipeline: ready fake-worker snapshots export to deterministic `.font-atlas.toml` plus `.fakepage` page files, import validates existence, content hashes, and fake page dimensions, and tests prove roundtrip equivalence. Real MSDF, PNG, font parsing, and renderer integration remain deferred. See `docs/machina-font-atlas-artifacts-m8d.md`.

### Machina M8e — MSDF and outline dependency audit

M8e is a research/design milestone only. It audits current public sources for `MSDF-Sharp.Core`, `LayoutFarm/Typography`, `SixLabors.Fonts`, and `SharpFont`, then recommends a strict Machina-owned adapter boundary. Current recommendation:

- prefer `Typography.OpenFont` for outline extraction,
- prefer `MSDF-Sharp.Core` for MSDF generation,
- avoid `MSDF-Sharp.Extensions` in the first real path because it pulls in `SixLabors.ImageSharp`,
- avoid `SixLabors.Fonts` for now because of the split-license policy,
- keep native FreeType as fallback only.

No package references or implementation behavior change land in M8e. See `docs/machina-font-msdf-dependency-audit-m8e.md`.

### Machina M8f — generation adapter seam

M8f lands that strict Machina-owned adapter boundary in code without adopting any real dependency yet.

- Machina-owned outline records and generation diagnostics are implemented in `Machina.Fonts.Generation`.
- `IGlyphOutlineSource` and `IGlyphDistanceFieldGenerator` now exist as compile-checked seams.
- `FakeGlyphOutlineSource`, `FakeGlyphDistanceFieldGenerator`, and `GlyphGenerationPipeline` prove deterministic output, cancellation behavior, missing-outline short-circuiting, and diagnostic aggregation.
- No `Typography.OpenFont`, `MSDF-Sharp.Core`, `SixLabors`, `FreeType`, renderer integration, or native dependency is added.

See `docs/machina-font-generation-adapters-m8f.md`.

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

See `docs/machina-typography-outline-adapter-m8g.md`.

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

See `docs/machina-msdf-sharp-generator-m8h.md`.

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

See `docs/machina-distance-field-atlas-packing-m8i.md`.

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

See `docs/machina-cpu-msdf-text-renderer-m8k.md`.

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

See `docs/machina-cpu-msdf-text-proof-audit-m8l.md`.

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

See `docs/machina-component-gallery-msdf-proof-m8m.md`.

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

See `docs/machina-cpu-msdf-spacing-kerning-m8n.md`.

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

See `docs/machina-msdf-reference-oracle-m8o.md`.
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

See `docs/machina-msdf-vertical-metrics-m8q.md`.

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

See `docs/machina-msdf-baseline-rounding-fix-m8q1.md`.

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

See `docs/machina-msdf-baseline-guide-overlay-m8q2.md`.

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

See `docs/machina-msdf-reference-diff-overlay-m8r.md`.

## M8s update

M8s adds a direct Typography-outline mask oracle and turns the proof stack into a three-way browser/direct-outline/MSDF diagnostic.

- `src/Machina.Fonts/ReferenceRendering` now includes deterministic outline flattening, supersampled direct-outline mask rasterization, shared `InkMask` extraction, edge extraction, pairwise shape metrics, and overlay helpers.
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

See `docs/machina-msdf-three-way-shape-diff-m8s.md`.
