namespace Copeland.Script.Compiler;

public sealed class CopelandCompilationOptions
{
    public CopelandCompilationStage TargetStage { get; init; } = CopelandCompilationStage.CSharp;

    public string? ModuleName { get; init; }
}
