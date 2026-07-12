namespace Machina.Presenter.Sample;

public sealed record PresenterProofOptions(
    bool IncludeDirectOutlineRenderBridgeProof = false,
    string? OblivionWorkspacePath = null)
{
    public bool HasAnyProof =>
        IncludeDirectOutlineRenderBridgeProof ||
        !string.IsNullOrWhiteSpace(OblivionWorkspacePath);
}
