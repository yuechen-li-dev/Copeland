using Aurelian.Playtesting;
using System.Text.Json.Serialization;

namespace TinyFarm.Playtesting;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PlaytestScript))]
[JsonSerializable(typeof(PlaytestObservation))]
[JsonSerializable(typeof(PlaytestTrace))]
public partial class PlaytestJsonContext : JsonSerializerContext;
