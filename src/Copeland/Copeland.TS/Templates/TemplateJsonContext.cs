using System.Text.Json;
using System.Text.Json.Serialization;

namespace Copeland.TS.Templates;

internal sealed record TemplatePackageManifestJson(
    string Name,
    string Version,
    bool Private,
    string Type,
    Dictionary<string, string> Dependencies);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TemplatePackageManifestJson))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
internal sealed partial class TemplateJsonContext : JsonSerializerContext
{
    internal static TemplateJsonContext Indented { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    });
}
