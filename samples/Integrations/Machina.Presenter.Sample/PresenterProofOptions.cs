namespace Machina.Presenter.Sample;

public sealed record PresenterProofOptions(
    bool IncludeDirectOutlineRenderBridgeProof = false,
    string? OblivionWorkspacePath = null,
    string? OblivionPresentationId = null)
{
    public OblivionHostOptions OblivionHostOptions => new(
        OblivionWorkspacePath,
        OblivionPresentationId);

    public static implicit operator OblivionHostOptions(PresenterProofOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.OblivionHostOptions;
    }

    public bool HasAnyProof =>
        IncludeDirectOutlineRenderBridgeProof ||
        !string.IsNullOrWhiteSpace(OblivionWorkspacePath) ||
        !string.IsNullOrWhiteSpace(OblivionPresentationId);
}
