using Aurelian.World.Units;

namespace Aurelian.Actuation.World.Requests;

public sealed record AttachChildRequest(
    UnitId ParentId,
    UnitChild Child);
