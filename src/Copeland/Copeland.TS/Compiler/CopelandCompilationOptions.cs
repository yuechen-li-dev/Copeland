namespace Copeland.TS.Compiler;

public sealed class CopelandCompilationOptions
{
    public CopelandCompilationStage TargetStage { get; init; } = CopelandCompilationStage.Mir;

    public string? ModuleName { get; init; }

    public string? SourcePath { get; init; }

    public string? ProjectRoot { get; init; }

    /// <summary>
    /// The project types available to TS-XML binding. A <c>.tsx</c> extension
    /// alone never selects a type or a renderer.
    /// </summary>
    public CopelandProjectTypeSet ProjectTypes { get; init; } = CopelandProjectTypeSet.None;

    /// <summary>
    /// Legacy test and host boundary. New callers must use <see cref="ProjectTypes"/>.
    /// This value is translated immediately and is not used by binding.
    /// </summary>
    public CopelandTsXmlProfile TsXmlProfile
    {
        get => CopelandProjectTypes.ToLegacy(ProjectTypes);
        init => ProjectTypes = CopelandProjectTypes.FromLegacy(value);
    }

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

[Flags]
public enum CopelandTsXmlProfile
{
    None,
    ReactM0,
    TextDocumentsM0,
    FlowAuthoringM0,
}

[Flags]
public enum CopelandProjectTypeSet
{
    None = 0,
    ReactComponents = 1,
    TextDocuments = 2,
    FlowAuthoring = 4,
}

public static class CopelandProjectTypes
{
    public static CopelandProjectTypeSet FromNames(IEnumerable<string> names, out string? unknownName)
    {
        CopelandProjectTypeSet result = CopelandProjectTypeSet.None;
        foreach (string name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (name.Equals("ReactComponents", StringComparison.OrdinalIgnoreCase)) result |= CopelandProjectTypeSet.ReactComponents;
            else if (name.Equals("TextDocuments", StringComparison.OrdinalIgnoreCase)) result |= CopelandProjectTypeSet.TextDocuments;
            else if (name.Equals("FlowAuthoring", StringComparison.OrdinalIgnoreCase)) result |= CopelandProjectTypeSet.FlowAuthoring;
            else
            {
                unknownName = name;
                return CopelandProjectTypeSet.None;
            }
        }

        unknownName = null;
        return result;
    }

    public static string ToTransport(CopelandProjectTypeSet types)
        => string.Join(",", Names(types));

    public static IReadOnlyList<string> Names(CopelandProjectTypeSet types)
    {
        var names = new List<string>();
        if (types.HasFlag(CopelandProjectTypeSet.TextDocuments)) names.Add("TextDocuments");
        if (types.HasFlag(CopelandProjectTypeSet.ReactComponents)) names.Add("ReactComponents");
        if (types.HasFlag(CopelandProjectTypeSet.FlowAuthoring)) names.Add("FlowAuthoring");
        return names;
    }

    public static CopelandProjectTypeSet FromLegacy(CopelandTsXmlProfile profile)
    {
        CopelandProjectTypeSet types = CopelandProjectTypeSet.None;
        if (profile.HasFlag(CopelandTsXmlProfile.ReactM0)) types |= CopelandProjectTypeSet.ReactComponents;
        if (profile.HasFlag(CopelandTsXmlProfile.TextDocumentsM0)) types |= CopelandProjectTypeSet.TextDocuments;
        if (profile.HasFlag(CopelandTsXmlProfile.FlowAuthoringM0)) types |= CopelandProjectTypeSet.FlowAuthoring;
        return types;
    }

    public static CopelandTsXmlProfile ToLegacy(CopelandProjectTypeSet types)
    {
        CopelandTsXmlProfile profile = CopelandTsXmlProfile.None;
        if (types.HasFlag(CopelandProjectTypeSet.ReactComponents)) profile |= CopelandTsXmlProfile.ReactM0;
        if (types.HasFlag(CopelandProjectTypeSet.TextDocuments)) profile |= CopelandTsXmlProfile.TextDocumentsM0;
        if (types.HasFlag(CopelandProjectTypeSet.FlowAuthoring)) profile |= CopelandTsXmlProfile.FlowAuthoringM0;
        return profile;
    }
}
