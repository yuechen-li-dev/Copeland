using Copeland.TS.Diagnostics;
using Copeland.TS.Lowering;
using Copeland.TS.Mir;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Copeland.TS.Templates;

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
                    new CopelandPackageContractMap(effectiveOptions.PackageContracts),
                    effectiveOptions.PackageBackend,
                    effectiveOptions.ProjectTypes,
                    effectiveOptions.SourcePath);
                diagnostics.AddRange(boundCompilation.Diagnostics);
                if (boundCompilation.Diagnostics.Count == 0)
                {
                    var sourcePaths = new Dictionary<BoundCompilation, string?>
                    {
                        [boundCompilation] = effectiveOptions.SourcePath,
                    };
                    IReadOnlyList<Diagnostic> staticDiagnostics = StaticEvaluationPass.Evaluate(
                        [boundCompilation],
                        sourcePaths: sourcePaths);
                    if (staticDiagnostics.Count > 0)
                    {
                        diagnostics.AddRange(staticDiagnostics);
                        boundCompilation = new BoundCompilation(
                            boundCompilation.SyntaxTree,
                            boundCompilation.Program,
                            boundCompilation.Diagnostics.Concat(staticDiagnostics).ToArray(),
                            boundCompilation.ModuleScope,
                            boundCompilation.TextDocuments);
                    }
                }
                if (boundCompilation.Program.Templates.Count > 0)
                {
                    if (effectiveOptions.TargetStage >= CopelandCompilationStage.Mir)
                    {
                        diagnostics.Add(new Diagnostic(
                            "COPE-TEMPLATE-0006",
                            "Template source is structural input. Use 'tscl template preview' or 'tscl template materialize' instead of runtime emit.",
                            0,
                            0,
                            effectiveOptions.SourcePath));
                    }
                    return new CopelandCompilation(effectiveOptions.TargetStage, diagnostics, syntaxTree, boundCompilation, null, null, assetResolver?.Dependencies);
                }
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
            ProjectTypes = effectiveOptions.ProjectTypes,
            AssetSource = effectiveOptions.AssetSource,
            NpmDependencies = effectiveOptions.NpmDependencies,
            NpmPackages = effectiveOptions.NpmPackages,
            JavaScriptHostModules = effectiveOptions.JavaScriptHostModules,
            PackageContracts = effectiveOptions.PackageContracts,
            PackageBackend = effectiveOptions.PackageBackend,
            ClrReferences = effectiveOptions.ClrReferences,
        });
    }

    /// <summary>Runs the shared parse/bind pipeline and returns its bounded template result.</summary>
    public static CopelandCompilation CompileTemplates(string sourceText, CopelandCompilationOptions? options = null)
    {
        CopelandCompilationOptions effectiveOptions = options ?? new CopelandCompilationOptions();
        return Compile(sourceText, new CopelandCompilationOptions
        {
            TargetStage = CopelandCompilationStage.Bound,
            ModuleName = effectiveOptions.ModuleName,
            SourcePath = effectiveOptions.SourcePath,
            ProjectRoot = effectiveOptions.ProjectRoot,
            ProjectTypes = effectiveOptions.ProjectTypes,
            AssetSource = effectiveOptions.AssetSource,
            NpmDependencies = effectiveOptions.NpmDependencies,
            NpmPackages = effectiveOptions.NpmPackages,
            JavaScriptHostModules = effectiveOptions.JavaScriptHostModules,
            PackageContracts = effectiveOptions.PackageContracts,
            PackageBackend = effectiveOptions.PackageBackend,
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
