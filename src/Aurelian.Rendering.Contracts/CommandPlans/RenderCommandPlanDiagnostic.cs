namespace Aurelian.Rendering.Contracts.CommandPlans;

public enum RenderCommandPlanDiagnosticSeverity
{
    Error,
    Warning,
    Info
}

public sealed record RenderCommandPlanDiagnostic(
    string Code,
    RenderCommandPlanDiagnosticSeverity Severity,
    string Message);
