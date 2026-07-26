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

        var syntaxTree = SyntaxTree.Parse(sourceText, effectiveOptions.SourcePath);
        diagnostics.AddRange(syntaxTree.Diagnostics);

        BoundCompilation? boundCompilation = null;
        MirCompilation? mirCompilation = null;
        string? mirText = null;
        CopelandAssetResolver? assetResolver = CreateAssetResolver(effectiveOptions);

        if (effectiveOptions.TargetStage >= CopelandCompilationStage.Bound)
        {
            if (diagnostics.Count == 0)
            {
                CopelandNpmDependencyGraph npmDependencies = effectiveOptions.NpmDependencies
                    ?? new CopelandNpmDependencyGraph(effectiveOptions.NpmPackages);
                boundCompilation = Binder.Bind(
                    syntaxTree,
                    assetResolver,
                    new CopelandNpmContractResolver(npmDependencies),
                    new CopelandJavaScriptHostContractResolver(effectiveOptions.JavaScriptHostModules),
                    new CopelandClrMetadataResolver(effectiveOptions.ClrReferences),
                    effectiveOptions.SourcePath);
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
            mirText,
            assetResolver?.Dependencies);
    }

    public static CopelandCompilation CompileToMir(string sourceText, CopelandCompilationOptions? options = null)
    {
        var effectiveOptions = options ?? new CopelandCompilationOptions();
        return Compile(sourceText, new CopelandCompilationOptions
        {
            TargetStage = CopelandCompilationStage.Mir,
            ModuleName = effectiveOptions.ModuleName,
            SourcePath = effectiveOptions.SourcePath,
            ProjectRoot = effectiveOptions.ProjectRoot,
            AssetSource = effectiveOptions.AssetSource,
            NpmDependencies = effectiveOptions.NpmDependencies,
            NpmPackages = effectiveOptions.NpmPackages,
            JavaScriptHostModules = effectiveOptions.JavaScriptHostModules,
            ClrReferences = effectiveOptions.ClrReferences,
        });
    }

    private static CopelandAssetResolver? CreateAssetResolver(CopelandCompilationOptions options)
    {
        if (options.SourcePath is null || options.ProjectRoot is null || options.AssetSource is null)
        {
            return null;
        }

        return new CopelandAssetResolver(options.SourcePath, options.ProjectRoot, options.AssetSource);
    }

}
