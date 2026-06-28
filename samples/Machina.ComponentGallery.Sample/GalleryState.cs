using Machina.Core.Actions;

namespace Machina.ComponentGallery.Sample;

public sealed record GalleryState(
    int PrimaryClicks,
    int SecondaryClicks,
    bool LiveCheckboxChecked,
    bool LiveSwitchOn,
    string InputValue)
{
    public static GalleryState Default { get; } = new(
        PrimaryClicks: 0,
        SecondaryClicks: 0,
        LiveCheckboxChecked: true,
        LiveSwitchOn: false,
        InputValue: "gallery.test.local");

    public static GalleryState Dispatch(GalleryState state, UiActionId action)
    {
        if (action == GalleryActions.ClickPrimaryButton)
        {
            return state with { PrimaryClicks = state.PrimaryClicks + 1 };
        }

        if (action == GalleryActions.ClickSecondaryButton)
        {
            return state with { SecondaryClicks = state.SecondaryClicks + 1 };
        }

        if (action == GalleryActions.ToggleCheckbox)
        {
            return state with { LiveCheckboxChecked = !state.LiveCheckboxChecked };
        }

        if (action == GalleryActions.ToggleSwitch)
        {
            return state with { LiveSwitchOn = !state.LiveSwitchOn };
        }

        return state;
    }
}
