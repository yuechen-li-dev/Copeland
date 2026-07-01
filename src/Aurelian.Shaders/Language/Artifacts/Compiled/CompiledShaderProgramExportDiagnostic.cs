using Aurelian.Rendering.Contracts.Shaders;

namespace Aurelian.Shaders.Language.Artifacts.Compiled;

public sealed record CompiledShaderProgramExportDiagnostic(
    string Code,
    CompiledShaderDiagnosticSeverity Severity,
    string Message,
    CompiledShaderStageKind? Stage = null);
