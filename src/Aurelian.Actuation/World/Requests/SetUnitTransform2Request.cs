using Aurelian.World.Stores;
using Aurelian.World.Units;

namespace Aurelian.Actuation.World.Requests;

public sealed record SetUnitTransform2Request(UnitId UnitId, Transform2 Transform);
