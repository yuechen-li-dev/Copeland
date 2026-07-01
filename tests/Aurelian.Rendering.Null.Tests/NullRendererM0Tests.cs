using System;
using System.IO;
using System.Linq;
using Aurelian.Rendering.Contracts.CommandPlans;
using Aurelian.Rendering.Contracts.Snapshots;
using Xunit;

namespace Aurelian.Rendering.Null.Tests;

public sealed class NullRendererM0Tests
{
    private static readonly RenderTargetRef Target = new("target/backbuffer");
    private static readonly RenderPipelineRef Pipeline = new("pipeline/main2d");
    private static readonly RenderShaderRef Shader = new("shader/unlit2d");

    [Fact]
    public void NullRenderer_RenderReadyPlan_ProducesDeterministicTrace()
    {
        RenderCommandPlan plan = ReadyPlan([
            Draw("draw-a", x: 12.5, y: -4, sortOrder: 1),
            Draw("draw-b", x: 1, y: 2, sortOrder: 2)
        ]);
        NullRenderer renderer = new();

        NullRenderResult result = renderer.Render(plan);

        Assert.Equal(NullRenderStatus.Rendered, result.Status);
        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);

        NullRenderPassTrace pass = Assert.Single(result.Trace.Passes);
        Assert.Equal("Main2D", pass.Name);
        Assert.Equal("target/backbuffer", pass.Target);
        Assert.Equal("pipeline/main2d", pass.Pipeline);
        Assert.Equal("shader/unlit2d", pass.Shader);

        NullRenderDrawTrace[] draws = pass.Draws.ToArray();
        Assert.Collection(
            draws,
            first =>
            {
                Assert.Equal("draw-a", first.Id);
                Assert.Equal("mesh/draw-a", first.Mesh);
                Assert.Equal("material/draw-a", first.Material);
                Assert.Equal(12.5, first.X);
                Assert.Equal(-4, first.Y);
                Assert.Equal(1, first.SortOrder);
            },
            second =>
            {
                Assert.Equal("draw-b", second.Id);
                Assert.Equal("mesh/draw-b", second.Mesh);
                Assert.Equal("material/draw-b", second.Material);
                Assert.Equal(1, second.X);
                Assert.Equal(2, second.Y);
                Assert.Equal(2, second.SortOrder);
            });
    }

    [Fact]
    public void NullRenderer_RenderReadyPlan_ReportsPassAndDrawCounts()
    {
        RenderCommandPlan plan = new(
            RenderCommandPlanStatus.Ready,
            RenderCommandPlanReason.Ready,
            [
                new RenderPassPlan("First", Target, Pipeline, Shader, [Draw("a"), Draw("b")]),
                new RenderPassPlan("Second", Target, Pipeline, Shader, [Draw("c")])
            ],
            []);
        NullRenderer renderer = new();

        NullRenderResult result = renderer.Render(plan);

        Assert.Equal(NullRenderStatus.Rendered, result.Status);
        Assert.Equal(2, result.Trace.PassCount);
        Assert.Equal(3, result.Trace.DrawCount);
    }

    [Fact]
    public void NullRenderer_RenderEmptyPlan_ReturnsNoOpTrace()
    {
        RenderCommandPlan plan = new(
            RenderCommandPlanStatus.Empty,
            RenderCommandPlanReason.EmptySnapshot,
            [],
            [new RenderCommandPlanDiagnostic(
                RenderCommandPlanDiagnosticCodes.EmptySnapshot,
                RenderCommandPlanDiagnosticSeverity.Info,
                "No items were present.")]);
        NullRenderer renderer = new();

        NullRenderResult result = renderer.Render(plan);

        Assert.Equal(NullRenderStatus.NoOp, result.Status);
        Assert.True(result.Success);
        Assert.Empty(result.Trace.Passes);
        Assert.Equal(0, result.Trace.PassCount);
        Assert.Equal(0, result.Trace.DrawCount);
        NullRenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(NullRenderDiagnosticCodes.EmptyPlanNoOp, diagnostic.Code);
        Assert.Equal(NullRenderDiagnosticSeverity.Info, diagnostic.Severity);
    }

    [Fact]
    public void NullRenderer_RenderRejectedPlan_ReturnsRejectedDiagnostic()
    {
        RenderCommandPlan plan = new(
            RenderCommandPlanStatus.Rejected,
            RenderCommandPlanReason.MissingShader,
            [],
            [new RenderCommandPlanDiagnostic(
                RenderCommandPlanDiagnosticCodes.MissingShader,
                RenderCommandPlanDiagnosticSeverity.Error,
                "Shader is required.")]);
        NullRenderer renderer = new();

        NullRenderResult result = renderer.Render(plan);

        Assert.Equal(NullRenderStatus.Rejected, result.Status);
        Assert.False(result.Success);
        Assert.Empty(result.Trace.Passes);
        NullRenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(NullRenderDiagnosticCodes.CommandPlanRejected, diagnostic.Code);
        Assert.Equal(NullRenderDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("MissingShader", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullRenderer_RenderMalformedReadyPlan_ReturnsInvalidReadyPlanDiagnostic()
    {
        RenderCommandPlan plan = new(
            RenderCommandPlanStatus.Ready,
            RenderCommandPlanReason.Ready,
            [],
            []);
        NullRenderer renderer = new();

        NullRenderResult result = renderer.Render(plan);

        Assert.Equal(NullRenderStatus.Rejected, result.Status);
        Assert.False(result.Success);
        Assert.Empty(result.Trace.Passes);
        NullRenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(NullRenderDiagnosticCodes.InvalidReadyPlan, diagnostic.Code);
        Assert.Equal(NullRenderDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void NullRenderer_DoesNotRequireWorldAssetsShadersOrBackend()
    {
        string projectFile = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/Aurelian.Rendering.Null/Aurelian.Rendering.Null.csproj"));
        string projectText = File.ReadAllText(projectFile);

        Assert.Contains("Aurelian.Rendering.Contracts", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian." + "World", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian." + "Assets", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian." + "Shaders", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Si" + "lk", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Vor" + "tice", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Vul" + "kan", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("D" + "3D", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Win" + "dow", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("Swap" + "Chain", projectText, StringComparison.Ordinal);
    }

    private static RenderCommandPlan ReadyPlan(DrawItem2D[] draws)
    {
        return new RenderCommandPlan(
            RenderCommandPlanStatus.Ready,
            RenderCommandPlanReason.Ready,
            [new RenderPassPlan("Main2D", Target, Pipeline, Shader, draws)],
            []);
    }

    private static DrawItem2D Draw(string id, double x = 0, double y = 0, int sortOrder = 0)
    {
        return new DrawItem2D(
            id,
            new RenderMeshRef($"mesh/{id}"),
            new RenderMaterialRef($"material/{id}"),
            new RenderTransform2(x, y, 0, 1, 1),
            sortOrder);
    }
}
