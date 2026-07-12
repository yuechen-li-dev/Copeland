using System.Text.Json;
using Aurelian.Shaders.Language.Diagnostics;

namespace Aurelian.Shaders.Language.Artifacts;

public static class SdslvShaderArtifactJsonWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string WriteManifest(SdslvShaderArtifact artifact)
    {
        var manifest = new SdslvShaderArtifactManifest(
            artifact.FormatVersion,
            artifact.Language,
            artifact.SourceName,
            artifact.SourceHash,
            artifact.Stages,
            artifact.Diagnostics.Select(ToManifestDiagnostic).ToArray(),
            artifact.Success,
            artifact.Hlsl);

        return JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine;
    }

    private static SdslvShaderArtifactManifestDiagnostic ToManifestDiagnostic(SdslvDiagnostic diagnostic) => new(
        diagnostic.Code,
        diagnostic.Severity.ToString(),
        diagnostic.Phase.ToString(),
        diagnostic.Message,
        new SdslvShaderArtifactManifestSpan(
            diagnostic.Span.Start,
            diagnostic.Span.End,
            diagnostic.Span.Line,
            diagnostic.Span.Column));
}
