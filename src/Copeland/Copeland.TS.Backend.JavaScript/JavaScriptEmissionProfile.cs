namespace Copeland.TS.Backend.JavaScript;

/// <summary>Supported JavaScript text profiles.</summary>
public enum JavaScriptEmissionProfile
{
    Diagnostic = 0,
    Symbolic = 1,
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
}
