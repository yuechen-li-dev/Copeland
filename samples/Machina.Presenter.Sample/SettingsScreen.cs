using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public static class SettingsScreen
{
    public const int RootWidth = 640;
    public const int RootHeight = 360;

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
