using System.Text.Json.Serialization;

namespace TinyFarm.Core;

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(TinyFarmSliceState))]
internal partial class TinyFarmSliceJsonContext : JsonSerializerContext;
