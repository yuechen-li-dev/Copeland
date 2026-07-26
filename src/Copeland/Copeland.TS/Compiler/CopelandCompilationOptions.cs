namespace Copeland.TS.Compiler;

public sealed class CopelandCompilationOptions
{
    public CopelandCompilationStage TargetStage { get; init; } = CopelandCompilationStage.Mir;

    public string? ModuleName { get; init; }

    public string? SourcePath { get; init; }

    public string? ProjectRoot { get; init; }

    public ICopelandAssetSource? AssetSource { get; init; }

    /// <summary>The manifest-derived, resolved npm graph consumed by this compilation.</summary>
    public CopelandNpmDependencyGraph? NpmDependencies { get; init; }

    /// <summary>
    /// Narrow test seam retained for focused compiler tests. Production callers
    /// must provide <see cref="NpmDependencies"/> from manifest IR.
    /// </summary>
    public IReadOnlyList<CopelandNpmPackageContract> NpmPackages { get; init; } = [];

    /// <summary>Already-compiled CLR assemblies available to CLR <c>using</c> binding.</summary>
    public IReadOnlyList<CopelandClrReference> ClrReferences { get; init; } = [];
}
