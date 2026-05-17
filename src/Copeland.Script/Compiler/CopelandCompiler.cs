using Copeland.Script.Codegen.CSharp;
using Copeland.Script.Diagnostics;
using Copeland.Script.Mir;
using Copeland.Script.Semantics;
using Copeland.Script.Semantics.Bound;
using Copeland.Script.Syntax;

namespace Copeland.Script.Compiler;

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
        CSharpCompilation? csharpCompilation = null;
        string? mirText = null;
        string? csharpText = null;

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

        if (effectiveOptions.TargetStage >= CopelandCompilationStage.CSharp)
        {
            if (diagnostics.Count == 0 && mirCompilation?.Program is not null)
            {
                csharpCompilation = CSharpBackend.Emit(mirCompilation.Program);
                diagnostics.AddRange(csharpCompilation.Diagnostics);
                if (csharpCompilation.Diagnostics.Count == 0)
                {
                    csharpText = csharpCompilation.SourceText;
                }
            }
        }

        return new CopelandCompilation(
            effectiveOptions.TargetStage,
            diagnostics,
            syntaxTree,
            boundCompilation,
            mirCompilation,
            csharpCompilation,
            mirText,
            csharpText);
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

    public static CopelandCompilation CompileToCSharp(string sourceText, CopelandCompilationOptions? options = null)
    {
        var effectiveOptions = options ?? new CopelandCompilationOptions();
        return Compile(sourceText, new CopelandCompilationOptions
        {
            TargetStage = CopelandCompilationStage.CSharp,
            ModuleName = effectiveOptions.ModuleName,
        });
    }

}
