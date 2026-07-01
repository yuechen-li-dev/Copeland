namespace Aurelian.World.Stores;

public readonly record struct WorldMeshRef(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct WorldMaterialRef(string Value)
{
    public override string ToString() => Value;
}

public sealed record Renderable2DData(
    WorldMeshRef Mesh,
    WorldMaterialRef Material,
    bool Visible = true,
    int SortOrder = 0);
