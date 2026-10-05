namespace Copeland.TS.Mir;

/// <summary>
/// Stable project-owned identity for a Copeland source module. The value is a
/// normalized project-relative path using forward slashes, never a generated
/// backend name or a file-system enumeration position.
/// </summary>
public sealed record MirModuleId(string Value)
{
    public override string ToString() => Value;
}

public sealed record MirModuleImport(
    string Specifier,
    MirModuleId? TargetModule,
    string ExportedName,
    string LocalName);

public sealed record MirModuleExport(string Name, string DeclarationKind, string? RuntimeName = null);

public sealed class MirProjectModule(
    MirModuleId id,
    IReadOnlyList<MirModuleImport> imports,
    IReadOnlyList<MirModuleExport> exports,
    IReadOnlyList<string> privateDeclarations,
    IReadOnlyList<MirFunction> functions,
    IReadOnlyList<MirNpmImport>? npmImports = null,
    IReadOnlyList<MirJavaScriptHostImport>? javaScriptHostImports = null)
{
    public MirModuleId Id { get; } = id;
    public IReadOnlyList<MirModuleImport> Imports { get; } = imports;
    public IReadOnlyList<MirModuleExport> Exports { get; } = exports;
    public IReadOnlyList<string> PrivateDeclarations { get; } = privateDeclarations;
    public IReadOnlyList<MirFunction> Functions { get; } = functions;
    public IReadOnlyList<MirNpmImport> NpmImports { get; } = npmImports ?? [];
    public IReadOnlyList<MirJavaScriptHostImport> JavaScriptHostImports { get; } = javaScriptHostImports ?? [];
}

/// <summary>
/// Backend-neutral project graph retained beside the aggregate program during
/// the M1 transition. Module members reference the exact MIR declarations from
/// <see cref="AggregateProgram"/>; imported aliases are metadata, not new
/// symbols or reconstructed lookalikes.
/// </summary>
public sealed class MirProjectGraph(MirProgram aggregateProgram, IReadOnlyList<MirProjectModule> modules)
{
    public MirProgram AggregateProgram { get; } = aggregateProgram;
    public IReadOnlyList<MirProjectModule> Modules { get; } = modules;
}
