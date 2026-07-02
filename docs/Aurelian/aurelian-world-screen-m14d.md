# Aurelian World Screen M14d

## Purpose

M14d wraps the existing visible-triangle Vulkan runtime path as a Presenter screen on the semantic `world` layer.

The new runtime path is:

```text
Silk.NET Presenter backend
  -> PresenterScreenStack
      -> VisibleTriangleWorldScreen on ScreenLayers.World
  -> Aurelian Vulkan render path
  -> compositor passthrough
  -> present
```

This keeps Presenter as the owner of the screen stack, window, input pump, and present lifecycle while letting Aurelian world output behave like just another screen in the stack.

## Scope

M14d implements:

- `VisibleTriangleWorldScreen` in `samples/Aurelian.VisibleTriangle`
- `VisibleTrianglePresenterScreenStack` as the sample-local stack seam
- a collection-expression `ScreenLayerOrder`
- runtime wiring so the sample now runs through `PresenterScreenStack`

M14d does **not** implement:

- Machina HUD or overlay composition
- Vulkan UI rendering
- shader compiler default changes
- a `VD-MIR` default switch
- Copeland package extraction
- Slang/PTX backends
- shader/kernel split
- Oblivion integration

No Machina HUD or overlay is implemented yet.

## Why the seam stays sample-local

`IPresenterScreen` in M14c is intentionally metadata-only:

- `Layer`
- `IsVisible`

M14d keeps that neutral shape intact. Instead of pushing Vulkan callbacks into `IPresenterScreen`, the sample adds a narrow world-screen wrapper plus a sample-local runner that:

1. creates the semantic layer order
2. builds a `PresenterScreenStack`
3. registers `VisibleTriangleWorldScreen`
4. selects visible screens in composition order
5. runs the existing `AurelianFrameLoop` path through the visible world screen

That proves the stack contract without overdesigning a universal render-screen protocol before Machina enters the picture.

## Layer declaration

The sample uses collection-expression syntax directly:

```csharp
ScreenLayerOrder order =
[
    ScreenLayers.Background,
    ScreenLayers.World,
    ScreenLayers.Hud,
    ScreenLayers.Debug,
    ScreenLayers.Cursor,
];
```

That declaration is now part of the real sample implementation path rather than only test/demo code.

## Runtime behavior

The visible triangle command remains the golden path:

```powershell
dotnet run --project samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -- --presenter silk --frames 120
```

The sample still:

- creates the Presenter/Silk.NET backend
- prepares Vulkan and the offscreen triangle
- starts `AurelianEngine`
- starts `AurelianRuntimeSession`
- acquires/presents per frame
- dispatches the compositor passthrough

The change is that the world render path now sits behind Presenter screen-stack registration instead of bypassing it.

## Relationship to M14e

M14d is the proof that the world layer path is real.

M14e treats that proof as closeout/handoff input only.

The screen model does not need redesign for the closeout, but M14e also does not add Machina HUD/overlay screens, a general bridge, or any new render contract. Future Machina-focused work can build on this seam later without reopening the current Aurelian migration arc.
