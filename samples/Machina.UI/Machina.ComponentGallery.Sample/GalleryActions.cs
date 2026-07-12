using Machina.Core.Actions;

namespace Machina.ComponentGallery.Sample;

public static class GalleryActions
{
    public static readonly UiActionId ClickPrimaryButton = new("gallery.button.primary.click");
    public static readonly UiActionId ClickSecondaryButton = new("gallery.button.secondary.click");
    public static readonly UiActionId ToggleCheckbox = new("gallery.checkbox.toggle");
    public static readonly UiActionId ToggleSwitch = new("gallery.switch.toggle");
}
