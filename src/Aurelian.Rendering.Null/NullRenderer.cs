using System.Collections.Generic;
using System.Linq;
using Aurelian.Rendering.Contracts.CommandPlans;

namespace Aurelian.Rendering.Null;

public sealed class NullRenderer
{
    public NullRenderResult Render(RenderCommandPlan plan)
    {
        if (plan is null)
        {
            return InvalidReadyPlan("Render command plan cannot be null.");
        }

        return plan.Status switch
        {
            RenderCommandPlanStatus.Rejected => RejectedPlan(plan),
            RenderCommandPlanStatus.Empty => EmptyPlan(),
            RenderCommandPlanStatus.Ready => ReadyPlan(plan),
            _ => InvalidReadyPlan($"Render command plan status '{plan.Status}' is not supported.")
        };
    }

    private static NullRenderResult RejectedPlan(RenderCommandPlan plan)
    {
        return new NullRenderResult(
            NullRenderStatus.Rejected,
            NullRenderTrace.Empty,
            [new NullRenderDiagnostic(
                NullRenderDiagnosticCodes.CommandPlanRejected,
                NullRenderDiagnosticSeverity.Error,
                $"Command plan was rejected before null rendering. Reason: {plan.Reason}.")]);
    }

    private static NullRenderResult EmptyPlan()
    {
        return new NullRenderResult(
            NullRenderStatus.NoOp,
            NullRenderTrace.Empty,
            [new NullRenderDiagnostic(
                NullRenderDiagnosticCodes.EmptyPlanNoOp,
                NullRenderDiagnosticSeverity.Info,
                "Command plan is empty; null renderer performed no work.")]);
    }

    private static NullRenderResult ReadyPlan(RenderCommandPlan plan)
    {
        string? invalidReason = ValidateReadyPlan(plan);
        if (invalidReason is not null)
        {
            return InvalidReadyPlan(invalidReason);
        }

        NullRenderPassTrace[] passes = plan.Passes
            .Select(pass => new NullRenderPassTrace(
                pass.Name,
                pass.Target.ToString(),
                pass.Pipeline.ToString(),
                pass.Shader.ToString(),
                pass.DrawItems.Select(draw => new NullRenderDrawTrace(
                    draw.Id,
                    draw.Mesh.ToString(),
                    draw.Material.ToString(),
                    draw.Transform.X,
                    draw.Transform.Y,
                    draw.SortOrder)).ToArray()))
            .ToArray();

        return new NullRenderResult(
            NullRenderStatus.Rendered,
            new NullRenderTrace(passes),
            []);
    }

    private static string? ValidateReadyPlan(RenderCommandPlan plan)
    {
        if (plan.Passes is null || plan.Passes.Count == 0)
        {
            return "Ready command plan must contain at least one render pass.";
        }

        for (int passIndex = 0; passIndex < plan.Passes.Count; passIndex++)
        {
            RenderPassPlan pass = plan.Passes[passIndex];
            if (pass is null)
            {
                return $"Ready command plan contains a null pass at index {passIndex}.";
            }

            if (pass.DrawItems is null || pass.DrawItems.Count == 0)
            {
                return $"Ready command plan pass '{pass.Name}' must contain at least one draw item.";
            }
        }

        return null;
    }

    private static NullRenderResult InvalidReadyPlan(string message)
    {
        return new NullRenderResult(
            NullRenderStatus.Rejected,
            NullRenderTrace.Empty,
            [new NullRenderDiagnostic(
                NullRenderDiagnosticCodes.InvalidReadyPlan,
                NullRenderDiagnosticSeverity.Error,
                message)]);
    }
}
