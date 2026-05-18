# Machina.Standard M0a component contract

`Machina.Standard` is the first reusable standard component package above `Machina.Core`.
It is a declaration-only layer: every component produces immutable Machina Core UI nodes that lower through the existing deterministic `UiLowerer` pipeline.

## Relationship to Machina.Core

`Machina.Core` remains the renderer-independent UI declaration layer. It owns the readable `UI.*` authoring surface, immutable `UiNode` records, immutable styles, semantic/action metadata, deterministic lowering, and the text-measurement seam.

`Machina.Standard` composes those Core primitives into a small, reusable component vocabulary. Standard components do not introduce a renderer, event dispatcher, DOM model, CSS model, or Copeland/Dominatus integration. They are ordinary C# declaration helpers that return `UiNode` values.

## shadcn-intent philosophy

M0a uses shadcn/ui as component inventory and concept-art reference only.

The package does **not** port or copy:

- Tailwind classes
- Radix primitives
- React components, hooks, or DOM structure
- CSS selectors or browser behavior
- focus, keyboard, popup, or input-dispatch systems

Instead, Standard re-expresses a very small shadcn-shaped vocabulary in Machina-native terms: Core declarations, immutable style records, semantic metadata, action metadata, and deterministic lowering.

## M0a supported components

M0a intentionally starts small:

- `StandardUI.Button`
- `StandardUI.Card`
- `StandardUI.Badge`
- `StandardUI.Separator`

Out of scope for M0a:

- dialog, popover, dropdown, select, combobox, command, and table
- forms and validation
- focus and keyboard navigation
- renderer adapters
- DOM, CSS, Tailwind, Radix, and React behavior
- animation
- Copeland, Dominatus, or HMI dependencies

## Theme and tokens

`StandardTheme.Default` provides deterministic token bundles:

- `StandardColors` for background, foreground, primary, secondary, destructive, muted, border, and accent colors
- `StandardSpacing` for small spacing and padding values
- `StandardRadius` for future radius-aware renderers

Radius tokens are stored for future component and renderer use. M0a does not emit radius because the current Core `UiStyle` only models foreground, background, and padding.

Button sizes are accepted as stable declaration inputs and are currently represented as deterministic padding style tokens. Core button intrinsic measurement still owns actual button frame sizing in M0a.

Separators lower as deterministic rectangles. A horizontal separator uses a default `100 x thickness` rectangle; a vertical separator uses a default `thickness x 100` rectangle.

## Example

```csharp
var ui = StandardUI.Card(
    id: "profile-card",
    child: UI.Column(
        gap: 8,
        children:
        [
            StandardUI.Badge("Admin", id: "role"),
            UI.Text("Ada Lovelace", id: "name", size: TextSize.H1),
            StandardUI.Button(
                "Save",
                id: "save",
                action: UiAction.Named("save")),
        ]));
```

The example builds a Core declaration tree. Lowering it with `UiLowerer.Lower(ui)` produces deterministic layout rows plus style, text-style, semantic, and action metadata.
