using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Gpu.Wgsl;

[SupportedOSPlatform("browser")]
public static partial class BrowserQualification
{
    public static void Main() { }

    [JSExport]
    public static string Compile(string source, string path)
    {
        var result = WgslGraphicsBackend.Compile(new GpuCompilationRequest([new GpuSourceFile(path, source)]));
        return JsonSerializer.Serialize(result, QualificationJson.Default.WgslGraphicsResult);
    }
}

[JsonSerializable(typeof(WgslGraphicsResult))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class QualificationJson : JsonSerializerContext;
