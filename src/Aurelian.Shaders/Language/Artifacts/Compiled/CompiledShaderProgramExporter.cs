using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Language.Artifacts.SdslvSpirv;
using Aurelian.Shaders.Language.Artifacts.Spirv;

namespace Aurelian.Shaders.Language.Artifacts.Compiled;

public static class CompiledShaderProgramExporter
{
    public static CompiledShaderProgramExportResult FromSpirvArtifact(SpirvShaderArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var diagnostics = new List<CompiledShaderProgramExportDiagnostic>();
        if (!artifact.Success)
        {
            foreach (SpirvShaderArtifactDiagnostic diagnostic in artifact.Diagnostics)
            {
                diagnostics.Add(new CompiledShaderProgramExportDiagnostic(
                    CompiledShaderProgramExportDiagnosticCodes.ArtifactFailed,
                    MapSeverity(diagnostic.Severity),
                    $"SPIR-V artifact diagnostic {diagnostic.Code}: {diagnostic.Message}"));
            }

            if (diagnostics.Count == 0)
            {
                diagnostics.Add(Error(
                    CompiledShaderProgramExportDiagnosticCodes.ArtifactFailed,
                    "SPIR-V artifact did not succeed and provided no diagnostics."));
            }

            return new CompiledShaderProgramExportResult(CompiledShaderStatus.Failed, null, diagnostics);
        }

        IReadOnlyList<CompiledShaderStage> stages = ExportStages(artifact.Stages, diagnostics);
        if (diagnostics.Any(x => x.Severity == CompiledShaderDiagnosticSeverity.Error))
        {
            return new CompiledShaderProgramExportResult(CompiledShaderStatus.Rejected, null, diagnostics);
        }

        return new CompiledShaderProgramExportResult(
            CompiledShaderStatus.Created,
            new CompiledShaderProgram(CompiledShaderProgram.CurrentFormatVersion, stages),
            diagnostics);
    }

    public static CompiledShaderProgramExportResult FromSdslvSpirvArtifact(SdslvSpirvShaderArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (artifact.SpirvArtifact is null)
        {
            var sdslvDiagnostics = artifact.Diagnostics
                .Select(x => new CompiledShaderProgramExportDiagnostic(
                    CompiledShaderProgramExportDiagnosticCodes.MissingArtifact,
                    MapSeverity(x.Severity),
                    $"SDSL-V SPIR-V artifact diagnostic {x.Code}: {x.Message}"))
                .ToList();
            if (sdslvDiagnostics.Count == 0)
            {
                sdslvDiagnostics.Add(Error(
                    CompiledShaderProgramExportDiagnosticCodes.MissingArtifact,
                    "SDSL-V SPIR-V artifact does not contain a nested SPIR-V artifact."));
            }

            return new CompiledShaderProgramExportResult(CompiledShaderStatus.Failed, null, sdslvDiagnostics);
        }

        CompiledShaderProgramExportResult result = FromSpirvArtifact(artifact.SpirvArtifact);
        if (artifact.Diagnostics.All(x => x.Severity != SdslvSpirvShaderArtifactDiagnosticSeverity.Error))
        {
            return result;
        }

        List<CompiledShaderProgramExportDiagnostic> diagnostics = [.. result.Diagnostics];
        diagnostics.AddRange(artifact.Diagnostics.Select(x => new CompiledShaderProgramExportDiagnostic(
            CompiledShaderProgramExportDiagnosticCodes.ArtifactFailed,
            MapSeverity(x.Severity),
            $"SDSL-V SPIR-V artifact diagnostic {x.Code}: {x.Message}")));
        return new CompiledShaderProgramExportResult(CompiledShaderStatus.Failed, null, diagnostics);
    }

    private static IReadOnlyList<CompiledShaderStage> ExportStages(
        IReadOnlyList<SpirvShaderStageArtifact> stages,
        List<CompiledShaderProgramExportDiagnostic> diagnostics)
    {
        if (stages is null || stages.Count == 0)
        {
            diagnostics.Add(Error(CompiledShaderProgramExportDiagnosticCodes.MissingStages, "At least one compiled shader stage is required."));
            return [];
        }

        foreach (IGrouping<HlslShaderStageKind, SpirvShaderStageArtifact> group in stages.GroupBy(x => x.Stage).Where(x => x.Count() > 1))
        {
            diagnostics.Add(Error(
                CompiledShaderProgramExportDiagnosticCodes.DuplicateStage,
                $"Duplicate compiled shader stage '{group.Key}' is not supported.",
                MapStage(group.Key)));
        }

        var compiledStages = new List<CompiledShaderStage>();
        foreach (SpirvShaderStageArtifact stage in stages)
        {
            CompiledShaderStageKind compiledStage = MapStage(stage.Stage);
            if (string.IsNullOrWhiteSpace(stage.EntryPoint))
            {
                diagnostics.Add(Error(
                    CompiledShaderProgramExportDiagnosticCodes.MissingEntryPoint,
                    $"{compiledStage} shader entry point must not be empty.",
                    compiledStage));
            }

            if (stage.SpirvBytes is null || stage.SpirvBytes.Length == 0)
            {
                diagnostics.Add(Error(
                    CompiledShaderProgramExportDiagnosticCodes.EmptySpirv,
                    $"{compiledStage} shader SPIR-V bytes must not be empty.",
                    compiledStage));
            }

            if (string.IsNullOrWhiteSpace(stage.SpirvSha256) || stage.SpirvSha256.Length != 64)
            {
                diagnostics.Add(Error(
                    CompiledShaderProgramExportDiagnosticCodes.InvalidSpirvHash,
                    $"{compiledStage} shader SPIR-V SHA-256 hash must be 64 lowercase hexadecimal characters.",
                    compiledStage));
            }

            compiledStages.Add(new CompiledShaderStage(
                compiledStage,
                stage.EntryPoint,
                stage.Profile,
                stage.SpirvBytes ?? [],
                stage.SpirvSha256,
                stage.SourceName));
        }

        return diagnostics.Any(x => x.Severity == CompiledShaderDiagnosticSeverity.Error) ? [] : compiledStages;
    }

    private static CompiledShaderStageKind MapStage(HlslShaderStageKind stage)
        => stage switch
        {
            HlslShaderStageKind.Vertex => CompiledShaderStageKind.Vertex,
            HlslShaderStageKind.Fragment => CompiledShaderStageKind.Fragment,
            HlslShaderStageKind.Compute => CompiledShaderStageKind.Compute,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unsupported HLSL shader stage."),
        };

    private static CompiledShaderDiagnosticSeverity MapSeverity(SpirvShaderArtifactDiagnosticSeverity severity)
        => severity switch
        {
            SpirvShaderArtifactDiagnosticSeverity.Error => CompiledShaderDiagnosticSeverity.Error,
            SpirvShaderArtifactDiagnosticSeverity.Warning => CompiledShaderDiagnosticSeverity.Warning,
            SpirvShaderArtifactDiagnosticSeverity.Info => CompiledShaderDiagnosticSeverity.Info,
            _ => CompiledShaderDiagnosticSeverity.Error,
        };

    private static CompiledShaderDiagnosticSeverity MapSeverity(SdslvSpirvShaderArtifactDiagnosticSeverity severity)
        => severity switch
        {
            SdslvSpirvShaderArtifactDiagnosticSeverity.Error => CompiledShaderDiagnosticSeverity.Error,
            SdslvSpirvShaderArtifactDiagnosticSeverity.Warning => CompiledShaderDiagnosticSeverity.Warning,
            SdslvSpirvShaderArtifactDiagnosticSeverity.Info => CompiledShaderDiagnosticSeverity.Info,
            _ => CompiledShaderDiagnosticSeverity.Error,
        };

    private static CompiledShaderProgramExportDiagnostic Error(string code, string message, CompiledShaderStageKind? stage = null)
        => new(code, CompiledShaderDiagnosticSeverity.Error, message, stage);
}
