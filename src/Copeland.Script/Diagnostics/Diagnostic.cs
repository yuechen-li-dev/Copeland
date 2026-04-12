namespace Copeland.Script.Diagnostics;

public sealed record Diagnostic(string Id, string Message, int Position, int Length);
