namespace Aurelian.Rendering.Contracts.CommandPlans;

public readonly record struct RenderPipelineRef(string Value)
{
    public override string ToString() => Value;
}
