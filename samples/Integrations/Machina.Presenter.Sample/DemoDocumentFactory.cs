using Machina.Core.Flat;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public static class DemoDocumentFactory
{
    public const int RootWidth = SettingsScreen.RootWidth;
    public const int RootHeight = SettingsScreen.RootHeight;

    public static UiDocument Build(DemoState state, StandardTheme? theme = null)
    {
        return Build(state, theme, new PresenterProofOptions());
    }

    public static UiDocument Build(
        DemoState state,
        StandardTheme? theme,
        PresenterProofOptions proofOptions)
    {
        return SettingsScreen.Build(state, theme, proofOptions);
    }
}
