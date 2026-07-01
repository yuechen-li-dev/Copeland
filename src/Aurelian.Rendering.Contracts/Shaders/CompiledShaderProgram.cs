using System.Collections.Generic;

namespace Aurelian.Rendering.Contracts.Shaders;

public sealed record CompiledShaderProgram(
    string FormatVersion,
    IReadOnlyList<CompiledShaderStage> Stages)
{
    public const string CurrentFormatVersion = "aurelian.compiled-shader-program/0";
}
