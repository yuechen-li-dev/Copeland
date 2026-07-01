using System.Collections.Generic;

namespace Aurelian.Rendering.Contracts.Shaders;

public sealed record CompiledShaderProgramResult(
    CompiledShaderStatus Status,
    CompiledShaderProgram? Program,
    IReadOnlyList<CompiledShaderDiagnostic> Diagnostics)
{
    public bool Success => Status == CompiledShaderStatus.Created && Program is not null;
}
