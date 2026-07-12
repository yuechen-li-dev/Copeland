using Aurelian.World.Units;

namespace Aurelian.World.Stores;

public sealed record Transform2Store(IReadOnlyDictionary<UnitId, Transform2> Transforms)
{
    public static Transform2Store Empty { get; } = new(new Dictionary<UnitId, Transform2>());

    public bool TryGet(UnitId unitId, out Transform2 transform) => Transforms.TryGetValue(unitId, out transform);

    public Transform2 GetOrIdentity(UnitId unitId) =>
        TryGet(unitId, out Transform2 transform) ? transform : Transform2.Identity;

    public Transform2Store Set(UnitId unitId, Transform2 transform)
    {
        Dictionary<UnitId, Transform2> transforms = CloneTransforms();
        transforms[unitId] = transform;
        return new Transform2Store(transforms);
    }

    public Transform2Store Remove(UnitId unitId)
    {
        Dictionary<UnitId, Transform2> transforms = CloneTransforms();
        transforms.Remove(unitId);
        return new Transform2Store(transforms);
    }

    private Dictionary<UnitId, Transform2> CloneTransforms() =>
        Transforms
            .OrderBy(x => x.Key.Value)
            .ToDictionary(x => x.Key, x => x.Value);
}
