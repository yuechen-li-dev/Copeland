using Copeland.Script.Codegen.CSharp;
using Copeland.Script.Diagnostics;
using Copeland.Script.Mir;
using Copeland.Script.Semantics.Bound;
using Copeland.Script.Syntax;

namespace Copeland.Script.Compiler;

public sealed class CopelandCompilation
{
    public CopelandCompilation(
        CopelandCompilationStage targetStage,
        IReadOnlyList<Diagnostic> diagnostics,
        SyntaxTree? syntaxTree,
        BoundCompilation? boundCompilation,
        MirCompilation? mirCompilation,
        CSharpCompilation? csharpCompilation,
        string? mirText,
        string? csharpText)
    {
        TargetStage = targetStage;
        Diagnostics = diagnostics;
        SyntaxTree = syntaxTree;
        BoundCompilation = boundCompilation;
        MirCompilation = mirCompilation;
        CSharpCompilation = csharpCompilation;
        MirText = mirText;
        CSharpText = csharpText;
    }

    public CopelandCompilationStage TargetStage { get; }

    public bool Success => Diagnostics.Count == 0 && ReachedTargetStage;

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public SyntaxTree? SyntaxTree { get; }

    public BoundCompilation? BoundCompilation { get; }

    public MirCompilation? MirCompilation { get; }

    public CSharpCompilation? CSharpCompilation { get; }

    public string? MirText { get; }

    public string? CSharpText { get; }

    private bool ReachedTargetStage => TargetStage switch
    {
        CopelandCompilationStage.Syntax => SyntaxTree is not null,
        CopelandCompilationStage.Bound => BoundCompilation is not null,
        CopelandCompilationStage.Mir => MirCompilation?.Program is not null,
        CopelandCompilationStage.CSharp => CSharpCompilation is not null && CSharpText is not null,
        _ => false,
    };
}
