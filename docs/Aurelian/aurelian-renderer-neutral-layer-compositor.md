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
