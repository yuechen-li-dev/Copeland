# Machina Component Model Probe M5a

## Purpose

This M5a milestone is an audit/probe to determine whether Machina can be refactored cleanly toward a component model that separates:

- explicit document-level layout tables,
- localized component functions,
- immutable record-based state and style,
- simple C# dispatch by default,
- Dominatus for lifecycle/effects/orchestration,
- and headless-first validation.

This document does not implement the refactor.

## Proposed model

### Architectural split

- **Layout (document/root)**: explicit `UiDocument`/`UiRow` tables answer placement and owned rectangles.
- **Component (local)**: each component owns internal composition, local layout details, emitted actions, and style contract.
- **State/transitions**: immutable records and ordinary C# `if`/`switch` dispatch by default.
- **DispatchTable**: retained where transitions must be data-shaped.
- **Style**: explicit records, customized with `with`, no CSS-like cascade.
- **Testing**: headless geometry/action/semantic assertions are primary; manual GUI is secondary confirmation.

### Invariants

- Style padding is paint metadata only.
- Layout-affecting insets must be explicit rows/frames.
- Screen-level documents place components; they should not manually decompose component internals.
- If composition inside one declaration becomes deep/opaque, promote child structure into named component functions.

## Current implementation inventory

### Core layout/table authoring

- Flat authoring primitives exist: `UiDocument`, `UiRow`, and `Row.*` frame helpers.
- `UiDocumentLowerer` lowers rows directly to layout rows and metadata dictionaries.
- Hybrid hosted component rows are supported (`UiRow.Component`), with scoped row ids during lowering.

### Core node-tree authoring

- `UiNode` + `UI.*` authoring remains active and well-tested.
- Standard components are authored as C# functions returning `UiNode`.

### Standard layer

- `StandardUI` currently routes to specific component constructors and already has local component boundaries.
- `StandardView` provides `UiView` metadata helpers for flat row authoring, including sub-part helpers for checkbox/switch skin rows.
- Theme tokens already exist as immutable records (`StandardTheme`, `StandardColors`, `StandardSpacing`, `StandardRadius`).

### Runtime and presenter

- Runtime dispatch model includes `DispatchTable` and `DispatchTransitions` helpers.
- Presenter sample still defaults to `DispatchTable<DemoState>` even though its state updates are simple field toggles/increment.
- Presenter document shape already places a single hosted component (`settings-card`) at top-level.

### Docs and prior milestones

- Existing docs already establish row-first + hybrid direction and explicit padding hardening.
- Existing docs still retain older guidance from flat composition experiments that encourage app-level decomposition of control internals in some places.

## Audit findings

### Layout as tables

**Assessment: yes (with one caveat).**

What is already aligned:

- `UiDocument` + `UiRow` is a concrete table model.
- Row frames are explicit (`Root`, `Anchor`, `Absolute`, `Fixed`, `Fill`) and lowered deterministically.
- Arranged stack behavior is represented as row arithmetic (`Arrange`).

Limitation/caveat:

- Layout table APIs are strong, but some sample/docs history still demonstrates control internals at screen level (pre-hybrid habits). This is not a type-system limitation; it is an authoring-convention drift.

### Components as localized functions

**Assessment: partial to strong.**

Aligned:

- `StandardUI.*` is already function-oriented C# API.
- Hybrid row-hosting allows screen table placement while keeping internals local.
- Hosted lowering scopes component internals under host ids, preserving host ownership boundaries.

Friction:

- `StandardView` includes helpers for full controls (`Button`, `Checkbox`, `Switch`, `Input`) and sub-parts (`CheckboxBox`, `SwitchTrack`, `SwitchThumb`), which can encourage document-level decomposition of control internals when used for app screens.
- Some docs/tests still demonstrate this decomposition pattern from earlier milestones.

### State and transitions

**Assessment: partial; default should change for C# hand-authored apps.**

Current:

- `DispatchTable` and `DispatchTransitions` are mature and tested.
- Presenter sample uses `DispatchTable<DemoState>` for very simple transitions.

Evaluation:

- For simple local app state in C#, plain `if`/`switch` dispatch is clearer and more local.
- `DispatchTable` remains valuable for data-shaped transitions, table-driven tooling, snapshots, and cross-language/lowering contexts.

### Styling model

**Assessment: partial, good foundation.**

Aligned:

- Theme/tokens are immutable records.
- `StandardUI` accepts optional `StandardTheme` parameters.
- Prior hardening codifies “style padding is paint metadata only.”

Gaps:

- There is no explicit “root theme passed through build pipeline” convention yet in presenter sample patterns.
- There is no first-class per-component style-record contract surface (for example, dedicated `ButtonStyle`, `CardStyle` records layered on top of theme tokens).
- `StandardView` pulls `StandardTheme.Default` directly, which reduces explicit style injection at document build sites.

### Headless testing model

**Assessment: yes, with targeted coverage gaps.**

Aligned:

- Existing tests cover dispatch, row-lowering, hosted component behavior, geometry regression, standard component geometry, raster artifacts, and hit-testing.
- Presenter sample geometry has dedicated regression tests (M4e direction).

Gaps:

- Manual GUI still appears in docs as proof language in some places; should consistently be reframed as secondary confirmation.
- A reusable test harness for “component in host rect” assertions could reduce duplication and make component contract tests cheaper to add.

## Component-by-component audit

| Component | Current shape | Proposed shape | Migration need | Notes |
| --- | --- | --- | --- | --- |
| Button | `StandardUI.Button` component function with explicit geometry hardening | Keep as localized component function | Needs minor cleanup | Strong fit; ensure style contract can be record-parameterized and themed from root context. |
| Card | `StandardUI.Card` hosts content with explicit inset region post-M4c | Keep as localized component function | Good as-is | Already aligned with explicit layout-padding invariant. |
| Checkbox | `StandardUI.Checkbox` component plus `StandardView.CheckboxBox` sub-part metadata helper | Keep component function for app use; retain sub-part helper for flat specialized cases | Needs minor cleanup | App docs should prefer hosted component use; sub-parts should be documented as advanced/manual skin helpers. |
| Switch | `StandardUI.Switch` component plus `StandardView.SwitchTrack`/`SwitchThumb` sub-parts | Keep component function for app use; retain sub-parts for advanced flat authors | Needs minor cleanup | Same as checkbox; avoid app-level decomposition by default guidance. |
| Input | `StandardUI.Input` shell component with explicit content region hardening | Keep as localized component function | Needs minor cleanup | Good base; style-record contract should become explicit. |
| Field | `StandardUI.Field` composition helper | Keep as localized component function | Good as-is | Fits locality and declarative composition intent. |
| Label | `StandardUI.Label` simple text helper | Should stay leaf/metadata helper | Good as-is | Minimal and clear. |
| Badge | `StandardUI.Badge` simple styled helper | Should stay leaf/metadata helper | Good as-is | Minimal and clear. |
| Separator | `StandardUI.Separator` simple line visual helper | Should stay leaf/metadata helper | Good as-is | Minimal and clear. |

## Dispatch model audit

### Q5 decision

- **Default for hand-authored C# app code:** plain C# dispatch (`switch`/`if`).
- **DispatchTable status:** keep and document as advanced/data-shaped option.
- **Presenter sample recommendation:** migrate sample to plain C# dispatch in M5b to model “boring explicit C# first.”

### Why

- Presenter state transitions are simple and domain-local (increment/toggle fields).
- Plain method form improves readability and makes action/state coupling obvious.
- DispatchTable remains important where transition data shape matters (tooling, generated flows, serialization, cross-language contracts).

### Docs to update (future milestone)

- `docs/machina-runtime-dispatch.md`
- `docs/machina-presenter-m1c.md`
- `docs/machina-support-roadmap.md` runtime control tier wording

## Styling/theme audit

### Current readiness

- Strong token foundation through `StandardTheme` record families.
- Good invariants around explicit layout vs paint padding.

### Needed for target model

1. Add explicit “root theme/context” patterns in canonical sample docs and presenter sample build path.
2. Introduce component style records (e.g., button/card/input style contracts) that are plain records and can be customized using `with`.
3. Keep no-cascade rule explicit.
4. Keep `StandardView` helpers intentionally minimal and clarify that they are metadata helpers, not full component-style API.

## Testing audit

### Current coverage

- Dispatch tables and transitions: covered.
- Standard component geometry/hit behavior: covered.
- Presenter sample resolved geometry regressions: covered.
- Raster command/artifact paths: covered.

### Remaining manual reliance

- Manual presenter window checks are still useful for end-to-end visual sanity but should not be required for correctness claims.

### Missing tests to close gaps

- Shared component-host geometry assertion helpers (M5e).
- Contract tests for style-record override behavior (future M5c).
- Explicit tests proving screen-level row stability when component internals evolve (hosted boundary safety).

## Migration recommendation

**Recommendation: proceed.**

The proposed architecture is more correct than the current mixed model for this codebase’s goals:

- better locality,
- clearer separation of concerns,
- stronger headless testability,
- and less risk of CSS/XAML/React-like implicit behavior creeping in.

The existing code already contains most required primitives; migration is primarily a convergence of defaults, docs, and sample patterns rather than a foundational rewrite.

## Proposed milestone plan

### M5b — Presenter sample dispatch simplification

- Replace presenter `DispatchTable` usage with plain C# dispatch method for `DemoState`.
- Preserve `DispatchTable` API/tests untouched.
- Update presenter/runtime docs to distinguish default vs advanced transition styles.

### M5c — Component style records / theme model

- Add explicit root theme/style record handoff in canonical sample(s).
- Add component-level style records for key controls (start with Button/Card/Input).
- Add `with` customization examples and tests.
- Keep no-cascade semantics explicit.

### M5d — Standard component contract cleanup

- Clarify contract split:
  - `StandardUI` = primary component-function surface.
  - `StandardView` = lightweight row metadata/leaf helpers.
- Reclassify/document sub-part helpers as advanced composition helpers.
- Keep compatibility shims and existing tests while de-emphasizing misleading usage patterns.

### M5e — Component geometry test harness

- Add reusable helpers for “resolve component within host rect.”
- Standardize assertions: shell/content/label/action/hit-target bounds and metadata.
- Reduce per-component test duplication and increase contract consistency.

### M5f — Presenter sample canonical rewrite

- Build document as component placement table.
- Keep component internals localized in named functions.
- Use plain C# dispatch default.
- Pass style/theme explicitly via root record context.
- Publish as canonical “boring C# Figma-in-code” sample.

## Risks and blockers

### Risks

- **Dual-surface confusion (`StandardUI` vs `StandardView`)**: developers may continue mixing app-level control decomposition unless docs and examples converge.
- **Backward-compat pressure**: cleanup must avoid abrupt breaking changes to existing tests/samples.
- **Theme API churn**: introducing per-component style records without over-fragmenting tokens requires careful sequence.

### Blockers

- No fundamental technical blocker found in layout/runtime pipeline.
- Main blockers are alignment/documentation/default-pattern convergence.

## Conclusion

The audit indicates Machina is already close to the target architecture. The critical work is converging authoring defaults and documentation around patterns the system already supports:

- explicit screen tables,
- localized components,
- plain C# state transitions by default,
- explicit record-based styles/themes,
- headless-first test contracts.

Proceeding with M5b+ is recommended with small, reversible milestones.


## M5b follow-through status

M5b landed the recommended presenter simplification: `DemoState` transitions now use plain C# dispatch over typed `UiActionId` constants in the presenter sample. `DispatchTable` remains intact in runtime APIs, tests, and docs as the advanced data-shaped option.

- M5c update: StandardTheme now carries typed component style records and supports explicit root theme handoff with `with` customization.
