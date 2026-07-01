using System;
using System.IO;
using System.Linq;
using Aurelian.Rendering.Contracts.CommandPlans;
using Aurelian.Rendering.Contracts.Snapshots;
using Xunit;

namespace Aurelian.Rendering.Contracts.Tests;

public sealed class RenderCommandPlanM0Tests
{
    private static readonly RenderPipelineRef Pipeline = new("pipeline/main2d");
    private static readonly RenderShaderRef Shader = new("shader/unlit2d");
    private static readonly RenderTargetRef Target = new("target/backbuffer");

    [Fact]
    public void RenderCommandPlan_ReadyWithoutErrors_IsSuccess()
    {
        RenderCommandPlan plan = new(
            RenderCommandPlanStatus.Ready,
            RenderCommandPlanReason.Ready,
            [],
            [new RenderCommandPlanDiagnostic(
                RenderCommandPlanDiagnosticCodes.UnsupportedFeature,
                RenderCommandPlanDiagnosticSeverity.Info,
                "Feature was ignored.")]);

        Assert.True(plan.Success);
        Assert.False(plan.IsEmpty);
    }

    [Fact]
    public void RenderCommandPlan_RejectedWithError_IsNotSuccess()
    {
        RenderCommandPlan plan = new(
            RenderCommandPlanStatus.Rejected,
            RenderCommandPlanReason.MissingShader,
            [],
            [new RenderCommandPlanDiagnostic(
                RenderCommandPlanDiagnosticCodes.MissingShader,
                RenderCommandPlanDiagnosticSeverity.Error,
                "Shader is required.")]);

        Assert.False(plan.Success);
    }

    [Fact]
    public void RenderCommandPlan_Empty_IsSuccessAndEmpty()
    {
        RenderCommandPlan plan = new(
            RenderCommandPlanStatus.Empty,
            RenderCommandPlanReason.EmptySnapshot,
            [],
            [new RenderCommandPlanDiagnostic(
                RenderCommandPlanDiagnosticCodes.EmptySnapshot,
                RenderCommandPlanDiagnosticSeverity.Info,
                "Snapshot was empty.")]);

        Assert.True(plan.Success);
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void RenderRefs_ToString_ReturnsValue()
    {
        Assert.Equal("pipeline/main2d", Pipeline.ToString());
        Assert.Equal("shader/unlit2d", Shader.ToString());
        Assert.Equal("target/backbuffer", Target.ToString());
    }

    [Fact]
    public void RenderPassPlan_CanHoldDrawItems()
    {
        DrawItem2D drawItem = new(
            "unit-1",
            new RenderMeshRef("mesh/quad"),
            new RenderMaterialRef("material/unlit"),
            RenderTransform2.Identity,
            SortOrder: 3);

        RenderPassPlan pass = new("Main2D", Target, Pipeline, Shader, [drawItem]);

        Assert.Equal("Main2D", pass.Name);
        Assert.Equal(Target, pass.Target);
        Assert.Equal(Pipeline, pass.Pipeline);
        Assert.Equal(Shader, pass.Shader);
        Assert.Equal(drawItem, Assert.Single(pass.DrawItems));
    }

    [Fact]
    public void RenderCommandPlanBuilder_FromSnapshot_EmptySnapshot_ReturnsEmpty()
    {
        RenderSnapshot snapshot = new(new RenderFrameId(1), [], []);

        RenderCommandPlan plan = RenderCommandPlanBuilder.FromSnapshot(snapshot, Pipeline, Shader, Target);

        Assert.Equal(RenderCommandPlanStatus.Empty, plan.Status);
        Assert.Equal(RenderCommandPlanReason.EmptySnapshot, plan.Reason);
        Assert.True(plan.Success);
        Assert.True(plan.IsEmpty);
        Assert.Empty(plan.Passes);
        RenderCommandPlanDiagnostic diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(RenderCommandPlanDiagnosticCodes.EmptySnapshot, diagnostic.Code);
        Assert.Equal(RenderCommandPlanDiagnosticSeverity.Info, diagnostic.Severity);
    }

    [Fact]
    public void RenderCommandPlanBuilder_FromSnapshot_MissingCamera_ReturnsRejected()
    {
        RenderSnapshot snapshot = new(new RenderFrameId(2), [], [CreateItem("item-a")]);

        RenderCommandPlan plan = RenderCommandPlanBuilder.FromSnapshot(snapshot, Pipeline, Shader, Target);

        Assert.Equal(RenderCommandPlanStatus.Rejected, plan.Status);
        Assert.Equal(RenderCommandPlanReason.MissingCamera, plan.Reason);
        Assert.False(plan.Success);
        Assert.Empty(plan.Passes);
        RenderCommandPlanDiagnostic diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(RenderCommandPlanDiagnosticCodes.MissingCamera, diagnostic.Code);
        Assert.Equal(RenderCommandPlanDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void RenderCommandPlanBuilder_FromSnapshot_WithCameraAndItems_ReturnsReadySinglePass()
    {
        RenderCamera2D camera = new("main", RenderTransform2.Identity, 1920, 1080);
        RenderItem2D item = CreateItem("item-a", sortOrder: 7);
        RenderSnapshot snapshot = new(new RenderFrameId(3), [camera], [item]);

        RenderCommandPlan plan = RenderCommandPlanBuilder.FromSnapshot(snapshot, Pipeline, Shader, Target);

        Assert.Equal(RenderCommandPlanStatus.Ready, plan.Status);
        Assert.Equal(RenderCommandPlanReason.Ready, plan.Reason);
        Assert.True(plan.Success);
        Assert.Empty(plan.Diagnostics);

        RenderPassPlan pass = Assert.Single(plan.Passes);
        Assert.Equal("Main2D", pass.Name);
        Assert.Equal(Target, pass.Target);
        Assert.Equal(Pipeline, pass.Pipeline);
        Assert.Equal(Shader, pass.Shader);

        DrawItem2D drawItem = Assert.Single(pass.DrawItems);
        Assert.Equal(item.Id, drawItem.Id);
        Assert.Equal(item.Mesh, drawItem.Mesh);
        Assert.Equal(item.Material, drawItem.Material);
        Assert.Equal(item.Transform, drawItem.Transform);
        Assert.Equal(item.SortOrder, drawItem.SortOrder);
    }

    [Fact]
    public void RenderCommandPlanBuilder_SortsDrawItemsBySortOrderThenId()
    {
        RenderCamera2D camera = new("main", RenderTransform2.Identity, 1920, 1080);
        RenderSnapshot snapshot = new(
            new RenderFrameId(4),
            [camera],
            [
                CreateItem("b", sortOrder: 2),
                CreateItem("c", sortOrder: 1),
                CreateItem("a", sortOrder: 1)
            ]);

        RenderCommandPlan plan = RenderCommandPlanBuilder.FromSnapshot(snapshot, Pipeline, Shader, Target);

        DrawItem2D[] drawItems = Assert.Single(plan.Passes).DrawItems.ToArray();
        Assert.Equal(["a", "c", "b"], drawItems.Select(x => x.Id).ToArray());
    }

    [Fact]
    public void RenderCommandPlanContracts_DoNotRequireWorldAssetsShadersOrBackend()
    {
        string projectFile = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/Aurelian.Rendering.Contracts/Aurelian.Rendering.Contracts.csproj"));
        string projectText = File.ReadAllText(projectFile);

        Assert.DoesNotContain("Aurelian." + "World", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian." + "Assets", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian." + "Shaders", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Silk", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Vortice", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Vulkan", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("D3D", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Window", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("SwapChain", projectText, StringComparison.Ordinal);
    }

    private static RenderItem2D CreateItem(string id, int sortOrder = 0)
    {
        return new RenderItem2D(
            id,
            RenderTransform2.Identity,
            new RenderMeshRef($"mesh/{id}"),
            new RenderMaterialRef($"material/{id}"),
            sortOrder);
    }
}
