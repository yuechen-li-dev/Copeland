# StandardUI vs StandardView (M5d)

## Purpose

M5d clarifies the authoring contract between `StandardUI` and `StandardView` so app code, samples, and future automation consistently choose the right surface.

## Rule of thumb

- Use `StandardUI` for normal components.
- Use `StandardView` for simple row metadata / leaf visuals.
- Use sub-part helpers only when authoring custom components.
- Do not manually decompose standard controls in app-level layout tables.

## StandardUI

`StandardUI` is the primary component-function surface for app/component authoring. These helpers return `UiNode` and own local internals.

Typical control/component usage:

- `StandardUI.Card`
- `StandardUI.Button`
- `StandardUI.Input`
- `StandardUI.Field`
- `StandardUI.Checkbox`
- `StandardUI.Switch`
- `StandardUI.Label`
- `StandardUI.Text` (if added later as node helper)
- `StandardUI.Badge`
- `StandardUI.Separator`

## StandardView

`StandardView` is a lightweight `UiView` metadata/helper surface for flat row authoring.

Use it for simple single-row metadata in explicit row tables.

## Advanced sub-part helpers

`StandardView.CheckboxBox`, `StandardView.SwitchTrack`, and `StandardView.SwitchThumb` are advanced helpers for custom component authors who intentionally compose low-level control sub-parts.

They are not the default app-level authoring path.

## What not to do

Avoid manually decomposing standard controls in screen documents when `StandardUI` already provides localized components.

Bad app-level decomposition example:

```csharp
Row.Fixed(id: "email-box", parent: "email-row", width: 18, height: 18, view: StandardView.CheckboxBox(true));
Row.Fill(id: "email-label", parent: "email-row", view: StandardView.Text("Email updates"));
```

## Examples

Preferred app-level component placement:

```csharp
Row.Anchor(
    id: "email-updates",
    parent: "settings-card",
    left: 20,
    top: 150,
    width: 240,
    height: 24,
    component: StandardUI.Checkbox(id: "email-updates", label: "Email updates", isChecked: true));
```

Valid advanced custom component use:

```csharp
private static UiNode MyCompactCheckbox(string id, bool isChecked, UiAction action)
{
    return UI.Column(
        id: id,
        children:
        [
            UI.Row(children:
            [
                UI.Rect(id: id + ".box", view: StandardView.CheckboxBox(isChecked, action)),
                UI.Text("Compact label")
            ])
        ]);
}
```

## Compatibility notes

- Existing `StandardView` helpers remain available for compatibility.
- M5d does not remove public helpers or change rendering/runtime behavior.
- Historical row-only guidance is superseded for canonical app authoring by this contract.

## Helper classification table

| Helper | Classification | Intended use | Notes |
| --- | --- | --- | --- |
| `StandardUI.Card` | Primary component function | App/component authoring | Localized internals; place as component. |
| `StandardUI.Button` | Primary component function | App/component authoring | Localized internals; includes action/semantics. |
| `StandardUI.Input` | Primary component function | App/component authoring | Localized shell/content internals. |
| `StandardUI.Field` | Primary component function | App/component authoring | Composition helper around label/control/description/error. |
| `StandardUI.Checkbox` | Primary component function | App/component authoring | Preferred checkbox API for ordinary usage. |
| `StandardUI.Switch` | Primary component function | App/component authoring | Preferred switch API for ordinary usage. |
| `StandardUI.Label` | Primary component function | App/component authoring | Simple node helper, still component-surface default. |
| `StandardUI.Badge` | Primary component function | App/component authoring | Simple node helper, still component-surface default. |
| `StandardUI.Separator` | Primary component function | App/component authoring | Simple node helper, still component-surface default. |
| `StandardView.Card` | Legacy/compatibility helper | Flat row metadata | Kept for compatibility; prefer `StandardUI.Card` for app components. |
| `StandardView.Button` | Legacy/compatibility helper | Flat row metadata | Kept for compatibility; prefer `StandardUI.Button`. |
| `StandardView.Checkbox` | Legacy/compatibility helper | Flat row metadata | Kept for compatibility; prefer `StandardUI.Checkbox`. |
| `StandardView.Switch` | Legacy/compatibility helper | Flat row metadata | Kept for compatibility; prefer `StandardUI.Switch`. |
| `StandardView.Input` | Legacy/compatibility helper | Flat row metadata | Kept for compatibility; prefer `StandardUI.Input`. |
| `StandardView.Text` | Simple leaf metadata/helper | Flat row metadata | Canonical leaf text helper for row tables. |
| `StandardView.Label` | Simple leaf metadata/helper | Flat row metadata | Canonical label metadata helper. |
| `StandardView.Badge` | Simple leaf metadata/helper | Flat row metadata | Canonical badge metadata helper. |
| `StandardView.Separator` | Simple leaf metadata/helper | Flat row metadata | Canonical separator metadata helper. |
| `StandardView.CheckboxBox` | Advanced sub-part metadata helper | Custom component composition | Advanced/manual helper; not default app-level usage. |
| `StandardView.SwitchTrack` | Advanced sub-part metadata helper | Custom component composition | Advanced/manual helper; not default app-level usage. |
| `StandardView.SwitchThumb` | Advanced sub-part metadata helper | Custom component composition | Advanced/manual helper; not default app-level usage. |
