namespace Machina.ComponentGallery.Sample;

public static class GalleryExportContract
{
    public static string DefaultOutputDirectory { get; } = Path.Combine("artifacts", "m7e");

    public const string DefaultExportName = "component-gallery-default";
    public const string InteractiveExportName = "component-gallery-interactive";
    public const string DirectOutlineProofExportName = "component-gallery-direct-outline-text-proof";
    public const string MsdfProofExportName = "component-gallery-msdf-proof";
    public const string TextBackendComparisonArtifactName = "component-gallery-text-backend-comparison";
    public const string DirectOutlineStandaloneArtifactName = "direct-outline-static-text-proof";

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

    public static string GetMsdfProofOutputPath(string outputDirectory)
    {
        return Path.Combine(outputDirectory, $"{MsdfProofExportName}.png");
    }

    public static string GetDirectOutlineProofOutputPath(string outputDirectory)
    {
        return Path.Combine(outputDirectory, $"{DirectOutlineProofExportName}.png");
    }

    public static string GetTextBackendComparisonOutputPath(string outputDirectory)
    {
        return Path.Combine(outputDirectory, $"{TextBackendComparisonArtifactName}.png");
    }

    public static string GetDirectOutlineStandaloneOutputPath(string outputDirectory)
    {
        return Path.Combine(outputDirectory, $"{DirectOutlineStandaloneArtifactName}.png");
    }
}
