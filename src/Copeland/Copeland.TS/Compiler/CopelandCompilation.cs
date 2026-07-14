using Copeland.TS.Diagnostics;
using Copeland.TS.Lowering;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Compiler;

public sealed class CopelandCompilation
{
    public CopelandCompilation(
        CopelandCompilationStage targetStage,
        IReadOnlyList<Diagnostic> diagnostics,
        SyntaxTree? syntaxTree,
        BoundCompilation? boundCompilation,
        MirCompilation? mirCompilation,
        string? mirText,
        IReadOnlyList<CopelandAssetDependency>? assetDependencies = null)
    {
        TargetStage = targetStage;
        Diagnostics = diagnostics;
        SyntaxTree = syntaxTree;
        BoundCompilation = boundCompilation;
        MirCompilation = mirCompilation;
        MirText = mirText;
        AssetDependencies = assetDependencies ?? [];
    }

    public CopelandCompilationStage TargetStage { get; }

    public bool Success => Diagnostics.Count == 0 && ReachedTargetStage;

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public SyntaxTree? SyntaxTree { get; }

    public BoundCompilation? BoundCompilation { get; }

    public MirCompilation? MirCompilation { get; }

    public string? MirText { get; }

    public IReadOnlyList<CopelandAssetDependency> AssetDependencies { get; }

    private bool ReachedTargetStage => TargetStage switch
    {
        CopelandCompilationStage.Syntax => SyntaxTree is not null,
        CopelandCompilationStage.Bound => BoundCompilation is not null,
        CopelandCompilationStage.Mir => MirCompilation?.Program is not null,
        _ => false,
    };
}
