# Machina screen composition (JTF-M4a)

## Result

`Machina.Presentation.Screens` owns generic presenter screen composition. Its public API is `IPresenterScreen`, `PresenterScreenId`, `ScreenLayerKey`, `ScreenLayerSlot`, `ScreenLayerOrder`, `ScreenLayers`, `Layer`, and `PresenterScreenStack`.

The contract is intentionally metadata-only:

- `Id` is normalized, stable identity within a stack.
- `Layer` declares the configured composition layer.
- `IsVisible` controls inclusion.

It does not manufacture a frame, pixels, render plans, raster surfaces, input policy, navigation, lifecycle, backend selection, or world ownership. A screen's producer continues to own any content it contributes through its existing presentation boundary.

## Composition semantics

`ScreenLayerOrder` requires declared, unique layer keys. Composition sorts declared layers by numeric order and then normalized key, so equal numeric orders are deterministic. `PresenterScreenStack` rejects an undeclared screen layer and duplicate normalized screen identity. It returns only visible screens, ordered by configured layer and then stable insertion sequence within a layer. Empty stacks produce an empty list.

`ScreenLayers` provides optional conventional slots, including `World`, `Hud`, and `Overlay`; it does not require an application to use them. The integration composition root declares the actual layer order.

## Ownership

```text
Machina.Presentation.Screens -> generic screen metadata and ordering
Aurelian                    -> engine/world/frame-loop behavior
Aurelian.Machina            -> frame-to-resolved-2D bridge
Integration host            -> concrete UI/world screen adapters and stack configuration
```

No Aurelian production project references Machina, and no Machina production project references Aurelian. The existing `Aurelian.Machina` project remains limited to `Machina.Presentation` and `Aurelian.Rendering.Contracts`.

## M4b preparation

This establishes a single screen/layer owner before input reconciliation. It deliberately makes no change to raw input, hit testing, focus, routing, game commands, animation, modal policy, frame loops, raster semantics, or backend selection.
