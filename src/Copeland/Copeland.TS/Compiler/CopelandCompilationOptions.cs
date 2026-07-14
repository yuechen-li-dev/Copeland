namespace Copeland.TS.Compiler;

public sealed class CopelandCompilationOptions
{
    public CopelandCompilationStage TargetStage { get; init; } = CopelandCompilationStage.Mir;

    public string? ModuleName { get; init; }

    public string? SourcePath { get; init; }

    public string? ProjectRoot { get; init; }

    public ICopelandAssetSource? AssetSource { get; init; }
}
