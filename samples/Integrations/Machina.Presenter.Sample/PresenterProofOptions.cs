namespace Machina.Presenter.Sample;

public sealed record PresenterProofOptions(
    bool IncludeDirectOutlineRenderBridgeProof = false,
    string? OblivionWorkspacePath = null)
{
    public OblivionHostOptions OblivionHostOptions => new(OblivionWorkspacePath);

    public static implicit operator OblivionHostOptions(PresenterProofOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.OblivionHostOptions;
    }

    public bool HasAnyProof =>
        IncludeDirectOutlineRenderBridgeProof ||
        !string.IsNullOrWhiteSpace(OblivionWorkspacePath);
}
