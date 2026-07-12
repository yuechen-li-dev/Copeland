using System.Collections.Generic;
using Aurelian.Rendering.Contracts.Shaders;

namespace Aurelian.Shaders.Language.Artifacts.Compiled;

public sealed record CompiledShaderProgramExportResult(
    CompiledShaderStatus Status,
    CompiledShaderProgram? Program,
    IReadOnlyList<CompiledShaderProgramExportDiagnostic> Diagnostics)
{
    public bool Success => Status == CompiledShaderStatus.Created && Program is not null;
}
