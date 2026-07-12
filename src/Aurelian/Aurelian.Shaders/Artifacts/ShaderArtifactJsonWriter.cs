using System.Text;
using System.Text.Json;

namespace Aurelian.Shaders.Artifacts;

public static class ShaderArtifactJsonWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static string ToJson(ShaderArtifactManifest manifest)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("format", manifest.Format);
        writer.WriteNumber("artifactVersion", manifest.ArtifactVersion);
        writer.WriteString("sourcePath", manifest.SourcePath);
        writer.WriteString("sourceHash", manifest.SourceHash);
        writer.WriteString("entryShader", manifest.EntryShader);
        writer.WriteString("specializationKey", manifest.SpecializationKey);
        writer.WriteString("backendProfile", manifest.BackendProfile);
        writer.WriteString("compiler", manifest.Compiler);
        writer.WriteString("compilerVersion", manifest.CompilerVersion);
        writer.WriteString("targetFamily", manifest.TargetFamily);

        writer.WritePropertyName("stages");
        JsonSerializer.Serialize(writer, manifest.Stages, SerializerOptions);
        writer.WritePropertyName("specializations");
        JsonSerializer.Serialize(writer, manifest.Specializations, SerializerOptions);
        writer.WritePropertyName("io");
        JsonSerializer.Serialize(writer, manifest.Io, SerializerOptions);
        writer.WritePropertyName("effectUsingParams");
        JsonSerializer.Serialize(writer, manifest.EffectUsingParams, SerializerOptions);
        writer.WritePropertyName("effectMixins");
        JsonSerializer.Serialize(writer, manifest.EffectMixins, SerializerOptions);
        writer.WritePropertyName("diagnostics");
        JsonSerializer.Serialize(writer, manifest.Diagnostics, SerializerOptions);

        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
