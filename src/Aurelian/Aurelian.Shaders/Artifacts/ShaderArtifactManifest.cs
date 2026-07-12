using Aurelian.Shaders.Diagnostics;

namespace Aurelian.Shaders.Artifacts;

public sealed class ShaderArtifactManifest
{
    public string Format { get; init; } = "striv.shader.artifact.v1";
    public int ArtifactVersion { get; init; } = 1;
    public string SourcePath { get; init; } = string.Empty;
    public string SourceHash { get; init; } = string.Empty;
    public string EntryShader { get; init; } = string.Empty;
    public string SpecializationKey { get; init; } = string.Empty;
    public string BackendProfile { get; init; } = "vulkan";
    public string Compiler { get; init; } = "dxc";
    public string CompilerVersion { get; init; } = "unavailable";
    public string TargetFamily { get; init; } = "spirv";

    public List<ShaderArtifactStageRecord> Stages { get; init; } = [];
    public List<ShaderArtifactSpecializationRecord> Specializations { get; init; } = [];
    public List<ShaderArtifactIoRecord> Io { get; init; } = [];
    public List<string> EffectUsingParams { get; init; } = [];
    public List<string> EffectMixins { get; init; } = [];
    public List<ShaderArtifactDiagnosticRecord> Diagnostics { get; init; } = [];
}

public sealed record ShaderArtifactStageRecord(string Stage, string EntryPoint, string TargetProfile, string GeneratedHlslPath, string BinaryPath, string BinaryHash);
public sealed record ShaderArtifactSpecializationRecord(string Shader, string Name, string Type, string Value);
public sealed record ShaderArtifactIoRecord(string Stage, string Direction, string Name, string Type, string Semantic, int Index);
public sealed record ShaderArtifactDiagnosticRecord(string Code, string Severity, string Phase, string Stage, string Message, string SourcePath, int Line, int Column, int Length, bool Fatal)
{
    public static ShaderArtifactDiagnosticRecord FromDiagnostic(Diagnostic diagnostic)
        => new(
            diagnostic.Code,
            "warning",
            InferPhase(diagnostic.Code),
            "none",
            diagnostic.Message,
            string.Empty,
            diagnostic.Line,
            diagnostic.Column,
            0,
            false);

    private static string InferPhase(string code)
    {
        if (code.StartsWith("SD3", StringComparison.Ordinal))
            return "lower";

        return "parse";
    }
}
