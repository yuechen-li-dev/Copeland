# Machina.UI renderer-neutral presentation architecture

Status: current boundary after AURELIAN-CHKPT-M0.

## Purpose and ownership

Machina.UI owns reusable UI semantics, authoring, layout, interaction mechanics, normalized UI input, and renderer-neutral presentation operations. It does not own application/game state, world rendering, window lifecycle policy, or a particular graphics API.

```text
application semantic UI projection
              |
              v
Machina.Core / Layout / Runtime / Presentation
  controls + semantics + layout + hit testing + interaction
              |
              v
renderer-neutral presentation frame
              |
              v
backend adapter + host window
```

## Current-state audit

| Layer | Current implementation | Renderer-neutral? | Authority |
|---|---|---|---|
| Semantic controls | `UiNode`, flat `UiDocument`/`UiRow`, Standard components, `UiSemantics`, `UiActionId` | yes | Machina |
| Layout | frame specs, stack/grid/anchor placement, resolved documents/trees, text measurement seam | yes | Machina |
| Interaction | `UiHitTestIndex`, half-open bounds, deterministic overlap order, dispatch and scrollbar helpers | yes | Machina/integration state |
| Input records | ordered pointer/key/text/resize/close `UiInputBatch` | yes | platform adapter normalizes; Machina routes |
| Presentation IR | fill rectangle, stroke rectangle, positioned text, push/pop rectangular clip | yes | Machina.Presentation |
| Text realization | deterministic measurement and CPU/font tooling exist | mostly; backend still chooses actual glyph rendering | split |

`MACHINA-TEXT-CONFORMANCE-M0` sharpens that split. Avalonia is a test/tooling-only
external layout and raster oracle. Machina owns the immutable `MachinaGlyphRun`, whose
line baselines, token anchors, glyph origins, advances, and plane bounds are shared by
DirectOutline and MSDF. Backend atlas rectangles, padding, UVs, and texture handles
cannot affect layout. Production packages do not reference Avalonia, and the current
`ITextMeasurer` migration remains a separate decision after conformance.
| Focus | semantic and presenter-local behavior exists, but no complete general focus manager/control lifecycle | partial | integration/Machina |
| Platform/window | samples and integration hosts | no | backend host |
| Backends | raster/reference and Aurelian integration paths; no TinyFarm MonoGame adapter | partial | adapter projects |

Machina already separates semantic UI, layout, interaction, and presentation. The missing game seam is not a new UI model; it is a bounded backend/host adapter.

## TinyFarm temporary UI migration map

| TinyFarm concern | Current reusable semantic layer | Temporary MonoGame realization | Migration |
|---|---|---|---|
| HUD/world status | simulation/frame/player UI projection | bitmap text and rectangles | project to Machina document; translate frame operations |
| Hotbar | `PlayerUiModel`, typed slot/binding/selection | drawing, number-key/pointer bounds | Machina controls/actions; return `SelectHotbarSlotIntent` |
| Inventory | authoritative stacks plus unsaved open/focus state | panel layout/drawing/hit areas | keep truth in TinyFarm; keep open/hover/focus presentational |
| Interaction/tool/cook/attack hints | semantic target and required-capability strings | direct text placement | Machina text/card semantics |
| Target presentation | typed frame target/object views | colors/labels in world renderer | world stays MonoGame; UI annotation becomes Machina |
| Input suppression | human controller state and edge routing | MonoGame key/mouse polling | host normalizes once; focus router chooses game or UI path |

The application projection is reusable. The hand-coded geometry, primitive drawing, text drawing, and pointer hit testing are temporary backend realization and duplicate Machina capability.

## Smallest adapter contract

The adapter consumes only a prepared presentation frame and normalized host events. A concrete interface need not be introduced before the first implementation, but the contract is:

```text
inputs:
  surface pixel size and scale
  viewport / coordinate transform
  ordered MachinaPresentationOperation sequence
  normalized pointer, wheel, key, text, resize, close events

outputs:
  executed fill/stroke/text/clip operations in order
  UiInputBatch in Machina root coordinates
  focus/capture/lifecycle observations for the host
```

Required drawing primitives are solid fill, rectangular stroke, positioned text, and rectangular clip stack. Z-order is operation order. The host composes world first and UI second unless an explicit layer contract says otherwise. Measurement is supplied through Machina's `ITextMeasurer`; a backend text realizer must agree closely enough that layout does not drift.

The adapter must contain no scene, inventory, hotbar, combat, or gameplay types. It may contain backend texture/font handles, viewport transforms, and device resources.

## Input and focus law

One host owns platform callbacks and constructs one ordered input batch. It normalizes coordinates and key/button identities once. Machina hit testing and interaction state consume UI events; the game controller consumes events not captured/suppressed by UI focus policy.

```text
platform event stream
  -> host normalization
  -> shared focus/capture decision
      -> Machina interaction -> UiAction -> application typed intent
      -> human game controller -> application typed intent
```

Gameplay never depends on simulated keyboard/mouse. LLM, replay, Dominatus, and human paths meet at the typed intent/resolver boundary.

## Same-window composition

```text
authoritative state
  +-> world projection -> MonoGame | Stride | Aurelian-native world pass
  +-> semantic UI -> Machina frame -> matching UI adapter

host window:
  world pass -> UI pass -> present
  input callbacks -> normalized batch -> focus router
```

Machina remains unchanged when the world backend changes. Each backend needs a thin host bridge because surface/device/text/input APIs differ; the Machina presentation and input contracts remain common.

## Backend capability matrix

| Concern | MonoGame | Stride | Aurelian Native | Avalonia |
|---|---|---|---|---|
| World rendering | qualified for TinyFarm | capable, not integrated here | low-level Vulkan/raster foundations; TinyFarm path incomplete | custom drawing possible, not a game-world target |
| UI rendering | current temporary TinyFarm drawing | possible adapter | presentation adapter incomplete | native strength |
| Text | SpriteFont/temporary bitmap path | engine text facilities | Machina font and raster work exists; native bridge incomplete | mature layout/shaping/control text |
| Input | current TinyFarm host polling | engine input available | window/input pump exists at infrastructure level | routed input and focus available |
| Same-window composition | proven manually; Machina bridge missing | feasible, unqualified | target architecture, unqualified end to end | feasible if Avalonia owns window or via native/offscreen bridge; proof required |
| Offscreen surface | render targets available | render textures available | explicit image/texture machinery available | `RenderTargetBitmap`/headless Skia are available with lifecycle constraints |
| Accessibility | no built-in semantic bridge | limited/host-specific | none qualified | strongest option through controls/automation peers |
| Desktop controls | no | no | no | yes |

Capability is intentionally asymmetric. A world renderer need not become a desktop control toolkit, and Avalonia need not become the world renderer.

## Avalonia feasibility

### Finding

Avalonia is feasible as an optional desktop control realizer or desktop alternative, conditionally feasible as an offscreen compositor, and high-risk as a universal same-window backend for arbitrary game renderers. It should not become Machina's semantic state model.

Current APIs provide custom `DrawingContext` rendering, `RenderTargetBitmap`, a headless platform with Skia for in-memory layout/rendering, `EmbeddableControlRoot`, `NativeControlHost`, routed input, focus, text, controls, and automation/accessibility. Those capabilities answer “can it render and host controls?” positively. They do not by themselves provide a zero-copy game texture bridge or eliminate native-child airspace, device-sharing, dispatcher, DPI, and focus integration.

### Candidate roles

| Role | Result |
|---|---|
| Full Machina backend | possible but duplicates Machina layout/control realization; not preferred for the first adapter |
| Control-library realizer | useful for desktop-heavy screens, text entry, accessibility; semantic action mapping required |
| Offscreen compositor | technically plausible with headless Skia/bitmap capture; likely copy/latency cost and lifecycle work |
| Desktop-only alternative | strongest near-term fit: Avalonia owns the desktop window and projects application semantics |

### Hosting and threading constraints

- Avalonia controls require its property/layout/render lifecycle and UI dispatcher. Control creation, mutation, layout, focus, and rendering must be marshalled to the Avalonia UI thread.
- `RenderTargetBitmap` is a software capture path; current documentation says a normal control capture requires attachment to a visible window, while truly windowless rendering uses the headless platform with Skia.
- `EmbeddableControlRoot` provides a TopLevel intended for embedding, but a platform implementation/host must still provide rendering and input services.
- `NativeControlHost` can place a native game child surface in an Avalonia window, but native-child airspace affects clipping, overlays, and composition. It is not a portable promise of seamless shared rendering.
- MonoGame, Stride, and Aurelian use different native/device/surface lifecycles, so each needs a thin host bridge even if the semantic adapter is shared.

### State ownership boundary

Safe Avalonia-local state:

- hover and pressed realization;
- focus realization and caret/selection state;
- text shaping, glyph, template, and render caches;
- animation clocks that are presentation-only;
- scroll offsets when they do not affect application semantics;
- native control handles and dispatcher work queues.

Must remain application/Machina state:

- inventory and item ownership;
- selected hotbar slot because it changes gameplay capability;
- game/world/simulation state and time;
- current semantic screen/selection when it affects actions or persistence;
- validation, combat, navigation, and resolver results.

Data flow is one-way projection plus typed actions:

```text
TinyFarm state -> semantic UI projection -> Machina -> Avalonia realization
Avalonia event -> UiAction -> TinyFarm typed intent -> resolver
```

No bidirectional ViewModel graph becomes application authority. Avalonia binding may be used internally to realize a projected immutable view if it is discarded/rebuilt and cannot mutate gameplay truth.

### Smallest proof of concept

Build a desktop-only, non-gameplay proof with one immutable Machina document containing text, a button, and a scroll region. Realize it in an `EmbeddableControlRoot` or normal Avalonia window, route pointer/key input back to `UiActionId`, and capture one deterministic frame. Measure layout parity, one-frame copy cost, dispatcher handoff, DPI transform, focus, and clipping. Then test one native game child surface with UI above it. Do not begin with full TinyFarm or MVVM.

Primary feasibility references consulted on 2026-09-03:

- [Avalonia custom rendering and RenderTargetBitmap](https://docs.avaloniaui.net/docs/graphics-animation/custom-rendering)
- [Avalonia headless platform and Skia frame capture](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform)
- [Avalonia threading model](https://docs.avaloniaui.net/docs/app-development/threading)
- [Avalonia native platform interop and NativeControlHost](https://docs.avaloniaui.net/docs/app-development/native-interop)
- [EmbeddableControlRoot API](https://api-docs.avaloniaui.net/docs/T_Avalonia_Controls_Embedding_EmbeddableControlRoot)

## Ownership table

| Concern | Owner |
|---|---|
| Scene/world state | application / Aurelian world contract when adopted |
| World realization | Aurelian runtime plus selected world backend |
| UI semantics | application projection plus Machina semantic types |
| UI layout | Machina |
| Focus and hit testing | Machina/integration policy; backend realizes native focus |
| Input normalization | host/backend adapter |
| Presentation IR | Machina.Presentation |
| Backend rendering | adapter/backend |
| Window lifecycle | host/Aurelian application shell, not Machina |

## Decision

The MonoGame adapter seam is now qualified by AURELIAN-COMPOSITOR-M0. TinyFarm semantic DTOs lower through the unchanged Machina pipeline; Machina owns layout and hit testing; a TinyFarm MonoGame leaf realizes the resulting presentation operations after the world pass. `Aurelian.Composition` owns layer-level focus, capture, ordering, resize, and fallthrough. Keep Stride and Aurelian-native as subsequent adapters when their end-to-end hosts demand them. Keep Avalonia as a focused proof candidate for desktop controls/accessibility/offscreen feasibility. Do not make Avalonia, MonoGame, Stride, or Vulkan types visible to semantic application UI.

## M1 prepared-value reuse law

AURELIAN-COMPOSITOR-M1 adds one bounded Machina primitive: `MachinaPreparedPresentationUpdater.ApplyValues`. It accepts stable existing `NodeId` slots for text, style, and semantics and produces an updated backend-neutral prepared presentation while sharing the layout document and resolved geometry. The hit-test index shares its immutable geometry/action candidates and reads the updated semantic dictionary. Missing IDs or operation-shape changes fail deterministically and instruct the caller to perform a normal layout/topology rebuild.

This is not reconciliation. It cannot insert, remove, reorder, or infer controls; it has no component lifecycle or state graph. The application adapter derives topology from its semantic DTO and chooses among complete reuse, value patch, layout rebuild, and topology rebuild. Existing Machina IDs remain the only identity scheme.

TinyFarm proves the law with a fixed eight-slot toolbar, changing HUD text and selection, an inserted/removed inventory row, resize, and scale invalidation. Stable repeated frames measure 0 B/update after the cold frame; alternating dynamic values measure 14,712 B and 18.13 microseconds versus M0's 194,387 B and 294.57 microseconds. Text measurement itself measured 0 B in the bounded deterministic sample, so no font engine or rendered-string cache was added.
