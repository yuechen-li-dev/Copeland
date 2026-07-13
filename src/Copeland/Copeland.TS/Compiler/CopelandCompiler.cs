using Copeland.TS.Diagnostics;
using Copeland.TS.Lowering;
using Copeland.TS.Mir;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Compiler;

public static class CopelandCompiler
{
    public static CopelandCompilation Compile(string sourceText, CopelandCompilationOptions? options = null)
    {
        var effectiveOptions = options ?? new CopelandCompilationOptions();
        var diagnostics = new List<Diagnostic>();

        var syntaxTree = SyntaxTree.Parse(sourceText);
        diagnostics.AddRange(syntaxTree.Diagnostics);

        BoundCompilation? boundCompilation = null;
        MirCompilation? mirCompilation = null;
        string? mirText = null;

        if (effectiveOptions.TargetStage >= CopelandCompilationStage.Bound)
        {
            if (diagnostics.Count == 0)
            {
                boundCompilation = Binder.Bind(syntaxTree);
                diagnostics.AddRange(boundCompilation.Diagnostics);
            }
        }

        if (effectiveOptions.TargetStage >= CopelandCompilationStage.Mir)
        {
            if (diagnostics.Count == 0)
            {
                mirCompilation = MirLowerer.Lower(boundCompilation!);
                if (mirCompilation.Program is not null)
                    mirText = MirTextWriter.Write(mirCompilation.Program);
            }
        }

        return new CopelandCompilation(
            effectiveOptions.TargetStage,
            diagnostics,
            syntaxTree,
            boundCompilation,
            mirCompilation,
            mirText);
    }

    public static CopelandCompilation CompileToMir(string sourceText, CopelandCompilationOptions? options = null)
    {
        var effectiveOptions = options ?? new CopelandCompilationOptions();
        return Compile(sourceText, new CopelandCompilationOptions
        {
            TargetStage = CopelandCompilationStage.Mir,
            ModuleName = effectiveOptions.ModuleName,
        });
    }

}
