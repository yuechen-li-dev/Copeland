namespace Aurelian.Rendering.Contracts.Snapshots;

public readonly record struct RenderMeshRef(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct RenderMaterialRef(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct RenderTextureRef(string Value)
{
    public override string ToString() => Value;
}
