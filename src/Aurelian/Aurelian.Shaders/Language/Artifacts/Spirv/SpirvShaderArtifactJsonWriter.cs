using System.Text.Json;

namespace Aurelian.Shaders.Language.Artifacts.Spirv;

public static class SpirvShaderArtifactJsonWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Write(SpirvShaderArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var manifest = new
        {
            artifact.FormatVersion,
            artifact.Language,
            artifact.Success,
            Stages = artifact.Stages.Select(stage => new
            {
                Stage = stage.Stage.ToString(),
                stage.EntryPoint,
                stage.Profile,
                stage.SourceName,
                stage.SourceSha256,
                stage.SpirvSha256,
                SpirvBase64 = Convert.ToBase64String(stage.SpirvBytes),
                stage.DxcArguments,
            }).ToArray(),
            Diagnostics = artifact.Diagnostics.Select(diagnostic => new
            {
                diagnostic.Code,
                Severity = diagnostic.Severity.ToString(),
                diagnostic.Message,
            }).ToArray(),
        };

        return JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine;
    }
}
