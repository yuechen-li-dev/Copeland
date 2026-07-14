namespace Copeland.TS.Backend.JavaScript;

/// <summary>Supported JavaScript text profiles.</summary>
public enum JavaScriptEmissionProfile
{
    Diagnostic = 0,
    Symbolic = 1,
}

/// <summary>Immutable options for JavaScript emission.</summary>
public sealed record JavaScriptEmissionOptions
{
    public JavaScriptEmissionProfile Profile { get; init; } = JavaScriptEmissionProfile.Diagnostic;
}
