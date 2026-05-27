# Machina M5c3: Checkbox and Switch style records fully wired

M5c3 completes style-record wiring for checkbox and switch components.

- `StandardUI.Checkbox` now resolves all checkbox visual and geometry values from `StandardCheckboxStyle`.
- `StandardUI.Switch` now resolves all switch visual and geometry values from `StandardSwitchStyle`.
- Explicit `style:` wins. Theme is only used to provide defaults.
- There is no cascading merge model and no mutable global style state.

Example:

```csharp
var checkboxStyle = theme.Checkbox.Default with
{
    BoxSize = 22,
    MarkSize = 10,
    Gap = 9,
};

StandardUI.Checkbox(
    id: "email-updates",
    label: "Email updates",
    isChecked: state.EmailUpdates,
    changed: Actions.ToggleEmailUpdates.ToAction(),
    style: checkboxStyle);
```

Headless tests in `Machina.Standard.Tests` and `Machina.Pipeline.Tests` are the source of truth for geometry and hit-test behavior.
