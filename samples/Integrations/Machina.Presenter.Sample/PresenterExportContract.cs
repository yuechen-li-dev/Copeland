namespace Machina.Presenter.Sample;

public static class PresenterExportContract
{
    public static string DefaultOutputPath { get; } = Path.Combine("artifacts", "presenter-default.png");
    public static string NavigationShellDefaultOutputPath { get; } = Path.Combine("artifacts", "m10a", "presenter-navigation-shell-overview.png");

    public const string DirectOutlineRenderBridgeProofArtifactName = "presenter-direct-outline-render-bridge-proof";
    public const string NavigationShellOverviewArtifactName = "presenter-navigation-shell-overview";
    public const string NavigationShellComponentsArtifactName = "presenter-navigation-shell-components";
    public const string NavigationShellTextArtifactName = "presenter-navigation-shell-text";
    public const string NavigationShellScrolledArtifactName = "presenter-navigation-shell-scrolled";
    public const string NavigationShellManifestJsonName = PresenterNavigationManifestWriter.JsonFileName;
    public const string NavigationShellManifestTextName = PresenterNavigationManifestWriter.TextFileName;

    public static string GetDirectOutlineRenderBridgeProofOutputPath(string outputDirectory)
    {
        return Path.Combine(outputDirectory, $"{DirectOutlineRenderBridgeProofArtifactName}.png");
    }

    public static string GetNavigationShellOutputPath(string outputDirectory, string artifactName)
    {
        return Path.Combine(outputDirectory, $"{artifactName}.png");
    }
}
