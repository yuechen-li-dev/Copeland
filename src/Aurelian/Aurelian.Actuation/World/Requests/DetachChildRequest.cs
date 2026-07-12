using Aurelian.World.Units;

namespace Aurelian.Actuation.World.Requests;

public sealed record DetachChildRequest(
    UnitId ParentId,
    UnitId ChildId);
