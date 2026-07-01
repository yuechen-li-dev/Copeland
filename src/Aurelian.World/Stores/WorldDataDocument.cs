using Aurelian.World.Units;

namespace Aurelian.World.Stores;

public sealed record WorldDataDocument(
    WorldDocument World,
    UnitNameStore Names,
    Transform2Store Transforms,
    Renderable2DStore Renderables)
{
    public static WorldDataDocument FromWorld(WorldDocument world) =>
        new(world, UnitNameStore.Empty, Transform2Store.Empty, Renderable2DStore.Empty);

    public WorldDataDocument WithWorld(WorldDocument world) => this with { World = world };

    public WorldDataDocument WithNames(UnitNameStore names) => this with { Names = names };

    public WorldDataDocument WithTransforms(Transform2Store transforms) => this with { Transforms = transforms };

    public WorldDataDocument WithRenderables(Renderable2DStore renderables) => this with { Renderables = renderables };
}
