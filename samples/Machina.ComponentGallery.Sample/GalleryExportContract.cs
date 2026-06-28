namespace Machina.ComponentGallery.Sample;

public static class GalleryExportContract
{
    public static string DefaultOutputDirectory { get; } = Path.Combine("artifacts", "m7e");

    public const string DefaultExportName = "component-gallery-default";
    public const string InteractiveExportName = "component-gallery-interactive";

    public static GalleryState InteractiveState { get; } = GalleryState.Default with
    {
        PrimaryClicks = 1,
        LiveCheckboxChecked = true,
        LiveSwitchOn = true,
    };

    public static string GetDefaultOutputPath(string outputDirectory)
    {
        return Path.Combine(outputDirectory, $"{DefaultExportName}.png");
    }

    public static string GetInteractiveOutputPath(string outputDirectory)
    {
        return Path.Combine(outputDirectory, $"{InteractiveExportName}.png");
    }
}
