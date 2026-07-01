using Aurelian.Shaders.Language.Artifacts.Spirv;

namespace Aurelian.Shaders.Language.Artifacts.SdslvSpirv;

public static class SdslvStageExtraction
{
    public const string VertexEntryPoint = "VSMain";
    public const string FragmentEntryPoint = "PSMain";
    public const string VertexProfile = "vs_6_0";
    public const string FragmentProfile = "ps_6_0";

    public static IReadOnlyList<HlslShaderStageSource> ExtractM0Stages(
        string hlsl,
        string sourceName,
        ICollection<SdslvSpirvShaderArtifactDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var stages = new List<HlslShaderStageSource>();
        if (string.IsNullOrWhiteSpace(hlsl))
        {
            diagnostics.Add(Error(
                SdslvSpirvShaderArtifactDiagnosticCodes.StageExtractionFailed,
                "Generated HLSL is empty; no HLSL stages can be extracted."));
            return stages;
        }

        if (!ContainsEntryPoint(hlsl, VertexEntryPoint))
        {
            diagnostics.Add(Error(
                SdslvSpirvShaderArtifactDiagnosticCodes.MissingVertexStage,
                "SDSL-V SPIR-V artifact M0 requires a generated HLSL vertex entry point named 'VSMain'."));
        }
        else
        {
            stages.Add(new HlslShaderStageSource(
                HlslShaderStageKind.Vertex,
                hlsl,
                VertexEntryPoint,
                VertexProfile,
                sourceName));
        }

        if (!ContainsEntryPoint(hlsl, FragmentEntryPoint))
        {
            diagnostics.Add(Error(
                SdslvSpirvShaderArtifactDiagnosticCodes.MissingFragmentStage,
                "SDSL-V SPIR-V artifact M0 requires a generated HLSL fragment entry point named 'PSMain'."));
        }
        else
        {
            stages.Add(new HlslShaderStageSource(
                HlslShaderStageKind.Fragment,
                hlsl,
                FragmentEntryPoint,
                FragmentProfile,
                sourceName));
        }

        if (stages.Count == 0 && !diagnostics.Any(x => x.Code == SdslvSpirvShaderArtifactDiagnosticCodes.StageExtractionFailed))
        {
            diagnostics.Add(Error(
                SdslvSpirvShaderArtifactDiagnosticCodes.StageExtractionFailed,
                "Generated HLSL did not contain any M0-supported entry points."));
        }

        return stages;
    }

    private static bool ContainsEntryPoint(string hlsl, string entryPoint) =>
        hlsl.Contains(entryPoint, StringComparison.Ordinal);

    private static SdslvSpirvShaderArtifactDiagnostic Error(string code, string message) => new(
        code,
        SdslvSpirvShaderArtifactDiagnosticSeverity.Error,
        message);
}
