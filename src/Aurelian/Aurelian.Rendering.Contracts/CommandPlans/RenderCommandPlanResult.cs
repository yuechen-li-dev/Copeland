using System.Collections.Generic;
using System.Linq;

namespace Aurelian.Rendering.Contracts.CommandPlans;

public sealed record RenderCommandPlanResult(
    RenderCommandPlan? Plan,
    IReadOnlyList<RenderCommandPlanDiagnostic> Diagnostics)
{
    public bool Success => Plan is not null
        && Plan.Success
        && Diagnostics.All(x => x.Severity != RenderCommandPlanDiagnosticSeverity.Error);
}
