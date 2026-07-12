namespace Machina.ComponentGallery.Sample;

public static class GalleryExportContract
{
    public static string DefaultOutputDirectory { get; } = Path.Combine("artifacts", "m7e");

    public const string DefaultExportName = "component-gallery-default";
    public const string InteractiveExportName = "component-gallery-interactive";
    public const string DirectOutlineProofExportName = "component-gallery-direct-outline-text-proof";
    public const string DirectOutlineRenderBridgeProofExportName = "component-gallery-direct-outline-render-bridge-proof";
    public const string DirectOutlineTextLayoutProofExportName = "component-gallery-direct-outline-text-layout-proof";
    public const string MsdfProofExportName = "component-gallery-msdf-proof";
    public const string TextBackendComparisonArtifactName = "component-gallery-text-backend-comparison";
    public const string DirectOutlineStandaloneArtifactName = "direct-outline-static-text-proof";
    public const string DirectOutlineRenderBridgeArtifactName = "direct-outline-render-bridge-proof";
    public const string DirectOutlineRenderBridgeLayoutGridArtifactName = "direct-outline-render-bridge-layout-grid";
    public const string DirectOutlineTextBoxLayoutArtifactName = "direct-outline-text-box-layout-proof";
    public const string DirectOutlineTextAlignmentGridArtifactName = "direct-outline-text-alignment-grid";

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

    public static string GetDirectOutlineRenderBridgeProofOutputPath(string outputDirectory)
    {
        return Path.Combine(outputDirectory, $"{DirectOutlineRenderBridgeProofExportName}.png");
    }

    public static string GetDirectOutlineTextLayoutProofOutputPath(string outputDirectory)
    {
        return Path.Combine(outputDirectory, $"{DirectOutlineTextLayoutProofExportName}.png");
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

    public static string GetDirectOutlineTextBoxLayoutOutputPath(string outputDirectory)
    {
        return Path.Combine(outputDirectory, $"{DirectOutlineTextBoxLayoutArtifactName}.png");
    }

    public static string GetDirectOutlineRenderBridgeOutputPath(string outputDirectory)
    {
        return Path.Combine(outputDirectory, $"{DirectOutlineRenderBridgeArtifactName}.png");
    }

    public static string GetDirectOutlineRenderBridgeLayoutGridOutputPath(string outputDirectory)
    {
        return Path.Combine(outputDirectory, $"{DirectOutlineRenderBridgeLayoutGridArtifactName}.png");
    }

    public static string GetDirectOutlineTextAlignmentGridOutputPath(string outputDirectory)
    {
        return Path.Combine(outputDirectory, $"{DirectOutlineTextAlignmentGridArtifactName}.png");
    }
}
