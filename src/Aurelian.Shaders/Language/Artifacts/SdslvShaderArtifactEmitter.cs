using Aurelian.Shaders.Language.Ast;
using Aurelian.Shaders.Language.Diagnostics;
using Aurelian.Shaders.Language.Emission.Hlsl;
using Aurelian.Shaders.Language.Parsing;
using Aurelian.Shaders.Language.Validation;

namespace Aurelian.Shaders.Language.Artifacts;

public static class SdslvShaderArtifactEmitter
{
    public static SdslvShaderArtifact Emit(SdslvShaderSource source, SdslvShaderArtifactOptions? options = null)
    {
        options ??= SdslvShaderArtifactOptions.Default;

        var sourceHash = SdslvShaderSourceHash.ComputeSha256(source.SourceText);
        var diagnostics = new List<SdslvDiagnostic>();
        var stages = new List<SdslvShaderArtifactStage>();
        var hlsl = string.Empty;

        var parse = SdslvParser.ParseModule(source.SourceText);
        diagnostics.AddRange(parse.Diagnostics);

        if (parse.Module is not null)
        {
            stages.AddRange(CollectStages(parse.Module));
        }

        var parseSucceeded = parse.Success && parse.Module is not null;
        var validationSucceeded = false;

        if (parseSucceeded)
        {
            var validation = SdslvValidator.ValidateModule(parse.Module!);
            diagnostics.AddRange(validation.Diagnostics);
            validationSucceeded = validation.Success;
        }

        if (parseSucceeded && (validationSucceeded || options.EmitPartialHlslOnError))
        {
            var emission = HlslEmitter.EmitModule(parse.Module!);
            diagnostics.AddRange(emission.Diagnostics);
            if (emission.Success || options.EmitPartialHlslOnError)
            {
                hlsl = emission.Hlsl;
            }
        }

        return new SdslvShaderArtifact(
            options.FormatVersion,
            SdslvShaderArtifact.LanguageName,
            source.DisplayName,
            sourceHash,
            hlsl,
            stages,
            diagnostics);
    }

    private static IEnumerable<SdslvShaderArtifactStage> CollectStages(SdslvModule module)
    {
        foreach (var shader in module.Declarations.OfType<SdslvShaderDecl>())
        {
            foreach (var stageMethod in shader.StageMethods)
            {
                var stage = InferStageKind(stageMethod);
                yield return new SdslvShaderArtifactStage(stageMethod.Name, stage, ProfileFor(stage));
            }
        }
    }

    private static SdslvShaderStageKind InferStageKind(SdslvFunctionDecl stageMethod)
    {
        if (TryInferStageKind(stageMethod.Stage, out var explicitStage))
        {
            return explicitStage;
        }

        return TryInferStageKind(stageMethod.Name, out var inferredStage) ? inferredStage : SdslvShaderStageKind.Unknown;
    }

    private static bool TryInferStageKind(string? text, out SdslvShaderStageKind stage)
    {
        stage = SdslvShaderStageKind.Unknown;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.StartsWith("VS", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Vertex", StringComparison.OrdinalIgnoreCase))
        {
            stage = SdslvShaderStageKind.Vertex;
            return true;
        }

        if (text.StartsWith("PS", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Pixel", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Fragment", StringComparison.OrdinalIgnoreCase))
        {
            stage = SdslvShaderStageKind.Pixel;
            return true;
        }

        if (text.StartsWith("CS", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Compute", StringComparison.OrdinalIgnoreCase))
        {
            stage = SdslvShaderStageKind.Compute;
            return true;
        }

        return false;
    }

    private static string? ProfileFor(SdslvShaderStageKind stage) => stage switch
    {
        SdslvShaderStageKind.Vertex => "vs_6_0",
        SdslvShaderStageKind.Pixel => "ps_6_0",
        SdslvShaderStageKind.Compute => "cs_6_0",
        _ => null,
    };
}
