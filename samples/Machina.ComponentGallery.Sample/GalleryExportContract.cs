namespace Machina.ComponentGallery.Sample;

public static class GalleryExportContract
{
    public static string DefaultOutputDirectory { get; } = Path.Combine("artifacts", "m7b");

    public const string DefaultExportName = "component-gallery-default";
    public const string InteractiveExportName = "component-gallery-interactive";

    public static GalleryState InteractiveState { get; } = GalleryState.Default with
    {
        PrimaryClicks = 1,
        LiveCheckboxChecked = true,
        LiveSwitchOn = true,
    };
}
