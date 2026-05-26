# Machina Authoring Model (M2a reset)

## Canonical model

Machina layout remains canonical as flat `LayoutRow[]` with explicit parent ids, explicit frames, and optional arrangers.

Nested `UiNode` declarations are authoring convenience only.

## Placement-first rule

- **Placement uses frames**: `UI.At` (absolute) and `UI.Anchor` (edge constraints).
- **Flow uses stack**: `UI.Row` and `UI.Column` only for true sequential layout.
- **Nesting is optional sugar**.

Do not use `HSpace`/`VSpace` to place major panels on the screen.

## New M2a primitives

- `UI.Surface(...)`: root/layer-like container with independently positioned children; lowers as root row (`RootFrame`) without stack arrange by default.
- `UI.Layer(...)`: non-root independent-position container; children are not implicitly stacked.
- `UI.At(child, x, y, width, height, ...)`: lowers to `AbsoluteFrame`.
- `UI.Anchor(child, left/right/top/bottom/width/height, ...)`: lowers to `AnchorFrame`.

`Surface` width/height are authoring metadata today; pipeline render size remains authoritative.

## Example

Bad (placement-by-spacer):

```csharp
UI.Column(children:
[
    UI.VSpace(24),
    UI.Row(children: [UI.HSpace(72), StandardUI.Card(...)])
])
```

Good (placement-first):

```csharp
UI.Surface(children:
[
    UI.At(
        id: "settings-card-slot",
        x: 72,
        y: 24,
        width: 500,
        height: 292,
        child: StandardUI.Card(...))
])
```
\n\n## M3a flat authoring note\nRow-first UiDocument/UiRow authoring is canonical for top-level screens; nested UiNode trees remain optional sugar.
