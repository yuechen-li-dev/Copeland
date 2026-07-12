using System.Security.Cryptography;
using System.Text;
using Aurelian.Shaders.Language.Artifacts.Spirv;
using Aurelian.Shaders.Language.Diagnostics;
using Aurelian.Shaders.Language.Emission.Hlsl;
using Aurelian.Shaders.Language.Parsing;
using Aurelian.Shaders.Language.Validation;

namespace Aurelian.Shaders.Language.Artifacts.SdslvSpirv;

public static class SdslvSpirvShaderArtifactEmitter
{
    public static SdslvSpirvShaderArtifact EmitFromSource(string sourceText, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var effectiveSourceName = string.IsNullOrWhiteSpace(sourceName) ? "shader.sdslv" : sourceName;
        var sourceSha256 = ComputeSha256Utf8(sourceText);
        var diagnostics = new List<SdslvSpirvShaderArtifactDiagnostic>();
        var hlsl = string.Empty;
        SpirvShaderArtifact? spirvArtifact = null;

        var parse = SdslvParser.ParseModule(sourceText);
        if (!parse.Success || parse.Module is null)
        {
            AddDiagnosticSummary(
                diagnostics,
                SdslvSpirvShaderArtifactDiagnosticCodes.ParseFailed,
                "SDSL-V parsing failed.",
                parse.Diagnostics);
            return Create(effectiveSourceName, sourceSha256, hlsl, spirvArtifact, diagnostics);
        }

        var validation = SdslvValidator.ValidateModule(parse.Module);
        if (!validation.Success)
        {
            AddDiagnosticSummary(
                diagnostics,
                SdslvSpirvShaderArtifactDiagnosticCodes.ValidationFailed,
                "SDSL-V validation failed.",
                validation.Diagnostics);
            return Create(effectiveSourceName, sourceSha256, hlsl, spirvArtifact, diagnostics);
        }

        var emission = HlslEmitter.EmitModule(parse.Module);
        hlsl = emission.Hlsl;
        if (!emission.Success)
        {
            AddDiagnosticSummary(
                diagnostics,
                SdslvSpirvShaderArtifactDiagnosticCodes.HlslEmissionFailed,
                "SDSL-V HLSL emission failed.",
                emission.Diagnostics);
            return Create(effectiveSourceName, sourceSha256, hlsl, spirvArtifact, diagnostics);
        }

        var stages = SdslvStageExtraction.ExtractM0Stages(hlsl, ToHlslSourceName(effectiveSourceName), diagnostics);
        if (diagnostics.Any(x => x.Severity == SdslvSpirvShaderArtifactDiagnosticSeverity.Error))
        {
            return Create(effectiveSourceName, sourceSha256, hlsl, spirvArtifact, diagnostics);
        }

        spirvArtifact = SpirvShaderArtifactEmitter.EmitFromHlslStages(stages);
        if (spirvArtifact.Diagnostics.Any(x => x.Code == SpirvShaderArtifactDiagnosticCodes.DxcUnavailable))
        {
            diagnostics.Add(new SdslvSpirvShaderArtifactDiagnostic(
                SdslvSpirvShaderArtifactDiagnosticCodes.SpirvCompilationUnavailable,
                SdslvSpirvShaderArtifactDiagnosticSeverity.Error,
                "DXC is unavailable; SDSL-V generated HLSL could not be compiled to SPIR-V."));
        }
        else if (!spirvArtifact.Success)
        {
            diagnostics.Add(new SdslvSpirvShaderArtifactDiagnostic(
                SdslvSpirvShaderArtifactDiagnosticCodes.SpirvCompilationFailed,
                SdslvSpirvShaderArtifactDiagnosticSeverity.Error,
                BuildSpirvFailureMessage(spirvArtifact)));
        }

        return Create(effectiveSourceName, sourceSha256, hlsl, spirvArtifact, diagnostics);
    }

    private static SdslvSpirvShaderArtifact Create(
        string sourceName,
        string sourceSha256,
        string hlsl,
        SpirvShaderArtifact? spirvArtifact,
        IReadOnlyList<SdslvSpirvShaderArtifactDiagnostic> diagnostics) => new(
            SdslvSpirvShaderArtifact.CurrentFormatVersion,
            SdslvSpirvShaderArtifact.LanguageName,
            sourceName,
            sourceSha256,
            hlsl,
            spirvArtifact,
            diagnostics);

    private static void AddDiagnosticSummary(
        List<SdslvSpirvShaderArtifactDiagnostic> diagnostics,
        string code,
        string summary,
        IReadOnlyList<SdslvDiagnostic> sourceDiagnostics)
    {
        var details = sourceDiagnostics.Count == 0
            ? summary
            : summary + " " + string.Join(" ", sourceDiagnostics.Select(FormatSourceDiagnostic));

        diagnostics.Add(new SdslvSpirvShaderArtifactDiagnostic(
            code,
            SdslvSpirvShaderArtifactDiagnosticSeverity.Error,
            details));
    }

    private static string FormatSourceDiagnostic(SdslvDiagnostic diagnostic) =>
        $"{diagnostic.Code}: {diagnostic.Message}";

    private static string BuildSpirvFailureMessage(SpirvShaderArtifact artifact)
    {
        var details = artifact.Diagnostics.Count == 0
            ? "No SPIR-V stage artifacts were produced."
            : string.Join(" ", artifact.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

        return "SPIR-V compilation failed for SDSL-V generated HLSL. " + details;
    }

    private static string ComputeSha256Utf8(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string ToHlslSourceName(string sourceName) =>
        Path.ChangeExtension(sourceName, ".hlsl") ?? "shader.hlsl";
}
