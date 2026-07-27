namespace Copeland.TS.Backend.JavaScript;

/// <summary>Supported JavaScript text profiles.</summary>
public enum JavaScriptEmissionProfile
{
    /// <summary>Checked representation for compiler development and hostile interop diagnostics.</summary>
    Diagnostic = 0,

    /// <summary>Checked representation with symbolic generated identifiers.</summary>
    Symbolic = 1,

    /// <summary>
    /// Production representation: compiler-created values are trusted inside generated code;
    /// nominal validation remains available at explicit external boundaries.
    /// </summary>
    Production = 2,
}

/// <summary>Execution environment selected explicitly by the JavaScript host.</summary>
public enum JavaScriptRuntimeTarget
{
    Node = 0,
    Browser = 1,
}

/// <summary>Immutable options for JavaScript emission.</summary>
public sealed record JavaScriptEmissionOptions
{
    public JavaScriptEmissionProfile Profile { get; init; } = JavaScriptEmissionProfile.Diagnostic;
    public JavaScriptRuntimeTarget RuntimeTarget { get; init; } = JavaScriptRuntimeTarget.Node;
    public bool EmitModuleFactories { get; init; }
    public IReadOnlySet<string> BoundaryFunctionNames { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}
