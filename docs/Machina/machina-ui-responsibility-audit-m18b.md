# Machina.UI Responsibility Audit — M18b

> M18c status: Oblivion model, persistence, UI projection, application state/effects, docs composition, and the canonical fixture have been extracted. Presenter now references the first-class stack. See [the host-boundary record](machina-oblivion-host-boundary-m18c.md).

## Executive finding

Machina.UI is the reusable native C# UI platform: semantic authored views, typed style, layout, standard components/text, input and dispatch contracts, frame preparation, and backend-neutral presentation output. Machina Presenter is a development application and palette. Oblivion is a first-class product that Presenter may host. The reusable Machina projects contain no Oblivion source dependency today, but almost the entire Oblivion product, persistence, UI, and application seam is incorrectly housed in `samples/Integrations/Machina.Presenter.Sample`.

**Outcome A — Boundary is clear.** M18c can extract the product directly. M18b performs no production refactor because the highest-value cleanup crosses assembly ownership and would cease to be “small.”

## Current topology

`Machina.UI.slnx` contains eight production projects, eight primary test projects, and the shared `Machina.Testing` helper. `Machina.UI.Slow.slnx` contains the two environment/diagnostic font suites. `JointTaskForce.slnx` contains Copeland, Machina.UI, Aurelian, the Aurelian.Machina bridge, selected samples, and tests; it does **not** currently include `Machina.Presenter.Sample`. `Copeland.slnx` is compiler-only after JTF-M0. `Copeland.Slow.slnx` no longer exists. `Aurelian.slnx` is the current Aurelian boundary solution.

There is no current `Machina.Rendering.Contracts` project. Backend-neutral UI output is `Machina.Presentation`; backend rendering contracts are in `Aurelian.Rendering.Contracts`, consumed through `Aurelian.Machina`.

## Project inventory

| Current project/path | Actual role | Classification | Recommended owner/disposition | Leakage/name finding | Timing | Confidence |
|---|---|---|---|---|---|---|
| `src/Machina.UI/Machina.Core` | authored `UiNode`/`UiDocument`/`View`, styles, semantics, actions, lowering | `MACHINA_PLATFORM` | keep; canonical semantic authoring/core | correctly renderer/product neutral; name is accurate | keep as-is | high |
| `src/Machina.UI/Machina.Layout` | frames, rows, compiler/resolver, resolved tree | `MACHINA_PLATFORM` | keep | pure layout realization beneath authoring | keep as-is | high |
| `src/Machina.UI/Machina.Runtime` | typed input batches, hit testing, presented-image mapping, generic dispatch | `MACHINA_PLATFORM` | keep; session interaction primitives may grow here only when generic | no product leakage; “Runtime” is broad but current contents are coherent | keep as-is | high |
| `src/Machina.UI/Machina.Standard` | standard controls, themes/styles, restricted rich text and layout | `MACHINA_STANDARD_UI` | keep | reusable component/text policy; not Markdown or Oblivion | keep as-is | high |
| `src/Machina.UI/Machina.Presentation` | backend-neutral frame/operations/builder, frontend lifecycle routing, screen composition | `MACHINA_PLATFORM` with mixed naming | retain frame contracts; reconsider placement/names of frontend routing and presenter-named screen APIs later | no product code, but `PresenterScreen*` naming grants presenter vocabulary to reusable infrastructure | later cleanup | high |
| `src/Machina.UI/Machina.Pipeline` | deterministic authoring-to-lowering/layout/hit-test/presentation orchestration | `MACHINA_PLATFORM` / `GENERIC_RUNTIME` | keep as narrow preparation facade | cohesive today; “Pipeline” is generic but implementation is only Machina presentation preparation | keep, later naming review | high |
| `src/Machina.UI/Machina.Fonts` | font contracts, generation, artifacts, TOML, CPU/reference rendering | `MACHINA_PLATFORM` plus diagnostic/reference-rendering implementation | keep; later separate reference/tooling realization only if consumers require it | no Oblivion leakage; project is much broader than font contracts | later cleanup | medium |
| `src/Machina.UI/Machina.Fonts.Tooling` | diagnostic export, overlays, layer composition | `PLAYBACK_TEST_TOOLING` / development tooling | keep out of product dependency graphs | tooling is correctly named and isolated | keep as-is | high |
| `samples/Machina.UI/Machina.ComponentGallery.Sample` | reusable component gallery and host proof | `PRESENTER_DEVTOOL` | presenter/devtools sample | valid sample; not product | keep as-is | high |
| `samples/Integrations/Machina.Presenter.Sample` | Avalonia host, palette/navigation, export/diagnostics, playback **and all Oblivion layers** | `PRESENTER_DEVTOOL` plus misplaced product | retain only devtool/host/playback; extract Oblivion | legacy “sample owns product” structure is the central violation | M18c | high |
| `src/Copeland/Copeland.Markdown` | Markdown compiler and `DocumentMir` | `COMPILER/DOCUMENT_INFRA` | remain independent; Oblivion.App/UI adapts compiled projections | must not become product model | keep as-is | high |
| `src/Integrations/Aurelian.Machina` | translates `MachinaPresentationFrame` to Aurelian rendering contracts | `MACHINA_PLATFORM` backend adapter/integration | keep as consumer-owned bridge | correct dependency direction | keep as-is | high |
| `Aurelian.Rendering.Contracts` | backend render/presentation vocabulary | external renderer contract, not Machina UI product | keep in Aurelian | confirms that Machina has no current `Rendering.Contracts` assembly | keep as-is | high |

## Ownership inventory: significant families

| Current family/path | Current role | Category | Recommended owner | Dependency concern | Relocation | Confidence |
|---|---|---|---|---|---|---|
| `Machina.Core.Authoring.UI`, `View`, `Row` | semantic tree/flat authoring | `MACHINA_PLATFORM` | Machina.Core | none | keep as-is | high |
| `UiNode`, `UiDocument`, `UiRow`, node families | authored view contracts | `MACHINA_PLATFORM` | Machina.Core | Core references Layout frame types by design | keep as-is | high |
| `UiStyle`, `TextStyle`, `Theme`, semantics | typed presentation semantics | `MACHINA_PLATFORM` | Machina.Core | no product terms | keep as-is | high |
| `UiAction`/`UiActionId` | UI event identity | `MACHINA_PLATFORM` | Machina.Core | must remain projection identity, not product action model | keep as-is | high |
| lowering/snapshot writers | authored-to-layout input conversion/diagnostics | `MACHINA_PLATFORM` | Machina.Core | diagnostics are reusable | keep as-is | high |
| frames/rows/arrangers/resolvers | layout realization | `MACHINA_PLATFORM` | Machina.Layout | must remain below product/UI | keep as-is | high |
| Standard controls/theme/text | reusable compositions and text policy | `MACHINA_STANDARD_UI` | Machina.Standard | Markdown must adapt into it, not merge into it | keep as-is | high |
| input batches/events/hit index | canonical backend-neutral input | `MACHINA_PLATFORM` | Machina.Runtime | product routing must consume actions, not coordinates | keep as-is | high |
| generic dispatch table/transitions | immutable state transition helper | `MACHINA_PLATFORM` | Machina.Runtime | application may use but must own its state/actions | keep as-is | high |
| `MachinaPresentationFrame`/operations/viewport | backend-neutral render intent | `MACHINA_PLATFORM` | Machina.Presentation | no backend or product dependency | keep as-is | high |
| `MachinaPresentationFrameBuilder`/text builder | resolved UI to presentation output | `MACHINA_PLATFORM` | Machina.Presentation | currently depends on Standard metadata, appropriately | keep as-is | high |
| `MachinaFrontendInputRouter`/messages | lifecycle input translation | `GENERIC_RUNTIME` | Machina.Runtime or a future neutral Hosting namespace | placement in Presentation is debatable; behavior is generic | later cleanup | medium |
| `IPresenterScreen`, `PresenterScreenId`, `PresenterScreenStack` | generic metadata-only screen/layer composition | `GENERAL_REUSABLE_UI` with `LEGACY_COMPAT` naming | keep reusable; later neutralize “Presenter” naming compatibly or move to Hosting/Runtime | used by Aurelian integration; moving to devtool would invert a valid generic use | later cleanup | medium-high |
| `ScreenLayer*`, `Layer`, conventional world/HUD/debug slots | generic composition policy | `GENERAL_REUSABLE_UI` | reusable hosting/composition namespace | optional engine-flavored defaults, no product code | later cleanup | medium |
| `MachinaPresentationPipeline`/prepared result | full preparation facade | `GENERIC_RUNTIME` | Machina.Pipeline | project name too broad but responsibility cohesive | keep as-is | high |
| font model/generation/artifacts | font infrastructure | `MACHINA_PLATFORM` | Machina.Fonts | reference rendering is implementation-heavy | later cleanup | medium |
| font diagnostics/export/layers | development diagnostics | `PLAYBACK_TEST_TOOLING` | Machina.Fonts.Tooling | product must not reference | keep as-is | high |
| presenter navigation model/catalog/state/dispatch | palette shell and navigation | `PRESENTER_DEVTOOL` | Presenter | state currently also stores Oblivion selection/scroll/effects | split in M18c | high |
| presenter navigation renderer/session/exporter | devtool shell rendering/export | `PRESENTER_DEVTOOL` | Presenter | may host Oblivion.UI but product must not call back | keep/split M18c | high |
| adaptive shell and keyboard routing | current presenter host/session policy | `PRESENTER_DEVTOOL` plus some Oblivion UI policy | Presenter for host adapters; Oblivion.UI for product shell mode/pane state | names/logic are intertwined | M18c | high |
| `PresenterCard`/layout helper | generic-looking card composition used by presenter and Oblivion | `UNCLEAR` / `COMPAT_OR_DELETE` | either promote minimal reusable pieces to Machina.Standard after evidence or duplicate a small Oblivion-local composition | “Presenter” API is a product dependency today; do not promote wholesale | M18c decision | medium |
| scrollbar geometry/state machine | generic local interaction logic under presenter names | `GENERAL_REUSABLE_UI` candidate | Machina.Runtime/Standard only if a second real consumer justifies; otherwise Oblivion.UI/Presenter-local | premature promotion risk | later/M18c localize | medium |
| workspace/page/card records | durable product truth mixed with realization | `PRODUCT_MODEL` | Oblivion.Model | `PresenterPageId`, paths, `DocumentMir`, preview data leak inward | M18c | high |
| JSON/TOML DTO/read/write/load/validate | persistence contract and safe materialization | `PRODUCT_PERSISTENCE` | Oblivion.Persistence | currently ships from WinExe sample and directly builds mixed model | M18c | high |
| sample workspace JSON/TOML/Markdown assets | real persisted product fixture | `PRODUCT_PERSISTENCE` fixture | Oblivion.Persistence/UI tests or Oblivion.App sample workspace | sample-relative path assumptions | M18c | high |
| `OblivionCardHandler*` | mixes model normalization, views, actions, and effects per kind | `PRODUCT_UI` plus application action policy | Oblivion.UI and Oblivion.App | one interface spans domain, UI, and effects | split in M18c | high |
| card runtime/compact/inspector/built view records | derived projections | `PRODUCT_UI` | Oblivion.UI | currently beside durable records | M18c | high |
| card renderers/Markdown renderer/reading style | Machina-authored product UI | `PRODUCT_UI` | Oblivion.UI | direct PresenterCard dependency; static caches/diagnostics | M18c | high |
| inspector/interaction maps/hit targets | product UI input projection | `PRODUCT_UI` | Oblivion.UI | layout coordinates appropriately remain here, not Model | M18c | high |
| workbench catalog/docs dogfood catalog | composition of product pages plus fixtures | `PRODUCT_UI` plus `PRESENTER_DEVTOOL` fixture | split Oblivion.App/UI from Presenter fixture catalog | 2,300-line mixed static catalog | M18c | high |
| card action declaration/descriptor/invocation | overlapping product/application/UI action models | `PRODUCT_MODEL` / `PRODUCT_UI` | split Model/App/UI | duplicate meanings and strings | M18c | high |
| effect request/result/router/effect state | deferred application effects and transient state | product application/runtime | Oblivion.App | stored inside PresenterNavigationState and dispatched by presenter | M18c | high |
| selected/expanded/scroll/raw-source state | session state | `PRODUCT_UI` session | Oblivion.UI/App session store | currently inseparable from presenter navigation | M18c | high |
| playback parser/runner/suite/output/scenarios | deterministic development regression tool | `PLAYBACK_TEST_TOOLING` | Presenter/DevTools, with product scenarios supplied by Oblivion tests | currently compiled into product-host sample; xUnit director was deleted | keep, restore tests in M18c | high |
| Avalonia input/window/PNG/export adapters | host and development output | `PRESENTER_DEVTOOL` | Presenter | product must depend only on host contracts | keep as-is | high |
| direct-outline proof cards/renderers | rendering diagnostics/fixtures | `PRESENTER_DEVTOOL` | Presenter or font tooling | not product UI | keep as-is | high |

## Current mixed-ownership findings

### Product leakage into Presenter

The central `KNOWN_PRE_M18C_VIOLATION` is assembly ownership: `Machina.Presenter.Sample.csproj` compiles production-grade Oblivion model, persistence, UI, actions/effects, and fixtures alongside Avalonia, Aurelian raster, presenter navigation, export, and playback. Because all code shares `Machina.Presenter.Sample`, Oblivion cannot be referenced without taking a WinExe/devtool/backend dependency.

Specific leaks:

1. `OblivionWorkspacePage.PresenterPageId` embeds devtool navigation identity in loaded product data.
2. `PresenterNavigationState` owns selected card, expansion, all Oblivion scroll offsets, and last effect state.
3. `PresenterNavigationDispatch` invokes product actions and writes effect outcomes.
4. `OblivionCardRenderer` depends on `PresenterCardFrame`/`PresenterCardLayoutHelper`.
5. `OblivionWorkbenchCatalog` combines product page composition, fallback/demo data, state resolution, and presenter page routing.
6. Persistence, Markdown compilation, renderer caches, application effects, and window hosting are one assembly.
7. The sample project owns the only persisted Oblivion workspace fixture and copies it via sample-relative MSBuild paths.

### Presenter/devtool leakage into Machina.UI

No presenter application or Oblivion namespace is referenced by `Machina.Core`, Layout, Runtime, Standard, Pipeline, Fonts, or Fonts.Tooling. The one vocabulary leak is `Machina.Presentation.Screens`: `IPresenterScreen`, `PresenterScreenId`, and `PresenterScreenStack` are semantically generic screen composition but permanently expose a devtool-specific noun. This is not an M18b rename; Aurelian currently consumes the API. Treat the names as compatibility debt and decide a neutral `ScreenStack` migration only with a compatible staged plan.

## Machina.Presentation audit

| Family | Classification | Disposition |
|---|---|---|
| frame, viewport, operation records, validation | `GENERAL_REUSABLE_UI` | keep in Machina.Presentation |
| frame builder and text presentation builder | `GENERAL_REUSABLE_UI` | keep; they are the authoritative backend-neutral output lowering |
| frontend lifecycle messages/router | `GENERIC_RUNTIME` | valid reusable behavior; later consider Machina.Runtime/Hosting because it routes input lifecycle rather than output presentation |
| screen layer keys/order/slots | `GENERAL_REUSABLE_UI` | keep reusable; optional conventional layers are policy defaults |
| presenter-named screen interface/id/stack | `LEGACY_COMPAT` naming over reusable semantics | do not move to Presenter; later introduce neutral names/adapters if worth the compatibility cost |

Machina.Presentation has no Oblivion product leakage and should retain permanent architectural status for backend-neutral presentation frames. Its mixed responsibility is modest: output frame construction, lifecycle frontend routing, and screen composition are three reusable families under one broad name. A future split may improve discoverability, but it is not required for Oblivion extraction.

## Machina.Pipeline audit

`Machina.Pipeline` has only two public families: `MachinaPresentationPipeline` and `MachinaPreparedPresentation`. It performs one generic deterministic sequence:

```text
UiNode/UiDocument
  -> Core lowering
  -> Layout compile/resolve
  -> Runtime hit-test index
  -> Presentation frame
```

Classification: `GENERIC_RUNTIME`. It owns no presenter navigation, Oblivion model, backend adapter, raster output, window, application state, or playback. The old architecture doc describing render commands/raster is stale after JTF-M3d; the current code is presentation-only. Keep the project for M18c. Later rename only if “Pipeline” causes real ambiguity; do not split a two-type cohesive facade to beautify the graph.

## Presenter/devtool boundary

Presenter owns:

- component/typography/layout galleries and fixtures;
- palette sections/tabs and presenter navigation state;
- diagnostics, proof cards, export commands, manifests, and PNG output;
- Avalonia/native host adapters and development input collection;
- playback parsers, runners, reports, and development scenarios;
- development hosting adapters for Oblivion.UI.

Presenter does not own workspace/card truth, persistence, product actions/effects, Oblivion selection/session policy, or product renderers. After M18c its dependency on Oblivion is optional hosting, and no Oblivion project references Presenter.

## Target dependency map

```text
Machina.Layout
      ^
      |
Machina.Core ---> Machina.Standard
      \               /
       \             /
        Machina.Runtime
              \     /
        Machina.Presentation
                 ^
                 |
          Machina.Pipeline

Oblivion.Model                 (no Machina/renderer/compiler dependency)
      ^
      +--- Oblivion.Persistence
      +--- Oblivion.UI -------> Machina.Core + Machina.Standard
                 ^                       + minimal Runtime contracts
                 |
          Oblivion.App -------> Persistence + explicit host/compiler/effect adapters
                 ^
                 |
Machina Presenter / DevTools --> Machina UI and MAY host Oblivion

Aurelian.Machina: Machina.Presentation -> Aurelian.Rendering.Contracts
```

The exact internal Machina project arrows remain as implemented; the rule relevant to M18c is that Oblivion.Model is independent, Oblivion.UI consumes semantic Machina authoring, and Presenter is always an outer development host.

## Architecture guard status

| Check | Current evidence | Status |
|---|---|---|
| Oblivion product code depending on Presenter | same assembly/namespace; renderer uses `PresenterCard*`; state/dispatch use presenter types | `KNOWN_PRE_M18C_VIOLATION` |
| Presenter depending on Oblivion | direct same-assembly construction/hosting | allowed target direction, but assembly co-location prevents enforcing it |
| Machina.UI depending on product namespaces | source search under `src/Machina.UI` finds no Oblivion reference | currently clean and guarded in M18b tests |
| product model depending on layout/rendering | durable records do not directly reference Machina, but mixed `OblivionCardBody` embeds `DocumentMir` and contract file imports Standard theme | `KNOWN_PRE_M18C_VIOLATION`; compiler/UI dependency still crosses model surface |
| sample project containing production product model/UI | all Oblivion implementation and persistence live under sample | `KNOWN_PRE_M18C_VIOLATION` |
| product actions/effects independent of presenter | dispatch/effect state live in presenter state/dispatch | `KNOWN_PRE_M18C_VIOLATION` |
| playback regression under active test discovery | prior xUnit project removed; CLI suite remains executable | `KNOWN_PRE_M18C_VIOLATION` for test topology |

M18b adds a lightweight test that asserts the required docs/manifest and currently enforceable “no Oblivion in Machina.UI production source” rule. It records rather than exempts pre-M18c violations.

## Compatibility hazards

| Risk | Severity | Evidence | Mitigation |
|---|---|---|---|
| format 1 JSON/TOML behavior changes during DTO/model split | `HIGH` | custom readers/writers, stable diagnostics, optional fields, deterministic output | freeze fixtures and round-trip bytes/semantics before move; use persistence DTO adapters |
| product code can only be consumed through WinExe Presenter assembly | `HIGH` | all Oblivion `.cs` files compile in `Machina.Presenter.Sample` with Avalonia/Aurelian references | extract projects atomically and invert host reference |
| product/playback xUnit regression project was deleted | `HIGH` | commit `f3408cd` removed 50 files/15,055 lines including playback director and product tests | use live CLI suite now; restore focused tests in first-class Oblivion/playback test projects during M18c |
| namespace/assembly/type-name changes break consumers and serialized metadata | `MEDIUM` | public records use `Machina.Presenter.Sample`; tests/integrations reference sample assembly | stage namespace adapters/type forwards where needed; search reflection strings; avoid changing persisted discriminators |
| `PresenterPageId` and presenter navigation IDs are embedded in loaded pages | `MEDIUM` | `OblivionWorkspacePage` includes `PresenterPageId` | map product page ID to devtool tab externally; preserve aliases at host boundary |
| sample-relative paths and copied workspace assets | `MEDIUM` | loader fallback and csproj content path assume sample location | introduce explicit fixture/app workspace path and retain old fallback compatibility temporarily |
| effect routing and transient results are coupled to presenter dispatch/state | `MEDIUM` | `PresenterNavigationState.EffectState`, presenter action routing | move invocation/effect reducer to Oblivion.App; presenter translates UI actions only |
| durable source, compiled `DocumentMir`, preview, and diagnostics share one record | `MEDIUM` | `OblivionCardBody` | split source declaration from compiled projection while preserving loader output behavior |
| overlapping card/action/artifact records drift | `MEDIUM` | persistence DTOs, `OblivionCard*`, contract refs/descriptors duplicate concepts | keep DTOs only at persistence edge; add explicit mapping tests |
| generic UI helpers are presenter-named | `MEDIUM` | `PresenterCard*`, scrollbar helpers, screen stack names | promote only evidenced reusable primitives; otherwise localize to Oblivion or Presenter |
| static caches retain file/render data across sessions/tests | `LOW` | workspace loader and Markdown renderer dictionaries/counters | move to app/session-owned cache or retain explicit reset/lifetime policy |
| playback fixture and output paths encode current sample layout | `LOW` | suite TOML and output writer paths | keep compatibility resolver during move; update one canonical manifest atomically |
| Aurelian integrations consume presenter-named screen APIs | `LOW` | `PresenterScreenStack` used outside Presenter | defer rename; use compatibility adapters if neutralized later |
| product fixture claims `machina-sample` identity | `LOW` | workspace manifest title/id | preserve as development fixture; add a product-owned neutral fixture rather than rewriting persisted data silently |

No reflection/type-name coupling was found in the audited Oblivion path, but namespace searches must be repeated immediately before M18c moves.

## Obvious cleanup performed

No production source was moved or renamed in M18b. The apparent “small” changes all crossed the sample/product assembly seam and would either duplicate types or begin the forbidden full extraction. The only implementation addition is a lightweight architecture/doctrine test. This is intentionally zero behavior change.

## Explicit deferred work

- extract the four Oblivion projects and tests described in the product contract;
- split durable content from `DocumentMir`/preview/diagnostics;
- separate product session state from presenter palette navigation state;
- move product action/effect routing out of presenter dispatch;
- decide the narrow fate of `PresenterCard*` and scrollbar helpers based on the extracted code;
- restore xUnit product/playback coverage under current solution topology;
- consider neutral screen-stack names and frontend-router placement only after M18c;
- update stale historical docs only through a separate documentation cleanup; they remain historical evidence, not current contract.

## Validation results

Validation was run serially with the installed .NET 10.0.302 SDK. No SDK installation was required.

| Command/check | Result |
|---|---|
| `dotnet test Machina.UI.slnx -m:1` | passed, 672 tests |
| `dotnet test Machina.UI.Slow.slnx -m:1` | passed, 308 tests |
| `dotnet build Machina.UI.slnx --no-restore -m:1` | passed, 0 warnings/errors |
| targeted `M18bBoundaryAuditTests` | passed, 2 tests |
| canonical M16c playback CLI suite | passed, 14/14 scenarios; temporary output outside repository |
| `dotnet test Aurelian.slnx --no-build -m:1` | passed, 657 tests |
| `dotnet test JointTaskForce.slnx -m:1` | failed in untouched `Copeland.Cli` before completing the solution: duplicate workspace ownership types conflict with `Copeland.TS.Workspace` (`CS0436`, warnings treated as errors) |
| `dotnet test Copeland.slnx -m:1` | reproduced the same unrelated `Copeland.Cli` baseline failure; other reached compiler suites passed |
| `Copeland.Slow.slnx` | not present in current topology; no equivalent slow Copeland solution found |
| former presenter xUnit commands | not runnable because `tests/Machina.UI/Machina.Presenter.Sample.Tests` was deleted in commit `f3408cd`; live CLI playback is the current equivalent |
| `git diff --check` | passed |

The JTF/Copeland failure is not caused by M18b: this change modifies only two docs, one manifest, and one Machina.Core test file. Fixing duplicate compiler workspace types would touch the explicitly out-of-scope Copeland TS lane, so M18b records the baseline instead of disguising it as an architecture cleanup.

## Boundary searches interpreted

- No native automation stack (`NativeAutomation`, `Win32Input`, `SendInput`, Selenium, Playwright, Appium) is present in the native Machina/Oblivion/Presenter path. The repository-wide search finds six unrelated Playwright references in Copeland-TS browser-proof samples.
- No Oblivion pixel-golden comparison API is present. The repository-wide search finds 24 `ImageDiff` references in Machina font reference diagnostics; those are artifact/reference tooling, not an Oblivion screenshot gate.
- Legacy layout names (`GuideFrame`, `EdgeRef`, `LayoutRowVariant`, `UiLength.Proportional`, `DeusMachine`) are absent from current product/UI source.
- No active project/solution reference points into `reference/dominatus` or `vendor/Dominatus`.
- `Copeland.slnx` and the absent `Copeland.Slow.slnx` contain no Aurelian coupling; the current Aurelian boundary is `Aurelian.slnx`.

## M18c exact scope

M18c should extract `Oblivion.Model`, `Oblivion.Persistence`, `Oblivion.UI`, and a small `Oblivion.App`; make Presenter a development host; preserve format 1 persistence; preserve the live playback output; restore focused model/persistence/UI/action/playback tests; and remove sample-local duplicate product definitions only after references are inverted. It should not redesign Machina runtime, execute cards, edit content, alter Copeland TS, move Aurelian, touch VD-MIR, or rename generic Machina assemblies.
