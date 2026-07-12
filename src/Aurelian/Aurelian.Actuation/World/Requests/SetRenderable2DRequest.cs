using Aurelian.World.Stores;
using Aurelian.World.Units;

namespace Aurelian.Actuation.World.Requests;

public sealed record SetRenderable2DRequest(UnitId UnitId, Renderable2DData Renderable);
