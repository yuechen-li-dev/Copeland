# Machina Presenter Ergonomics (M5g)

M5g keeps the presenter sample architecture intact and cleans up the authoring surface so the sample reads as the canonical screen/component/action/theme model.

## Convention

- A **screen** owns the `UiDocument` row table and placement frames. In the sample this is `SettingsScreen`.
- A **component** owns localized `StandardUI`/`UI` composition under a hosted row. In the sample this is `SettingsCard`.
- **Actions** live in a screen/domain contract. In the sample this is `SettingsActions`.
- **Dispatch** references that action contract directly. `DemoStateDispatch` does not depend on document factory naming.
- **Theme** is explicit C# data. The screen accepts a `StandardTheme?`, defaults to `StandardTheme.Default`, and passes the theme into hosted components. Components pass the same theme into child `StandardUI.*` controls.

There is no hidden global theme state and no CSS-like cascade.

## Canonical shape

```csharp
public static class SettingsScreen
{
    public static UiDocument Build(DemoState state, StandardTheme? theme = null)
    {
        var effectiveTheme = theme ?? StandardTheme.Default;

        return UiDocument.Create(
            rows:
            [
                Row.Root(
                    id: "root",
                    view: View.Rect(background: effectiveTheme.Colors.Background)),

                Row.Anchor(
                    id: "settings-card",
                    parent: "root",
                    left: 72,
                    top: 24,
                    width: 500,
                    height: 292,
                    component: SettingsCard.Build(state, effectiveTheme)),
            ]);
    }
}
```

```csharp
public static class SettingsCard
{
    public static UiNode Build(DemoState state, StandardTheme theme)
    {
        return StandardUI.Card(
            id: "settings-card-content",
            theme: theme,
            gap: 10,
            children:
            [
                UI.Text("Machina Presenter", id: "title", color: theme.Colors.Foreground),
                StandardUI.Button("Increment", id: "increment", action: SettingsActions.Increment.ToAction(), theme: theme),
                StandardUI.Separator(id: "rule", theme: theme),
                StandardUI.Checkbox(id: "email-updates", changed: SettingsActions.ToggleEmailUpdates.ToAction(), theme: theme),
                StandardUI.Switch(id: "notifications", changed: SettingsActions.ToggleNotifications.ToAction(), theme: theme),
            ]);
    }
}
```

`StandardUI.Card(..., children: [...], gap: ...)` is preferred over manually wrapping `UI.Column` inside a card. The card applies its own default card style from the explicit theme unless a caller supplies an explicit `style:` override.

## Style precedence

1. Explicit `style:` wins.
2. Explicit `theme:` supplies default component styles.
3. No explicit theme uses `StandardTheme.Default`.

## Compatibility

`DemoDocumentFactory.Build(...)` remains as a thin compatibility shim that delegates to `SettingsScreen.Build(...)`. Canonical tests and docs should prefer `SettingsScreen`.

## M6e note

The sample now carries one controlled `StandardUI.TextBlock` probe at the bottom of `SettingsCard`.

This keeps the M5g screen/component/action/theme split intact while providing one visible Standard rich text proof:

- screen still owns the flat top-level document
- component still owns localized composition
- primitive `UI.Text` still owns the existing title/count text
- rich text is isolated to one Standard component surface
