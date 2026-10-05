namespace Copeland.TS.Backend.JavaScript;

public sealed class JavaScriptCompilation(
    string? sourceText,
    IReadOnlyList<JavaScriptDiagnostic> diagnostics,
    JavaScriptReachabilityReport? reachability = null)
{
    public string? SourceText { get; } = sourceText;

    public IReadOnlyList<JavaScriptDiagnostic> Diagnostics { get; } = diagnostics;

    /// <summary>Backend-owned generated-definition reachability evidence.</summary>
    public JavaScriptReachabilityReport? Reachability { get; } = reachability;

    public bool Success => SourceText is not null && Diagnostics.Count == 0;
}

public sealed record JavaScriptDiagnostic(string Id, string Message);

public sealed record JavaScriptReachabilityDefinition(
    string StableId,
    string Kind,
    bool IsRoot,
    bool IsReachable,
    int EmittedBytes,
    IReadOnlyList<string> References);

public sealed record JavaScriptReachabilityReport(
    bool Enabled,
    int DefinitionCount,
    int RetainedCount,
    int RemovedCount,
    int RemovedBytes,
    IReadOnlyList<JavaScriptReachabilityDefinition> Definitions);
