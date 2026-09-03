# Aurelian renderer-neutral layer compositor M0

Status: implementation report for `AURELIAN-COMPOSITOR-M0`.

## Outcome

Outcome A. `Aurelian.Composition` is a backend- and application-neutral ordered layer host, and TinyFarm is its first direct-host qualification: MonoGame realizes the world pass and Machina.UI produces the HUD/hotbar/inventory overlay presentation consumed by a MonoGame leaf adapter. Layers share only immutable DTOs and compositor contracts.

The TinyFarm bootstrap is intentionally conservative because Machina.UI is not yet treated as battle-tested. The retired hand-rolled MonoGame toolbar is the compatibility oracle: the Machina projection preserves its geometry, colors, border weights, text origins/scales, labels, responsive breakpoints, and interaction behavior. This real application path is meant to expose missing Machina capabilities and renderer discrepancies before broader migration, rather than using the migration as an opportunity to redesign the UI.

## Mandatory infrastructure audit

| Existing type/project | Capability | Reuse? | Needed extension |
|---|---|---:|---|
| `AurelianFrameLoop` / `IPresentationMechanism` | deterministic engine frame/present boundary | yes, unchanged | no heterogeneous presentation-layer graph |
| `Aurelian.Rendering.Contracts.Compositor` | Vulkan plant-output selection and presentation target | yes, unchanged for GPU plants | intentionally not stretched into runtime/UI composition |
| `AurelianHostExtent` | engine lifecycle extent | yes at Aurelian frame boundary | composition needs scale, viewport, and surface capability |
| `Machina.Presentation.Screens` | stable screen IDs and deterministic semantic screen ordering | yes as prior art and for Machina-only screens | no lifecycle, native pass, input focus/capture, or DTO transport |
| `MachinaPresentationFrame` | ordered fill/stroke/text/clip presentation IR | yes unchanged | MonoGame realization adapter only |
| `MachinaPresentationPipeline` | UI lowering, layout, hit-test index, presentation preparation | yes unchanged | TinyFarm semantic projection adapter |
| `UiInputBatch` / `UiHitTestIndex` | neutral UI events and widget hit testing | yes behind Machina adapter | compositor-level runtime routing remains Aurelian-owned |
| `TinyFarmGame` | one MonoGame window, frame loop, world renderer, temporary UI | host and world renderer reused | temporary UI drawing/hit-test code retired |
| `TinyFarmPlayerUiView` / controller | application-owned semantic UI DTO and command behavior | yes unchanged | transport adapter and typed return command DTO |

The similarly named Vulkan compositor remains the owner of multi-plant GPU output composition. Runtime-layer composition is a different responsibility and therefore uses the explicit `Aurelian.Composition` namespace.

## Final contract

- A `LayerId` is stable, normalized, and typed. A descriptor declares explicit `ZOrder`, enabled state, viewport, direct/offscreen presentation mode, and input policy.
- `IAurelianLayer` has only attach, resize, presentation-update, present, input, and detach operations. It has no gameplay or backend vocabulary.
- The compositor updates and presents enabled layers bottom-to-top by `(ZOrder, LayerId, registration sequence)`. Host-only `SetZOrder` and `SetEnabled` mutations cannot be overwritten by a layer refresh.
- M0 qualifies `DirectHostPass`. `OffscreenSurface`, `LayerSurfaceKind.Offscreen`, and output identity establish the future surface seam without GPU-copy machinery.
- The canonical surface contains pixel extent, scale, kind, and full viewport. Pointer coordinates are clipped in host coordinates and translated to layer-local logical coordinates by viewport origin and scale.
- Resize replaces the canonical surface and is delivered in composition order before future input/presentation.
- `LayerPresentationDto` contains only identity, viewport, redraw metadata, surface kind, and optional output identity. There are no native handles.
- `LayerMessage<T>` and `IAurelianLayerMessageReceiver<T>` provide typed same-process DTO transport. `ILayerApplicationMessageSink` carries typed layer-to-application messages. No serializer, reflection dispatcher, delegate payload, or service locator is used.

## Input, focus, and capture law

Pointer routing begins with the capture owner, otherwise proceeds from highest to lowest eligible layer. A hit-test layer can decline, enabling transparent fallthrough; an opaque layer consumes empty space. Keyboard/text begins with the focused layer, then follows top-down fallback. Capture and focus identify layers only; Machina retains widget-level hit testing and control semantics.

The host owns Escape/close and save/load. Pause/play and fast-forward remain semantic TinyFarm UI commands so keyboard and rendered controls have one behavior. Inventory-open makes the UI layer opaque: movement/interact/use input is consumed, layer focus is acquired, and simulation time continues independently.

## TinyFarm qualification

```text
TinyFarm state
  -> TinyFarmFrame --------------------------------> MonoGame world layer (z 0)
  -> TinyFarmPlayerUiView + presentation scalars
       -> TinyFarmPresentationSnapshot
       -> TinyFarmMachinaUiLayer
       -> MachinaPresentationFrame
       -> TinyFarm MonoGame presentation renderer -> UI layer (z 100)

Machina UiAction / normalized key
  -> TinyFarmUiCommandDto
  -> application-owned queue
  -> TinyFarmPlayerUiController / existing intent path
```

The MonoGame host no longer draws or hit-tests HUD, hotbar, inventory, interaction hints, or simulation mode. It only normalizes platform input, applies returned semantic commands, advances the independent simulation host, and asks the compositor to run presentation passes. World drawing remains MonoGame-owned and now fills the complete viewport behind the transparent UI surface.

Core and Runtime do not reference Aurelian composition, Machina, or MonoGame. CLI/LLM/headless paths remain unchanged. The DTO boundary can move to IPC later because payloads contain values and immutable lists, but M0 deliberately pays no serialization tax.

## Backend seams

- Avalonia: the exact next PoC is an offscreen bitmap layer using the already-proven Machina presentation IR and Avalonia `RenderTargetBitmap`/headless Skia path, not a native child surface. Use one immutable text/button/scroll document to measure dispatcher handoff, DPI, focus, clip, and one-frame copy cost. A native-surface or `NativeControlHost` experiment follows only if that copy cost is unacceptable; do not adopt Avalonia ViewModels as application truth.
- Stride: construct a direct-host or render-texture world layer and a matching Machina IR realizer; no compositor contract change is needed.
- Aurelian native: provide a resolved-2D UI layer adapter alongside an Aurelian world layer, or expose both as offscreen outputs to the existing Vulkan plant compositor.
- Unity/Unreal: conceptually fit as native direct/offscreen layer adapters; no dependency or integration is included.

## Qualification

Fake-layer tests cover stable identity, deterministic order and z mutation, visibility, resize, viewport transform, top-down consumption, fallthrough, layer focus, capture/release, keyboard routing, typed messages in both directions, failure attribution, and dependency neutrality. TinyFarm tests cover Machina presentation creation, 2560x1440 and 1280x720 containment, exact hand-rolled toolbar geometry and style compatibility, Machina-owned hotbar hit testing, key/click command parity, inventory suppression, empty-space world fallthrough, and headless dependency independence.

The compatibility projection is deliberately more explicit than the first generic Machina layout and now measures 194,387 bytes and 289.50 microseconds per recomposition at 1280x720. This is recorded, not gated. A native 120-frame sample measured world/recomposition/adapter CPU averages of 49.29/297.81/153.91 microseconds at 1280x720 and 57.56/322.25/168.75 microseconds at 2560x1440. M15 authoritative movement gates remain independent and continue to pass. DPI is explicit in the compositor surface, while the current MonoGame DesktopGL host reports no meaningful per-monitor scale and therefore supplies `1.0`.

The next observed pressure is to cache/reuse Machina lowering for unchanged UI topology and update only values/layout when possible. That is a bounded presentation optimization, not authorization for a new retained UI state graph.

## AURELIAN-COMPOSITOR-M1 hardening

M1 keeps the M0 compositor contract unchanged. Cache ownership is inside the Machina/TinyFarm UI adapter; `Aurelian.Composition` has no Machina or TinyFarm cache vocabulary. The cached objects are derived lowering, resolved layout, hit-test candidates, and backend-neutral presentation operations. They never contain `TinyFarmState`, inventory authority, clocks, resolver state, native handles, or SpriteBatch commands.

Topology is the ordered control hierarchy, control kinds, existing Machina `NodeId`/`UiActionId` identities, parent/order/layout relationships, hotbar slot order, inventory-open panel shape, and—while the panel is visible—ordered inventory product semantic IDs. Money, time, counts, selection, binding availability, labels, status, hints, and simulation mode are values. The fixed eight-slot hotbar is consequently a strong reuse case. Inventory count changes patch a stable row; insertion, removal, reorder, and open/close rebuild deterministically. Inventory row node IDs now derive from the existing product semantic ID instead of row position.

The invalidation law is intentionally small:

```text
same values and topology -> reuse the complete prepared presentation
VALUE_CHANGE            -> patch existing text/style/semantics slots
LAYOUT_CHANGE           -> rebuild measure/layout, presentation, and hit testing
TOPOLOGY_CHANGE         -> rebuild the full Machina projection
```

Surface width, height, scale/DPI, or kind changes are layout changes. The current DesktopGL adapter continues to report scale 1.0. The fixed TinyFarm style/theme has no runtime switch; a future metric-affecting style change must request layout invalidation, while a shape-compatible color/border change can use the value patch. Disabling a compositor layer retains reusable UI state, while the compositor still clears layer focus/capture when the layer itself is disabled. Closing the inventory removes its topology and hit-test entries; pointer release retains the existing capture-release law.

The generic Machina addition is only a fail-closed prepared-value patch. It replaces values for already-existing text, style, and semantic IDs while retaining geometry and hit-test candidates. A missing node or any fill/stroke operation-shape change throws and requires normal preparation. There is no diff engine, scheduler, component lifecycle, reactive graph, or application-maintained version counter.

### Measured M0 attribution and M1 result

The pre-change 1280x720 closed-inventory workload was reproduced at 194,387 B and 294,570 ns per recomposition. Independently isolated stages (500 warmed iterations) measured as follows; temporary collections and input metadata remain included in the stages that own them rather than being estimated separately.

| Stage | ns/recomposition | B/recomposition | Rebuild required every frame? |
|---|---:|---:|---|
| TinyFarm compatibility + Machina authoring construction | 9,413 | 24,168 | no |
| Machina lowering, including semantic/action metadata | 25,764 | 30,280 | no |
| layout tree construction | 99,456 | 69,579 | no |
| layout resolution/measure placement | 61,927 | 31,768 | no |
| presentation IR lowering | 76,323 | 37,088 | no |
| hit-test index generation | 9,523 | 1,344 | no |
| deterministic text/glyph measurement sample | 547 | 0 | only when layout requires it |
| normalized input hit routing | 157 | 72 | input events only |
| backend-neutral adapter dispatch sample | 338 | 32 | realization frames only |

Text measurement is not the material allocator. TinyFarm text is explicitly placed and the current bitmap realization does not wrap or clip from measured string width, so value patches safely retain its qualified rectangle. Layout/scale/topology changes still run normal measurement. Adapter-native resources were already backend-local (`Texture2D` pixel and SpriteBatch/font realization); M1 adds no string texture cache.

| Metric | M0 | M1 unchanged values | M1 value update |
|---|---:|---:|---:|
| ns/recomposition | 294,570 | 60 | 18,130 |
| B/recomposition | 194,387 | 0 | 14,712 |
| topology builds / 120 stable frames | 120 | 1 | 1 |
| layout builds / 120 stable frames | 120 | 1 | 1 |
| presentation lowers / 120 stable frames | 120 | 1 | 1 |
| hit-test builds / 120 stable frames | 120 | 1 | 1 |

The exact M0 repeated-snapshot workload therefore removes all steady-state allocation after the cold frame. Alternating money values still reduce allocation by 92.4% and CPU by 93.8%, while preserving the same prepared layout and hit geometry. Cold structural rebuilding remains the ordinary M0 path and its isolated stage total is approximately 282 microseconds and 194 KiB on this machine.

The hand-rolled MonoGame toolbar remains **KEEP TEST-ONLY** because it still uniquely fixes geometry, colors, borders, labels, uppercase hint behavior, and click-target compatibility. It is not a runtime authority.

### Next renderer-system pressure

M1 does not begin renderer work. The recommended next bounded milestone is `AURELIAN-NATIVE-VULKAN-BACKEND-M0`: audit and qualify a selectable end-to-end native backend using one existing world/presentation workload. Before implementation it must answer ownership and capability questions for 2D sprite/quad realization, text/glyph rendering, render target and surface lifecycle, camera/viewport mapping, texture/resource upload and lifetime, batching, the composition-layer adapter, the SDSL-V shader path, and frame synchronization. It must preserve Machina presentation IR and `Aurelian.Composition` as upstream-neutral contracts.
