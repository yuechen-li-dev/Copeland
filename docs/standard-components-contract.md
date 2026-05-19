# Machina.Standard component contract

`Machina.Standard` is the first reusable standard component package above `Machina.Core`.
It is a declaration-only layer: every component produces immutable Machina Core UI nodes that lower through the existing deterministic `UiLowerer` pipeline.

## Relationship to Machina.Core

`Machina.Core` remains the renderer-independent UI declaration layer. It owns the readable `UI.*` authoring surface, immutable `UiNode` records, immutable styles, semantic/action metadata, deterministic lowering, and the text-measurement seam.

`Machina.Standard` composes those Core primitives into a small, reusable component vocabulary. Standard components do not introduce a renderer, event dispatcher, DOM model, CSS model, validation engine, focus engine, or Copeland/Dominatus integration. They are ordinary C# declaration helpers that return `UiNode` values.

## shadcn-intent philosophy

Standard uses shadcn/ui as component inventory and concept-art reference only.

The package does **not** port or copy:

- Tailwind classes
- Radix primitives
- React components, hooks, or DOM structure
- CSS selectors or browser behavior
- focus, keyboard, popup, or input-dispatch systems

Instead, Standard re-expresses a small shadcn-shaped vocabulary in Machina-native terms: Core declarations, immutable style records, semantic metadata, action metadata where Core can carry it cleanly, and deterministic lowering.

## M0a supported components

M0a intentionally started small:

- `StandardUI.Button`
- `StandardUI.Card`
- `StandardUI.Badge`
- `StandardUI.Separator`

Buttons are declaration shells with semantic role `Button`. Enabled buttons preserve named action metadata. Disabled buttons keep disabled semantics and omit active action metadata.

Cards, badges, and separators are visual composition helpers over Core nodes and standard theme tokens.

## M0b supported form declarations

M0b adds the first form-oriented declaration vocabulary:

- `StandardUI.Field`
- `StandardUI.Label`
- `StandardUI.Input`
- `StandardUI.Checkbox`
- `StandardUI.Switch`

These are still declaration-only shells. They describe visual structure, stable ids, styles, roles, and optional changed-action metadata. They do not perform real input behavior.

### Field

`StandardUI.Field` is a vertical grouping helper for a label, control, description, and error message. It lowers to a Core column and includes only the optional pieces supplied by the caller.

When the field has an explicit id, generated children use deterministic suffixes:

- `{field-id}.label`
- `{field-id}.description`
- `{field-id}.error`

`Field` does not provide validation, form submission, or label/control binding. It is only a deterministic declaration grouping.

### Label

`StandardUI.Label` lowers to styled Core text with semantic role `Label`. It uses standard foreground color and small text sizing.

There is no `for`/target binding in M0b. Any association between a label and a control is currently represented only by composition and naming conventions.

### Input shell

`StandardUI.Input` is a visual and semantic shell for text-like input. It lowers to a styled Core rectangle containing text.

M0b input behavior is intentionally limited:

- if `value` is non-empty, the shell displays `value`
- otherwise, it displays `placeholder` when present
- placeholder and disabled text use muted foreground tokens
- disabled input uses muted background tokens
- enabled input emits semantic role `Input` and is focusable in metadata
- an enabled input preserves the supplied `changed` action as declaration metadata
- a disabled input omits the supplied `changed` action

There is no real text editing, caret, selection, keyboard handling, focus engine, validation, or event dispatch.

### Checkbox

`StandardUI.Checkbox` is a boolean declaration shell. It renders a small box, an optional deterministic checked marker (`✓`), and an optional label row.

M0b checkbox behavior is intentionally limited:

- `isChecked` changes the visible marker and active styling
- `disabled` changes styling and disabled semantics
- enabled checkboxes emit semantic role `Checkbox` and focusable metadata
- enabled checkboxes preserve supplied `changed` action metadata
- disabled checkboxes omit supplied `changed` action metadata

There is no real toggling, keyboard handling, focus engine, or event dispatch.

### Switch

`StandardUI.Switch` is a boolean declaration shell. It renders a track and thumb with deterministic on/off visual differences, plus an optional label row.

M0b switch behavior is intentionally limited:

- `isOn` changes active/inactive track styling and thumb placement declaration
- `disabled` changes styling and disabled semantics
- enabled switches emit semantic role `Switch` and focusable metadata
- enabled switches preserve supplied `changed` action metadata
- disabled switches omit supplied `changed` action metadata

There is no real toggling, animation, keyboard handling, focus engine, or event dispatch.

## Theme and tokens

`StandardTheme.Default` provides deterministic token bundles:

- `StandardColors` for background, foreground, primary, secondary, destructive, muted, border, and accent colors
- `StandardSpacing` for small spacing and padding values
- `StandardRadius` for future radius-aware renderers

Radius tokens are stored for future component and renderer use. M0a and M0b do not emit radius because the current Core `UiStyle` only models foreground, background, and padding.

Button sizes are accepted as stable declaration inputs and are currently represented as deterministic padding style tokens. Core button intrinsic measurement still owns actual button frame sizing.

Separators lower as deterministic rectangles. A horizontal separator uses a default `100 x thickness` rectangle; a vertical separator uses a default `thickness x 100` rectangle.

## Form card example

```csharp
var ui = StandardUI.Card(
    id: "settings-card",
    child: UI.Column(
        id: "settings-content",
        gap: 12,
        children:
        [
            UI.Text("Settings", id: "title", size: TextSize.H1),

            StandardUI.Field(
                id: "username-field",
                label: "Username",
                control: StandardUI.Input(
                    id: "username",
                    value: "ada",
                    placeholder: "Enter username"),
                description: "This appears in your profile."),

            StandardUI.Checkbox(
                id: "email-updates",
                label: "Email updates",
                isChecked: true,
                changed: UiAction.Named("email-updates.changed")),

            StandardUI.Switch(
                id: "notifications",
                label: "Notifications",
                isOn: false,
                changed: UiAction.Named("notifications.changed")),

            StandardUI.Separator(id: "rule"),

            StandardUI.Button(
                "Save",
                id: "save",
                action: UiAction.Named("save")),
        ]));
```

The example builds a Core declaration tree. Lowering it with `UiLowerer.Lower(ui)` produces deterministic layout rows plus style, text-style, semantic, and action metadata. Compiling the lowered rows with `LayoutCompiler.CompileLayoutRows(...)` validates that the declaration remains inside the Machina.Core and Machina.Layout pipeline.

## Non-goals retained after M0b

M0b does not add:

- real text editing
- real checkbox or switch toggling
- focus management
- keyboard navigation
- validation
- form submission
- renderer adapters
- DOM, CSS, Tailwind, Radix, or React behavior
- animation
- accessibility tree export beyond existing semantics metadata
- dialog, popover, dropdown, select, combobox, command, or data table components
- Copeland, Dominatus, or HMI dependencies
