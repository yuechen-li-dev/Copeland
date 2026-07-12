using Aurelian.World.Stores;
using Aurelian.World.Units;

namespace Aurelian.Actuation.World.Requests;

public sealed record SetUnitNameRequest(UnitId UnitId, UnitName Name);
