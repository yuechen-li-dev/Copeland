using System.Security.Cryptography;
using System.Text;
using Aurelian.Shaders.Language.External.Dxc;

namespace Aurelian.Shaders.Language.Artifacts.Spirv;

public static class SpirvShaderArtifactEmitter
{
    public static SpirvShaderArtifact EmitFromHlslStages(IReadOnlyList<HlslShaderStageSource> stages)
    {
        var diagnostics = new List<SpirvShaderArtifactDiagnostic>();
        var artifacts = new List<SpirvShaderStageArtifact>();

        if (stages is null || stages.Count == 0)
        {
            diagnostics.Add(Error(
                SpirvShaderArtifactDiagnosticCodes.EmptyHlslSource,
                "At least one HLSL stage source is required."));
            return Create(artifacts, diagnostics);
        }

        AddDuplicateStageDiagnostics(stages, diagnostics);

        foreach (var stage in stages)
        {
            ValidateStage(stage, diagnostics);
        }

        if (diagnostics.Any(x => x.Severity == SpirvShaderArtifactDiagnosticSeverity.Error))
        {
            return Create(artifacts, diagnostics);
        }

        var resolution = DxcExecutableResolver.Resolve();
        if (!resolution.Success)
        {
            diagnostics.Add(Error(
                SpirvShaderArtifactDiagnosticCodes.DxcUnavailable,
                "DXC is unavailable; HLSL stages could not be compiled to SPIR-V."));
            return Create(artifacts, diagnostics);
        }

        foreach (var stage in stages)
        {
            var result = DxcSpirvCompiler.Compile(
                new DxcSpirvCompileRequest(
                    stage.SourceText,
                    stage.EntryPoint,
                    stage.Profile,
                    EffectiveSourceName(stage.SourceName),
                    null),
                resolution);

            if (result.Status == DxcSpirvStatus.Unavailable)
            {
                diagnostics.Add(Error(
                    SpirvShaderArtifactDiagnosticCodes.DxcUnavailable,
                    $"DXC is unavailable while compiling {DisplayStage(stage)}."));
                continue;
            }

            if (result.Status != DxcSpirvStatus.Compiled)
            {
                diagnostics.Add(Error(
                    SpirvShaderArtifactDiagnosticCodes.DxcCompilationFailed,
                    BuildCompileFailureMessage(stage, result)));
                continue;
            }

            if (result.SpirvBytes.Length == 0)
            {
                diagnostics.Add(Error(
                    SpirvShaderArtifactDiagnosticCodes.EmptySpirvOutput,
                    $"DXC produced empty SPIR-V output for {DisplayStage(stage)}."));
                continue;
            }

            artifacts.Add(new SpirvShaderStageArtifact(
                stage.Stage,
                stage.EntryPoint,
                stage.Profile,
                stage.SourceName,
                ComputeSha256Utf8(stage.SourceText),
                ComputeSha256Bytes(result.SpirvBytes),
                result.SpirvBytes,
                result.Arguments));
        }

        return Create(artifacts, diagnostics);
    }

    private static SpirvShaderArtifact Create(
        IReadOnlyList<SpirvShaderStageArtifact> stages,
        IReadOnlyList<SpirvShaderArtifactDiagnostic> diagnostics) => new(
            SpirvShaderArtifact.CurrentFormatVersion,
            SpirvShaderArtifact.LanguageName,
            stages,
            diagnostics);

    private static void AddDuplicateStageDiagnostics(
        IReadOnlyList<HlslShaderStageSource> stages,
        List<SpirvShaderArtifactDiagnostic> diagnostics)
    {
        foreach (var duplicate in stages.GroupBy(x => x.Stage).Where(x => x.Count() > 1).Select(x => x.Key))
        {
            diagnostics.Add(Error(
                SpirvShaderArtifactDiagnosticCodes.DuplicateStage,
                $"Duplicate HLSL stage kind '{duplicate}' is not supported by SPIR-V shader artifact M0."));
        }
    }

    private static void ValidateStage(HlslShaderStageSource stage, List<SpirvShaderArtifactDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(stage.SourceText))
        {
            diagnostics.Add(Error(
                SpirvShaderArtifactDiagnosticCodes.EmptyHlslSource,
                $"HLSL source text is required for {DisplayStage(stage)}."));
        }

        if (string.IsNullOrWhiteSpace(stage.EntryPoint))
        {
            diagnostics.Add(Error(
                SpirvShaderArtifactDiagnosticCodes.MissingEntryPoint,
                $"HLSL entry point is required for {DisplayStage(stage)}."));
        }

        if (string.IsNullOrWhiteSpace(stage.Profile))
        {
            diagnostics.Add(Error(
                SpirvShaderArtifactDiagnosticCodes.MissingProfile,
                $"HLSL shader profile is required for {DisplayStage(stage)}."));
        }
        else if (!ProfileMatchesStage(stage.Stage, stage.Profile))
        {
            diagnostics.Add(Error(
                SpirvShaderArtifactDiagnosticCodes.StageProfileMismatch,
                $"HLSL profile '{stage.Profile}' does not match stage kind '{stage.Stage}'."));
        }
    }

    private static bool ProfileMatchesStage(HlslShaderStageKind stage, string profile) => stage switch
    {
        HlslShaderStageKind.Vertex => profile.StartsWith("vs_", StringComparison.OrdinalIgnoreCase),
        HlslShaderStageKind.Fragment => profile.StartsWith("ps_", StringComparison.OrdinalIgnoreCase),
        HlslShaderStageKind.Compute => profile.StartsWith("cs_", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static string ComputeSha256Utf8(string text) => ComputeSha256Bytes(Encoding.UTF8.GetBytes(text));

    private static string ComputeSha256Bytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static SpirvShaderArtifactDiagnostic Error(string code, string message) => new(
        code,
        SpirvShaderArtifactDiagnosticSeverity.Error,
        message);

    private static string DisplayStage(HlslShaderStageSource stage) =>
        string.IsNullOrWhiteSpace(stage.SourceName) ? stage.Stage.ToString() : $"{stage.Stage} stage '{stage.SourceName}'";

    private static string EffectiveSourceName(string sourceName) =>
        string.IsNullOrWhiteSpace(sourceName) ? "shader.hlsl" : sourceName;

    private static string BuildCompileFailureMessage(HlslShaderStageSource stage, DxcSpirvCompileResult result)
    {
        var details = new[] { result.StandardError, result.StandardOutput }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .FirstOrDefault();

        return details is null
            ? $"DXC failed to compile {DisplayStage(stage)} with status {result.Status}."
            : $"DXC failed to compile {DisplayStage(stage)} with status {result.Status}: {details}";
    }
}
