namespace Aurelian.Rendering.Contracts.Snapshots;

public enum RenderSnapshotDiagnosticSeverity
{
    Error,
    Warning,
    Info
}

public sealed record RenderSnapshotDiagnostic(
    string Code,
    RenderSnapshotDiagnosticSeverity Severity,
    string Message);
