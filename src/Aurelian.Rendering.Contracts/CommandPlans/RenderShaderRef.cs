namespace Aurelian.Rendering.Contracts.CommandPlans;

public readonly record struct RenderShaderRef(string Value)
{
    public override string ToString() => Value;
}
