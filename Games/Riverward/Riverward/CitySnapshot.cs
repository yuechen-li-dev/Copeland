using System.Text.Json.Serialization;
using Aurelian.Strategy;

namespace Riverward;

internal readonly record struct SavedBlock(HexCell Cell, Block Kind, int Residents);

internal sealed record CitySnapshot(long Cash, int Month, int MonthProgress, int Streak, bool Won, SavedBlock[] Blocks, HexEdge[] Streets);

[JsonSerializable(typeof(CitySnapshot))]
internal sealed partial class RiverwardJson : JsonSerializerContext;
