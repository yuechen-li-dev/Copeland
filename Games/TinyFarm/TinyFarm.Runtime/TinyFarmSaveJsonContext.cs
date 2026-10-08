using System.Text.Json.Serialization;

namespace TinyFarm.Core;

/// <summary>Closed serialization graph for game saves and semantic replay; no reflection fallback.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(TinyFarmSave))]
[JsonSerializable(typeof(TinyFarmState))]
[JsonSerializable(typeof(TinyFarmChunkedSaveCodec.WorldChunkModel))]
[JsonSerializable(typeof(TinyFarmRuntimeSave))]
[JsonSerializable(typeof(TinyFarmAgentSave))]
[JsonSerializable(typeof(TinyFarmNarrativeSave))]
[JsonSerializable(typeof(TinyFarmSemanticSaveSnapshot))]
[JsonSerializable(typeof(TinyFarmSemanticSaveSnapshotV1))]
[JsonSerializable(typeof(TinyFarmReplayEnvelope))]
internal partial class TinyFarmSaveJsonContext : JsonSerializerContext;
