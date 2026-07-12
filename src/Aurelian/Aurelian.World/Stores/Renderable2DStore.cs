using Aurelian.World.Units;

namespace Aurelian.World.Stores;

public sealed record Renderable2DStore(IReadOnlyDictionary<UnitId, Renderable2DData> Renderables)
{
    public static Renderable2DStore Empty { get; } = new(new Dictionary<UnitId, Renderable2DData>());

    public bool TryGet(UnitId unitId, out Renderable2DData renderable) =>
        Renderables.TryGetValue(unitId, out renderable!);

    public Renderable2DStore Set(UnitId unitId, Renderable2DData renderable)
    {
        Dictionary<UnitId, Renderable2DData> renderables = CloneRenderables();
        renderables[unitId] = renderable;
        return new Renderable2DStore(renderables);
    }

    public Renderable2DStore Remove(UnitId unitId)
    {
        Dictionary<UnitId, Renderable2DData> renderables = CloneRenderables();
        renderables.Remove(unitId);
        return new Renderable2DStore(renderables);
    }

    private Dictionary<UnitId, Renderable2DData> CloneRenderables() =>
        Renderables
            .OrderBy(x => x.Key.Value)
            .ToDictionary(x => x.Key, x => x.Value);
}
