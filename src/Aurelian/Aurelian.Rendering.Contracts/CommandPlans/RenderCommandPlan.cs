using System.Collections.Generic;
using System.Linq;

namespace Aurelian.Rendering.Contracts.CommandPlans;

public sealed record RenderCommandPlan(
    RenderCommandPlanStatus Status,
    RenderCommandPlanReason Reason,
    IReadOnlyList<RenderPassPlan> Passes,
    IReadOnlyList<RenderCommandPlanDiagnostic> Diagnostics)
{
    public bool Success => Status != RenderCommandPlanStatus.Rejected
        && Diagnostics.All(x => x.Severity != RenderCommandPlanDiagnosticSeverity.Error);

    public bool IsEmpty => Status == RenderCommandPlanStatus.Empty;
}
