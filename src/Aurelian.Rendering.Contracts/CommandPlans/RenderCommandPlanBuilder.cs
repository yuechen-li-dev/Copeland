using System;
using System.Linq;
using Aurelian.Rendering.Contracts.Snapshots;

namespace Aurelian.Rendering.Contracts.CommandPlans;

public static class RenderCommandPlanBuilder
{
    public static RenderCommandPlan FromSnapshot(
        RenderSnapshot snapshot,
        RenderPipelineRef pipeline,
        RenderShaderRef shader,
        RenderTargetRef target)
    {
        if (snapshot.IsEmpty)
        {
            return new RenderCommandPlan(
                RenderCommandPlanStatus.Empty,
                RenderCommandPlanReason.EmptySnapshot,
                [],
                [new RenderCommandPlanDiagnostic(
                    RenderCommandPlanDiagnosticCodes.EmptySnapshot,
                    RenderCommandPlanDiagnosticSeverity.Info,
                    "Snapshot contains no cameras or render items.")]);
        }

        if (snapshot.Cameras.Count == 0)
        {
            return Rejected(
                RenderCommandPlanReason.MissingCamera,
                RenderCommandPlanDiagnosticCodes.MissingCamera,
                "Snapshot contains render items but no camera.");
        }

        if (snapshot.Items.Count == 0)
        {
            return new RenderCommandPlan(
                RenderCommandPlanStatus.Empty,
                RenderCommandPlanReason.MissingDrawItems,
                [],
                [new RenderCommandPlanDiagnostic(
                    RenderCommandPlanDiagnosticCodes.MissingDrawItems,
                    RenderCommandPlanDiagnosticSeverity.Info,
                    "Snapshot contains cameras but no draw items.")]);
        }

        if (string.IsNullOrWhiteSpace(pipeline.Value))
        {
            return Rejected(
                RenderCommandPlanReason.MissingPipeline,
                RenderCommandPlanDiagnosticCodes.MissingPipeline,
                "A render pipeline reference is required to build a command plan.");
        }

        if (string.IsNullOrWhiteSpace(shader.Value))
        {
            return Rejected(
                RenderCommandPlanReason.MissingShader,
                RenderCommandPlanDiagnosticCodes.MissingShader,
                "A render shader reference is required to build a command plan.");
        }

        RenderItem2D? invalidItem = snapshot.Items.FirstOrDefault(IsInvalid);
        if (invalidItem is not null)
        {
            return Rejected(
                RenderCommandPlanReason.InvalidDrawItem,
                RenderCommandPlanDiagnosticCodes.InvalidDrawItem,
                $"Snapshot render item '{invalidItem.Id}' cannot be converted to a draw item.");
        }

        DrawItem2D[] drawItems = snapshot.Items
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(x => new DrawItem2D(x.Id, x.Mesh, x.Material, x.Transform, x.SortOrder))
            .ToArray();

        RenderPassPlan pass = new("Main2D", target, pipeline, shader, drawItems);

        return new RenderCommandPlan(
            RenderCommandPlanStatus.Ready,
            RenderCommandPlanReason.Ready,
            [pass],
            []);
    }

    private static bool IsInvalid(RenderItem2D item)
    {
        return string.IsNullOrWhiteSpace(item.Id)
            || string.IsNullOrWhiteSpace(item.Mesh.Value)
            || string.IsNullOrWhiteSpace(item.Material.Value);
    }

    private static RenderCommandPlan Rejected(
        RenderCommandPlanReason reason,
        string diagnosticCode,
        string message)
    {
        return new RenderCommandPlan(
            RenderCommandPlanStatus.Rejected,
            reason,
            [],
            [new RenderCommandPlanDiagnostic(
                diagnosticCode,
                RenderCommandPlanDiagnosticSeverity.Error,
                message)]);
    }
}
