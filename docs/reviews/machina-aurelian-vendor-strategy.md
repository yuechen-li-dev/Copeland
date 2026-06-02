# Machina UI vendor strategy audit for Aurelian

## 1. Files changed

| File | Reason |
| --- | --- |
| `docs/reviews/machina-aurelian-vendor-strategy.md` | Added a docs-only guest-review audit of whether and how Machina UI should be carried into the planned Aurelian engine/tooling stack. |

## 2. Task scope

This review audits the Copeland repository's Machina UI work from the perspective of Aurelian, a planned greenfield C# engine direction after the Stri-V retrofit effort was paused. The Stri-V codebase and reports are not present in this repository, so this audit uses only the Aurelian context supplied in the task plus local Copeland source, tests, and docs.

Constraints observed:

- No source code changes.
- No project file changes.
- No dependency changes.
- No package creation.
- No Aurelian integration work.
- Validation limited to docs-only existence and repository status checks.

## 3. Executive recommendation

Aurelian should use Machina as a **design reference now** and a **tooling/UI candidate later**, but should **not make Aurelian core depend on Machina at this stage**.

Recommended strategy: **hybrid upstream/reference first**.

1. Keep Copeland/Machina as the active upstream while Aurelian core architecture settles.
2. Carry the stable concepts into Aurelian planning now: layout rows/documents, resolved layout documents, lowering output shape, hit-test index, semantic/action metadata, render-command extraction, raster snapshots, and the pipeline-frame idea.
3. Do not vendor the full Machina stack into Aurelian yet, because authoring APIs, text/rich text, Avalonia hosting, and Dominatus-specific runtime bridges still look like active experiments rather than final engine contracts.
4. When Aurelian starts tool UI work, place any Machina-derived dependency under `Aurelian.Tools.Ui` and/or `Aurelian.Tools.Host`, not under `Aurelian.Core`, `Aurelian.Runtime`, `Aurelian.World`, assets, shaders, or renderer/HAL core packages.
5. Never let Avalonia, Copeland sample/demo code, debug raster assumptions, or Machina's current public authoring shape enter Aurelian core.

Answer to the central question: **yes, Aurelian should use Machina, but later and selectively**. Machina is most valuable as a headless, deterministic UI document/lowering/layout/render-command reference for tools. Before direct vendoring or packaging, Copeland should stabilize the authoring contract, mark experimental APIs clearly, harden text measurement/rasterization, clarify host boundaries, and define a packaging/vendoring plan.

## 4. Copeland/Machina project inventory

Inventory command observations:

- `src/Machina.Cli` was requested for inspection but does not exist in the current tree.
- Avalonia appears in the presenter sample under `samples/Machina.Presenter.Sample`, not in a main `src/Machina.*` project.
- The solution includes source, sample, test, and vendored Dominatus projects.

| Project/folder | Apparent role | Dependencies | Maturity guess | Aurelian action |
| -------------- | ------------- | ------------ | -------------- | --------------- |
| `src/Machina.Layout` | Deterministic layout rows, documents, frame specs, arrange specs, compiler, resolver, resolved tree projection. | No project references. | Strongest core contract; compact resolver implementation should be reviewed for readability, but tests and docs suggest active hardening. | Carry concept only |
| `src/Machina.Core` | Authored `UiNode` model, flat `UiDocument`/`UiRow`, styles, semantics, actions, lowering to layout rows plus metadata. | `Machina.Layout`. | Useful but mixed maturity: contracts are promising; authoring helpers and node set are still likely to evolve. | Needs stabilization |
| `src/Machina.Standard` | Standard component authoring helpers, component style records, theme records, rich text model/parser. | `Machina.Core`, `Machina.Layout`. | Component shell appears tested; rich text is newer and explicitly roadmap-oriented. | Carry concept only |
| `src/Machina.Runtime` | Hit testing, pointer coordinate mapping, deterministic action dispatch table. | `Machina.Core`, `Machina.Layout`. | Small, focused, and relevant; not yet a complete UI runtime with focus/keyboard/modal behavior. | Carry concept only |
| `src/Machina.Dominatus` | Dominatus render-command bridge, render snapshots, sample counter UI runtime, action events. | `Machina.Core`, `Machina.Layout`, `Machina.Standard`, vendored `Dominatus.Core`, vendored `Dominatus.OptFlow`. | Valuable compatibility experiment for Aurelian's Dominatus-native spine, but too coupled to current Dominatus package details for core import. | Keep upstream/reference |
| `src/Machina.Renderer.Raster` | CPU raster surface, color, PPM encoding, rectangle fill/stroke primitives. | `Machina.Layout`. | Good deterministic test backend; not enough evidence it is intended as production renderer. | Carry concept only |
| `src/Machina.Renderer.Raster.Text` | Debug/readable bitmap text rasterizer seam. | `Machina.Renderer.Raster`, `Machina.Layout`, `Machina.Core`. | Useful for snapshots and tests; not a text correctness solution. | Needs stabilization |
| `src/Machina.Renderer.Raster.Dominatus` | Dominatus actuation handler and recorder that turn render commands into `RasterFrame` artifacts. | `Machina.Core`, `Machina.Layout`, `Machina.Dominatus`, raster projects, vendored `Dominatus.Core`. | Strong deterministic proof path; tightly coupled to current command/actuator bridge. | Keep upstream/reference |
| `src/Machina.Pipeline` | End-to-end `UiNode`/`UiDocument` to lowering, layout, hit test, Dominatus render commands, and raster frame. | Most Machina projects plus vendored Dominatus packages through references. | Excellent integration proof; too broad and too coupled for Aurelian core. | Keep upstream/reference |
| `samples/Machina.Presenter.Sample` | Avalonia desktop bitmap presenter with pointer click-to-action loop. | Machina pipeline/runtime/standard/raster projects plus `Avalonia` and `Avalonia.Desktop`. | Useful bootstrap host proof; explicitly sample-level, partial presenter maturity. | Defer |
| `src/Copeland.Script` | Separate Copeland language/compiler subsystem. | No project references. | Out of Machina UI vendor scope. | Do not carry |
| `src/Copeland.Cli` | CLI for Copeland script tooling. | `Copeland.Script`. | Out of Machina UI vendor scope. | Do not carry |
| `tests/Machina.*` | Unit, contract, snapshot, presenter sample, and renderer tests for Machina areas. | Test projects reference relevant Machina projects and test SDK packages. | Strong evidence of headless test direction; should guide Aurelian acceptance criteria. | Carry concept only |
| `docs/machina-*`, `docs/raster-*`, `docs/reference/machinalayout-js/*` | Machina design docs, audits, contracts, reference material, roadmap. | Documentation. | Valuable context; contains both implemented contracts and planned/deferred work. | Keep upstream/reference |

## 5. Machina architecture summary

### 5.1 Actual architecture answers

1. **Authored UI model**: Machina has two authoring paths. The typed tree path uses abstract `UiNode` records with optional ids, semantics, and declared actions, plus helpers such as `UI.Text`, `UI.Rect`, `UI.Row`, `UI.Column`, `UI.Button`, `UI.Surface`, and `UI.Layer`. The flat path uses `UiDocument` containing explicit `UiRow` records, where each row supplies id, parent, frame, arrange metadata, view metadata, and optionally a hosted component.
2. **Lowering output**: lowering produces `UiLoweringResult`: layout rows plus dictionaries keyed by `NodeId` for styles, text styles, semantics, and actions. This is a clean separation point between authored UI and layout/runtime/render metadata.
3. **Layout resolution output**: layout compilation turns rows into `LayoutDocument` with a single root, node map, and ordered children map. resolution produces `ResolvedLayoutDocument`, preserving the graph and adding resolved rectangles for every node.
4. **Rendering representation**: Machina's render bridge emits Dominatus `IActuationCommand` records such as begin/end frame, fill/stroke rect, draw text, and clip commands. The raster path dispatches those commands through a Dominatus actuator host into a `RasterFrame` containing a `RasterSurface` and PPM export.
5. **Hit testing and actions**: `UiHitTestIndex` is built from a resolved layout document plus action/semantic dictionaries. It stores actionable non-empty rectangles and returns the last matching candidate in reverse traversal order, providing node id, rect, action, and optional semantics.
6. **Semantics**: semantics are represented as `UiSemantics` with role, label, disabled, and focusable fields. The current semantics model supports roles such as text, label, button, checkbox, switch, and input, and it feeds text drawing only for text/label roles.
7. **Dominatus**: Dominatus appears in render-command contracts, render snapshots, bridge construction, raster actuation, `MachinaFrame`, and sample UI runtime. This aligns with Aurelian's Dominatus-native direction, but also couples current pipeline projects to vendored Dominatus types.
8. **Avalonia/window/input**: Avalonia is not in a core Machina source project. It appears in `samples/Machina.Presenter.Sample`, which presents a raster frame as an Avalonia image, maps pointer positions back to root coordinates, performs hit testing, dispatches actions, and re-renders. Current docs describe it as implemented/partial rather than a hardened host layer.
9. **Text/rich text**: simple text exists in core `TextNode`, `TextStyle`, deterministic measurement, and the readable bitmap rasterizer. Rich text lives under `Machina.Standard.Text` with block/inline records and a parser informed by imported JS reference docs. Existing docs explicitly describe the simple text path as transitional until proper Machina.Text layout is integrated.
10. **Stable versus WIP**: the most stable-looking pieces are layout rows/documents, resolved layout documents, lowering result shape, hit-test index, semantic/action dictionaries, deterministic raster artifacts, and the end-to-end pipeline as a proof. WIP/unstable areas include public authoring API shape, rich text/text layout correctness, Avalonia host hardening, focus/keyboard/input runtime, packaging, and deciding whether raster is a production backend or deterministic test backend.

### 5.2 Responsibility table

| Layer | Current Machina type/project | Responsibility | Aurelian relevance | Risk |
| ----- | ---------------------------- | -------------- | ------------------ | ---- |
| Authoring API | `Machina.Core.Authoring.UI`, `Machina.Standard.Authoring.StandardUI`, `StandardView`, `UiNode` subclasses | User-facing construction of UI trees and standard controls. | Useful for tool UI prototyping. | Public API may freeze too early; current helpers mix convenience with experimental component assumptions. |
| Core UI model | `UiNode`, `UiDocument`, `UiRow`, `UiView`, styles, semantics, actions | Authored tree/flat document and node metadata. | Strong conceptual fit for engine tools and inspectors. | Tree and flat paths need a clear stable contract story. |
| Lowering | `UiLowerer`, `UiDocumentLowerer`, `UiLoweringResult` | Convert authored forms into layout rows plus side-channel metadata. | Very strong fit for Aurelian's desired separation of authored model, lowering, layout, semantics/actions, and rendering. | Text measurement during lowering may create unwanted coupling if not formalized. |
| Layout | `LayoutRow`, `LayoutDocument`, `LayoutCompiler`, `LayoutDocumentResolver`, `ResolvedLayoutDocument` | Validate row graph, order children, resolve frames/arrangements to rectangles. | Strong fit for deterministic headless tooling UI. | Resolver is active/hardening code; future editor demands may require more layout primitives. |
| Runtime/input | `UiHitTestIndex`, `PresentedImageMapper`, `PointerPoint` | Hit testing and presenter coordinate conversion. | Good seed for Aurelian tools. | Does not yet cover keyboard, focus, drag/drop, IME, modal routing, or accessibility. |
| Semantics/actions | `UiSemantics`, `UiRole`, `UiAction`, `UiActionId`, `DispatchTable` | Attach labels/roles/action ids and dispatch deterministic state transitions. | Good match for typed lifecycle/action routing concepts if kept decoupled. | Needs richer event/action contract before editor-grade use. |
| Raster renderer | `RasterSurface`, `Rasterizer`, `RasterFrame`, `RasterRenderRecorder` | Deterministic CPU rendering and artifact output. | Useful for headless tests and early tools. | Should not be mistaken for Aurelian renderer/HAL or production UI renderer without a separate decision. |
| Text/rich text | `TextNode`, `TextStyle`, `DeterministicTextMeasurer`, `ReadableBitmapTextRasterizer`, `Machina.Standard.Text` | Simple text drawing plus emerging rich text source/document/parser model. | Important for tools, inspectors, docs panels, and later editor. | Current text subsystem is explicitly transitional/WIP; correctness, shaping, wrapping, overflow, and font backend are unresolved. |
| Dominatus bridge | `Machina.Dominatus`, `Machina.Renderer.Raster.Dominatus`, `MachinaFrame` | Convert UI render extraction into Dominatus actuation commands/snapshots and sample runtime events. | Architecturally interesting for Aurelian's Dominatus-native runtime spine. | Current coupling to vendored Dominatus packages can leak into engine core if imported wholesale. |
| Avalonia/window host | `samples/Machina.Presenter.Sample` | Desktop bitmap presenter, pointer handling, action dispatch loop. | Practical bootstrap host for tools. | Sample-level dependency must not become engine core dependency; resize/DPI/input are partial. |
| CLI/tooling | `src/Copeland.Cli`, no `src/Machina.Cli` present | Copeland script CLI, not Machina UI CLI. | No direct Aurelian UI vendor value. | Out of scope; avoid carrying Copeland-specific tooling. |

## 6. Aurelian fit analysis

| Aurelian need | Machina support today | Gap/risk | Recommendation |
| ------------- | --------------------- | -------- | -------------- |
| Tool/editor UI document model | `UiNode` tree and flat `UiDocument`/`UiRow` model both exist. | Dual authoring paths need stability boundaries; standard components may not match future editor needs. | Use as design reference now; carry stable contracts later under tooling. |
| Deterministic headless UI tests | Layout, lowering, hit testing, raster, golden/artifact tests are present across `tests/Machina.*`. | Test coverage is promising but not a formal Aurelian acceptance suite. | Carry the test philosophy and snapshot shapes. |
| Layout system | Layout rows, compiler, frame resolver, stack/grid arrangements, resolved documents. | Future editor may need richer layout, scrolling, virtualization, and constraints. | Carry concept and selected contracts after stabilization. |
| Input/hit testing | Hit-test index and presenter coordinate mapper exist. | Pointer-only baseline; no full focus, keyboard, drag/drop, text input, modal routing, IME, or accessibility. | Carry hit-test concept; defer full runtime import. |
| Semantic/action routing | `UiSemantics`, `UiAction`, action dictionaries, `DispatchTable`, Dominatus action event sample. | Action ids are string-like; typed Aurelian lifecycle/event contracts are not represented yet. | Keep one-way adapter from Aurelian/Dominatus concepts to Machina UI actions. |
| Render command extraction | `MachinaRenderBridge` emits explicit render commands from lowering+resolved layout. | Commands are currently Dominatus `IActuationCommand`s; Aurelian render snapshots/command plans may want neutral contracts. | Copy the concept; define Aurelian-neutral render UI commands before binding to engine renderer/HAL. |
| Raster/headless output | CPU raster surface, PPM output, raster frame, artifact tests. | Debug/test backend status; not production renderer. | Use for deterministic test artifacts only unless separately promoted. |
| Text rendering | Simple text measurement/drawing, readable bitmap rasterizer, rich text parser/model docs and tests. | Text layout/shaping/wrapping/overflow/correct font backend are WIP/deferred. | Needs stabilization before Aurelian consumption. |
| Avalonia hosting/windowing | Avalonia presenter sample exists and proves bitmap-host click loop. | Sample-level host; partial DPI/resize/input maturity; no dedicated `Machina.Host.Avalonia` package. | Defer; use only under `Aurelian.Tools.Host` if needed. |
| Dominatus integration compatibility | Strong bridge experiments and counter runtime sample. | Coupled to vendored Dominatus APIs; Aurelian may evolve its own Dominatus spine. | Keep upstream/reference; adapt later through a narrow boundary. |
| Integration with future Aurelian render snapshots/command plans | Machina's extraction shape is aligned with snapshots/commands. | Current commands are UI-specific and Dominatus-specific, not Aurelian rendering contracts. | Use concept to inform `Aurelian.Rendering.Contracts`; avoid direct dependency now. |
| Packaging/vendor strategy | Multiple projects and solution entries exist; no evident stable package split. | API stability and dependency cleanliness not ready for blind vendoring. | Hybrid upstream/reference now; selective vendor/package later. |

## 7. Carry-over recommendation

| Machina area | Carry to Aurelian? | How | Why |
| ------------ | -----------------: | --- | --- |
| Layout document model | Yes, later | Carry stable contract or vendor `Machina.Layout`-like package into `Aurelian.Tools.Ui`. | Clean, deterministic, backend-independent UI geometry document. |
| Resolved layout document model | Yes, later | Carry stable contract. | Strong fit for hit testing, render extraction, snapshots, and tests. |
| UI lowering result shape | Yes, later | Carry contract shape, not necessarily current implementation. | Excellent separation of rows/styles/text/semantics/actions. |
| Hit-test index | Yes, later | Carry or rewrite against Aurelian-resolved layout. | Simple deterministic runtime bridge for tool panels. |
| Semantic/action model | Yes, selectively | Carry concept; retype action ids/events for Aurelian if needed. | Allows UI to describe intent without owning side effects. |
| Headless raster frame/test artifacts | Yes, for tests | Carry concept and maybe raster test backend. | Useful for deterministic validation independent of Avalonia/GPU. |
| Pipeline frame concept | Yes, concept | Recreate as Aurelian tooling pipeline artifact. | Captures each stage output for debugging and snapshot tests. |
| Authoring row API | Not yet | Stabilize first or carry as internal tooling-only helper. | Useful, but public engine API risk is high. |
| `UiNode` authoring helpers | Not yet | Carry concept only until naming, styling, text, and component policy settle. | Avoid freezing Copeland-specific convenience API. |
| Standard components/theme | Partially | Use as reference; port selected controls for tools when needed. | Good shells for buttons/cards/forms, but editor needs may differ. |
| Text rendering | Not yet | Stabilize text layout/raster contracts first. | Current simple path is transitional and rich text is still evolving. |
| Rich text parser/model | Not yet | Carry concept only; validate against Aurelian docs/tooling needs. | Useful but should not drive core API before text layout is correct. |
| Avalonia host | Defer | Keep as sample/reference; create `Aurelian.Tools.Host` boundary later. | Bootstrap host is useful, but engine core must remain Avalonia-free. |
| Dominatus-specific UI runtime | Defer | Keep upstream/reference; build adapter after Aurelian runtime contracts exist. | Good directional fit, but current types may not be Aurelian's final spine. |
| CLI/tooling | No | Do not carry. | Copeland script CLI is outside Aurelian UI strategy. |
| Copeland demo/sample code | No | Do not carry into core; use only as reference. | Prevent demo assumptions from becoming engine architecture. |

## 8. Vendor/upstream strategy

### 8.1 Options considered

| Option | Evaluation |
| --- | --- |
| A. Vendor selected Machina projects directly into Aurelian | Too early. `Machina.Layout` is tempting, but direct vendoring now risks importing unsettled authoring/text/runtime assumptions and Dominatus package shape before Aurelian core exists. |
| B. Keep Copeland as upstream and reference as submodule/package later | Good default. Preserves ongoing Machina development and avoids forcing Aurelian to stabilize around immature APIs. |
| C. Copy only stable contracts/models and rewrite runtime integration | Best later-stage execution path. Aurelian can keep layout/lowering/hit-test/render-command concepts while owning engine/runtime/render boundaries. |
| D. Do not vendor now; use as design reference until Aurelian core exists | Best immediate path. Avoids dependency-direction mistakes while retaining Machina's architectural lessons. |

### 8.2 Recommended strategy

Use **D now**, with a planned transition toward **B + C** once Aurelian tooling starts.

Near-term:

- Keep Copeland/Machina upstream.
- Treat Machina docs/tests/source as the Aurelian tooling UI reference.
- Do not add Machina as a dependency of Aurelian core.
- Do not copy projects until Aurelian has its core/runtime/render-contract seams.

Later, when Aurelian needs tools UI:

- Prefer a separate package/submodule/reference for stable Machina components, or copy only stable contracts if package boundaries are not ready.
- Start with `Machina.Layout`-like contracts, `UiLoweringResult`-like metadata, `UiHitTestIndex`-like hit testing, and render-command extraction concepts.
- Rewrite adapters for Aurelian's Dominatus-native runtime and render snapshot/command-plan interfaces rather than importing the whole current pipeline.
- Keep Avalonia under tooling host packages only.

Decision drivers:

- **Code maturity**: layout/lowering/hit-test/raster proofs are promising; authoring/text/host/runtime are still moving.
- **Dependency cleanliness**: layout is clean; pipeline and Dominatus/raster bridge import many projects and vendored Dominatus types.
- **API stability**: current docs include active roadmaps and partial/deferred items; do not freeze as Aurelian public API yet.
- **Text risk**: current text is sufficient for debug/presentable artifacts, not yet for editor-grade correctness.
- **Avalonia risk**: sample host is useful but should remain outside core.
- **Dominatus benefit/risk**: compatibility is strategically relevant, but must be adapted through Aurelian-owned runtime contracts.
- **Maintenance burden**: wholesale vendoring would create duplicated development pressure while Machina is still evolving in Copeland.

## 9. Proposed Aurelian integration boundary

Suggested package shape:

```text
Aurelian.Core
Aurelian.Runtime
Aurelian.World
Aurelian.Rendering.Contracts
Aurelian.Assets
Aurelian.Shaders

Aurelian.Tools.Ui      // Machina-derived or Machina-dependent
Aurelian.Tools.Host    // Avalonia/desktop host if needed
Aurelian.Editor        // later
```

Boundary answers:

- **Should Machina live under `Aurelian.Tools.Ui`?** Yes. Machina-derived UI concepts should initially live under `Aurelian.Tools.Ui`, because the motivating use cases are inspectors, debug panels, asset tools, and eventual editor work rather than simulation/runtime core.
- **Should it be a separate package dependency?** Prefer yes once stable package boundaries exist. Until then, keep it upstream/reference or copy only stable contracts for a milestone.
- **Should it remain Copeland/Machina upstream and only be referenced by tooling?** Yes for now. That avoids coupling Aurelian core to experimental authoring, text, Avalonia, and current Dominatus package details.
- **Should any Machina concepts enter Aurelian core?** Only very narrow concepts should influence core design: neutral action/event identity concepts, render snapshot/command-plan separation, and possibly shared geometry primitives if Aurelian deliberately wants them. The Machina UI implementation itself should not enter core.
- **Which boundaries must remain one-way?**
  - `Aurelian.Tools.Ui` may depend on Aurelian contracts, but `Aurelian.Core` must not depend on `Aurelian.Tools.Ui`.
  - `Aurelian.Tools.Host` may depend on Avalonia, but `Aurelian.Core`, `Aurelian.Runtime`, renderer/HAL, assets, and shaders must not.
  - UI action routing may call into Aurelian runtime through explicit commands/events, but runtime/world state should not depend on Machina UI types.
  - UI render extraction may target Aurelian render command-plan contracts, but the renderer/HAL should not know about authoring nodes or Avalonia controls.
  - Dominatus integration should be adapter-owned: Machina/Aurelian tools can publish typed UI events into Aurelian/Dominatus runtime, but Dominatus core should not require Machina UI.

## 10. Risks and blockers

| Risk/blocker | Evidence | Impact | Mitigation |
| ------------ | -------- | ------ | ---------- |
| Authoring API instability | `UI`, `StandardUI`, flat authoring, and hosted component paths coexist; roadmap/docs discuss authoring model evolution. | Aurelian could freeze a convenience API that later fights editor/tooling needs. | Mark authoring APIs experimental; stabilize contracts below authoring first. |
| Text rendering WIP/off rendering | Text docs call current simple text transitional; readable bitmap rasterizer is debug-oriented; roadmap defers real text backend, shaping, overflow, and richer layout. | Tools/editor will need accurate measurement, wrapping, clipping, accessibility labels, and font behavior. | Harden text measurement/layout/raster contracts before import; keep debug raster separate from production text. |
| Avalonia coupling | Avalonia dependency is in presenter sample and handles bitmap presentation/pointer events. | If imported incorrectly, Aurelian engine core could inherit desktop UI framework dependencies. | Confine Avalonia to `Aurelian.Tools.Host`; never reference it from core/runtime/renderer contracts. |
| Unclear packaging | Source projects are split, but no stable Machina package plan is evident from local project files. | Direct dependency or submodule choices may be brittle. | Define stable package/vendoring plan around layout/core/runtime/raster/text boundaries. |
| Duplicated Dominatus integration surface | Machina has Dominatus render bridge, raster Dominatus actuation, and sample runtime; Aurelian will have a Dominatus-native spine. | Competing event/actuation surfaces could fragment runtime architecture. | Let Aurelian own Dominatus contracts; adapt Machina UI at the boundary. |
| Raster renderer production ambiguity | Raster docs/tests support deterministic artifacts; roadmap distinguishes raster/debug text from future backends. | Aurelian might mistake a test backend for final tool/editor renderer. | Treat raster as headless/test backend until a production renderer decision is made. |
| UI model mismatch with future Aurelian editor | Machina currently targets simple components and presenter sample needs; Aurelian editor may need docking, virtualization, complex inspectors, editable text, selection, drag/drop, and data binding. | Overcommitting to Machina's current model may require later rework. | Use Machina for lightweight tools first; validate editor-specific needs before making it editor core. |
| Dependency direction mistakes | `Machina.Pipeline` pulls together Dominatus, raster, runtime, layout, core, text; sample adds Avalonia. | Aurelian core could accidentally depend on host/render/sample layers. | Keep layered project boundaries and one-way references; import only narrow contracts. |
| Runtime input incompleteness | Current runtime centers hit testing, pointer mapping, and dispatch table; roadmap lists focus/keyboard/modal questions. | Tool UX will be limited for real editors and asset tools. | Add focus, keyboard, text input, modal, and routing milestones before serious editor adoption. |
| Compact/hard-to-review layout resolver sections | `LayoutDocumentResolver` currently contains dense statements in places despite overall repository readability guidance. | Future stabilization may be harder to review or safely extend. | Refactor for readability before treating layout resolver as long-term imported contract. |

## 11. Recommended Machina stabilization checklist

1. **Stabilize or explicitly mark experimental authoring APIs.** Define which of `UiNode`, `UI`, `UiDocument`/`UiRow`, `StandardUI`, and `StandardView` are public contracts versus probes.
2. **Define stable core document/lowering/layout contracts.** Freeze or version `LayoutRow`, `LayoutDocument`, `ResolvedLayoutDocument`, `UiLoweringResult`, semantics, actions, and style side-channel rules.
3. **Separate neutral render extraction from Dominatus actuation.** Preserve Dominatus support, but expose a renderer-neutral UI command model that Aurelian can adapt to render snapshots/command plans.
4. **Harden text measurement, layout, and rasterization correctness.** Decide the minimum text contract for tools: wrapping, clipping/overflow, alignment, line boxes, font metrics, rich text blocks/inlines, and deterministic measurement.
5. **Clarify the Avalonia host boundary.** Keep the sample useful, but document whether a real `Machina.Host.Avalonia` package is planned and what it may depend on.
6. **Add or preserve snapshot tests for lowering/layout/hit testing/raster frame.** These are the most Aurelian-relevant proofs and should survive any packaging plan.
7. **Package stable Machina components or write an explicit vendoring plan.** Define which projects can be consumed independently without pulling Dominatus/Avalonia/raster/text unless requested.
8. **Add an Aurelian-facing integration sketch.** Document the intended one-way dependencies and adapter seams for `Aurelian.Tools.Ui`, `Aurelian.Tools.Host`, Aurelian runtime events, and render command plans.
9. **Clarify raster backend status.** State whether CPU raster is a test/artifact backend, a reference backend, or a candidate production renderer for tools.
10. **Refactor dense resolver/runtime code before freezing.** Clean, boring source will matter if Aurelian vendors or forks layout code.

## 12. Validation / command log

Commands run for inventory and inspection:

```bash
git status --short
find src -maxdepth 3 -type d | sort
find src -maxdepth 3 \( -name '*.csproj' -o -name '*.sln' -o -name '*.slnx' \) | sort
find tests -maxdepth 3 -type d 2>/dev/null | sort || true
find tests -maxdepth 3 \( -name '*.csproj' -o -name '*.cs' \) 2>/dev/null | sort || true
find docs -maxdepth 3 -type f 2>/dev/null | sort || true
rg -n "record|class|interface|struct|enum|UiRow|LayoutDocument|ResolvedLayoutDocument|UiLoweringResult|MachinaFrame|RasterFrame|HitTest|UiAction|UiSemantics|Text|RichText|Avalonia|Window|Input|Dominatus|IActuationCommand|RenderCommand|Raster|Pipeline|Lowering|Measure|Arrange|Style|Theme" src docs tests -g '*.cs' -g '*.md' || true
rg --files src/Machina.Core src/Machina.Layout src/Machina.Pipeline src/Machina.Runtime src/Machina.Dominatus src/Machina.Renderer.Raster src/Machina.Renderer.Raster.Dominatus src/Machina.Renderer.Raster.Text src/Machina.Standard src/Machina.Cli src/Copeland.Cli src/Copeland.Script | sort
rg --files -g '*.sln*' -g '*.props' -g '*.targets' -g 'global.json' -g '*.csproj' | sort
rg -n "Avalonia|Window|PointerPressed|Input|PresentedImageMapper|MachinaRasterPipeline" samples src tests docs -g '*.cs' -g '*.csproj' -g '*.md'
```

Validation commands:

```bash
test -f docs/reviews/machina-aurelian-vendor-strategy.md
git status --short
```

No .NET build or test command was run because this milestone is docs-only and the requested validation was limited to document existence and repository status. No source, project, or dependency files were changed.
