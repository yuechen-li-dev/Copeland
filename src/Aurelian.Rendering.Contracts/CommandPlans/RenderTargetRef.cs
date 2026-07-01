namespace Aurelian.Rendering.Contracts.CommandPlans;

public readonly record struct RenderTargetRef(string Value)
{
    public override string ToString() => Value;
}
