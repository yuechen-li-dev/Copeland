using System.Text;
using System.Text.Json;

namespace Copeland.TS.Gpu.VdMir;

public static class VdMirJson
{
    public static string Serialize(VdMirComputeModule module)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            JsonSerializer.Serialize(writer, module, SerializerOptions);
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
    }

    public static string Serialize(VdMirGraphicsModule module)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            JsonSerializer.Serialize(writer, module, SerializerOptions);
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
    }

    private static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
