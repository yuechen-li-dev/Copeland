namespace Copeland.TS.Compiler;

public sealed class CopelandCompilationOptions
{
    public CopelandCompilationStage TargetStage { get; init; } = CopelandCompilationStage.Mir;

    public string? ModuleName { get; init; }

    public string? SourcePath { get; init; }

    public string? ProjectRoot { get; init; }

    /// <summary>
    /// Selects the semantic owner for renderer-neutral TS-XML syntax. The
    /// default deliberately remains None: a .tsx file alone never selects
    /// React semantics.
    /// </summary>
    public CopelandTsXmlProfile TsXmlProfile { get; init; } = CopelandTsXmlProfile.None;

    public ICopelandAssetSource? AssetSource { get; init; }

    /// <summary>The manifest-derived, resolved npm graph consumed by this compilation.</summary>
    public CopelandNpmDependencyGraph? NpmDependencies { get; init; }

    /// <summary>
    /// Narrow test seam retained for focused compiler tests. Production callers
    /// must provide <see cref="NpmDependencies"/> from manifest IR.
    /// </summary>
    public IReadOnlyList<CopelandNpmPackageContract> NpmPackages { get; init; } = [];

    /// <summary>
    /// Compiler-owned host modules available to native JavaScript emission.
    /// This intentionally remains separate from npm's transport contract.
    /// </summary>
    public IReadOnlyList<CopelandJavaScriptHostModuleContract> JavaScriptHostModules { get; init; } = [];

    /// <summary>Explicit contracts supplied by MSBuild project/package items; the compiler never discovers them from NuGet storage.</summary>
    public IReadOnlyList<CopelandPackageContract> PackageContracts { get; init; } = [];

    public CopelandPackageBackend PackageBackend { get; init; } = CopelandPackageBackend.Clr;

    /// <summary>Already-compiled CLR assemblies available to CLR <c>using</c> binding.</summary>
    public IReadOnlyList<CopelandClrReference> ClrReferences { get; init; } = [];
}

public enum CopelandTsXmlProfile
{
    None,
    ReactM0,
}
