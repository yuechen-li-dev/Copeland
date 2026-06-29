namespace Machina.Presenter.Sample;

public static class PresenterExportContract
{
    public static string DefaultOutputPath { get; } = Path.Combine("artifacts", "presenter-default.png");

    public const string DirectOutlineRenderBridgeProofArtifactName = "presenter-direct-outline-render-bridge-proof";

    public static string GetDirectOutlineRenderBridgeProofOutputPath(string outputDirectory)
    {
        return Path.Combine(outputDirectory, $"{DirectOutlineRenderBridgeProofArtifactName}.png");
    }
}
