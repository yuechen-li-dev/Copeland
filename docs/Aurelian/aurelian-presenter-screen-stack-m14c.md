# Aurelian Presenter Semantic Screen Stack M14c

## Purpose

M14c establishes the Presenter-owned semantic screen/layer model that will let Aurelian world rendering and future Machina UI rendering compose in one explicit stack.

Aurelian output should be treated as a Presenter screen/layer, not as a special-case host path.

This milestone is model-first:

- Presenter owns the screen stack and explicit layer ordering
- Aurelian remains the renderer
- Machina remains future UI/screen content
- composition stays semantic and deterministic

M14c does **not** composite Machina over Aurelian yet, and it does **not** change the M14b visible triangle runtime path.

## Why the types live in `Aurelian.Core`

The new types live under `src/Aurelian.Core/Presentation/Screens`.

That placement matches the current seam boundaries:

- `Aurelian.Core` already owns frame-loop and presentation orchestration seams
- `Aurelian.Graphics` stays Vulkan/backend-specific
- `Machina` stays free of Aurelian runtime dependencies for now
- the screen stack model remains presenter-facing rather than renderer-facing

This keeps the model neutral. Nothing in the API assumes Vulkan, Silk.NET, Machina raster output, or a specific compositor graph.

## Layer primitives

M14c adds:

- `ScreenLayerKey`
- `ScreenLayerSlot`
- `ScreenLayers`
- `Layer.At(string name, int order)`
- `ScreenLayerOrder`
- `IPresenterScreen`
- `PresenterScreenStack`

Standard semantic layers are:

- `background` at `0`
- `world` at `100`
- `hud` at `200`
- `overlay` at `300`
- `modal` at `400`
- `debug` at `900`
- `cursor` at `1000`

Layer names are validated, trimmed, and normalized to lowercase. Equality is therefore case-insensitive from the author's point of view.

## Collection-expression authoring

The primary declaration style is direct collection-expression assignment:

```csharp
ScreenLayerOrder order =
[
    ScreenLayers.Background,
    ScreenLayers.World,
    ScreenLayers.Hud,
    ScreenLayers.Overlay,
    ScreenLayers.Modal,
    ScreenLayers.Debug,
    ScreenLayers.Cursor,
];
```

Custom layers remain square-bracket friendly:

```csharp
ScreenLayerOrder order =
[
    ScreenLayers.Background,
    ScreenLayers.World,
    Layer.At("damage-vignette", 250),
    ScreenLayers.Hud,
    ScreenLayers.Debug,
];
```

The declared set is explicit. There is no hidden mutable global ordering state and no raw integer z-index surface as the primary API.

## Ordering rules

`ScreenLayerOrder` preserves declared slots and also computes deterministic composition order.

Rules:

- duplicate layer keys are rejected immediately
- duplicate numeric order values are allowed
- ties on numeric order are resolved deterministically by normalized layer key
- unknown layer lookups throw clear errors naming the missing layer

That means authors can group layers semantically without giving up predictable sort behavior.

## Screen stack behavior

`PresenterScreenStack` takes an explicit `ScreenLayerOrder` and accepts `IPresenterScreen` instances.

Behavior:

- adding a screen on an undeclared layer is rejected
- hidden screens are skipped
- visible screens are returned in composition order
- screens on the same layer preserve insertion order

Example intent:

```csharp
ScreenLayerOrder order =
[
    ScreenLayers.Background,
    ScreenLayers.World,
    ScreenLayers.Hud,
    ScreenLayers.Debug,
];

var stack = new PresenterScreenStack(order);
stack.Add(new FakeWorldScreen(ScreenLayers.World.Key));
stack.Add(new FakeHudScreen(ScreenLayers.Hud.Key));
```

The concrete Aurelian/Machina screens are still future work. For M14d, Aurelian will land in the `world` layer. For M14e, Machina HUD/overlay screens will land in upper layers.

## Relationship to M14b

M14b remains the visible runtime proof:

- Presenter owns window/frame/input/present
- Aurelian renders the visible triangle
- the sample composes them through the existing Vulkan passthrough path

M14c adds the semantic stack model beside that runtime proof. It does not change the existing visible-triangle command, default compiler behavior, or compositor policy path.
