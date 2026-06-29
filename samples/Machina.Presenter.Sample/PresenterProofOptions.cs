namespace Machina.Presenter.Sample;

public sealed record PresenterProofOptions(
    bool IncludeDirectOutlineRenderBridgeProof = false)
{
    public bool HasAnyProof => IncludeDirectOutlineRenderBridgeProof;
}
