using Aurelian.Machina;
using Aurelian.Rendering.Contracts.Resolved2D;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Presentation;
using Xunit;
using AurelianFill = Aurelian.Rendering.Contracts.Resolved2D.FillRectangleOperation;
using AurelianPop = Aurelian.Rendering.Contracts.Resolved2D.PopClipOperation;
using AurelianPush = Aurelian.Rendering.Contracts.Resolved2D.PushRectangularClipOperation;
using AurelianStroke = Aurelian.Rendering.Contracts.Resolved2D.StrokeRectangleOperation;
using AurelianText = Aurelian.Rendering.Contracts.Resolved2D.PositionedTextOperation;
using MachinaFill = Machina.Presentation.FillRectangleOperation;
using MachinaPop = Machina.Presentation.PopClipOperation;
using MachinaPush = Machina.Presentation.PushRectangularClipOperation;
using MachinaStroke = Machina.Presentation.StrokeRectangleOperation;
using MachinaText = Machina.Presentation.PositionedTextOperation;

namespace Aurelian.Machina.Tests;

public sealed class MachinaPresentationTranslatorTests
{
    [Fact]
    public void EmptyFrame_PreservesViewportAndUsesImmutablePlanStorage()
    {
        var source = new MachinaPresentationFrame(new MachinaPresentationViewport(7, 5), []);

        Resolved2DPlan plan = MachinaPresentationTranslator.Translate(source);

        Assert.Equal(new Resolved2DViewport(7, 5), plan.Viewport);
        Assert.Empty(plan.Operations);
        Assert.IsAssignableFrom<IReadOnlyList<Resolved2DOperation>>(plan.Operations);
        var operations = Assert.IsAssignableFrom<IList<Resolved2DOperation>>(plan.Operations);
        Assert.Throws<NotSupportedException>(() => operations.Add(new AurelianPop("pop")));
    }

    [Fact]
    public void Operations_MapOneToOneInExactSourceOrder()
    {
        var source = Frame(
            new MachinaFill("fill", new Rect(1, 2, 3, 4), ColorToken.Hex(0x10203040)),
            new MachinaStroke("stroke", new Rect(5, 6, 7, 8), ColorToken.Hex(0xAABBCCDD), 1.5),
            new MachinaText(
                "text",
                new Rect(9, 10, 11, 12),
                "Hi",
                new TextStyle(
                    Color: ColorToken.Hex(0x11223344),
                    Size: TextSize.H1,
                    AlignX: TextAlignX.Center,
                    AlignY: TextAlignY.Bottom),
                ColorToken.Hex(0x11223344)),
            new MachinaPush("clip", new Rect(0, 0, 20, 20)),
            new MachinaPop());

        Resolved2DPlan plan = MachinaPresentationTranslator.Translate(source);

        Assert.Equal(source.Operations.Count, plan.Operations.Count);
        Assert.Collection(
            plan.Operations,
            operation => Assert.IsType<AurelianFill>(operation),
            operation => Assert.IsType<AurelianStroke>(operation),
            operation => Assert.IsType<AurelianText>(operation),
            operation => Assert.IsType<AurelianPush>(operation),
            operation => Assert.IsType<AurelianPop>(operation));
    }

    [Fact]
    public void FillAndStroke_PreserveGeometryColorThicknessAndProvenance()
    {
        var source = Frame(
            new MachinaFill("fill-source", new Rect(0.25, 1.5, 2.75, 3.5), ColorToken.Hex(0x11223344)),
            new MachinaStroke("stroke-source", new Rect(4.25, 5.5, 6.75, 7.5), ColorToken.Hex(0xAABBCCDD), 2.5));

        Resolved2DPlan plan = MachinaPresentationTranslator.Translate(source);
        var fill = Assert.IsType<AurelianFill>(plan.Operations[0]);
        var stroke = Assert.IsType<AurelianStroke>(plan.Operations[1]);

        Assert.Equal("fill-source.0", fill.OperationId);
        Assert.Equal(new Resolved2DRectangle(0.25, 1.5, 2.75, 3.5), fill.Rectangle);
        Assert.Equal(new Resolved2DRgbaColor(0x11, 0x22, 0x33, 0x44), fill.Color);
        Assert.Equal("stroke-source.1", stroke.OperationId);
        Assert.Equal(new Resolved2DRectangle(4.25, 5.5, 6.75, 7.5), stroke.Rectangle);
        Assert.Equal(new Resolved2DRgbaColor(0xAA, 0xBB, 0xCC, 0xDD), stroke.Color);
        Assert.Equal(2.5, stroke.Thickness);
    }

    [Fact]
    public void PositionedText_PreservesResolvedColorPlacementAndBitmapTextValues()
    {
        var source = Frame(new MachinaText(
            "rich.b0.l1.r2",
            new Rect(2.5, 3.5, 40, 16),
            "Linked text",
            new TextStyle(
                Color: ColorToken.Hex(0xAABBCC80),
                Size: TextSize.Sm,
                AlignX: TextAlignX.Right,
                AlignY: TextAlignY.Center),
            ColorToken.Hex(0xAABBCC80)));

        var text = Assert.IsType<AurelianText>(MachinaPresentationTranslator.Translate(source).Operations[0]);

        Assert.Equal("rich.b0.l1.r2.0", text.OperationId);
        Assert.Equal(new Resolved2DRectangle(2.5, 3.5, 40, 16), text.Bounds);
        Assert.Equal("Linked text", text.Text);
        Assert.Equal(new Resolved2DRgbaColor(0xAA, 0xBB, 0xCC, 0x80), text.Color);
        Assert.Equal(Resolved2DTextFace.ReadableBitmap5x7, text.Face);
        Assert.Equal(Resolved2DTextSize.Small, text.Size);
        Assert.Equal(Resolved2DTextAlignX.Right, text.AlignX);
        Assert.Equal(Resolved2DTextAlignY.Center, text.AlignY);
    }

    [Fact]
    public void ClipOperations_PreserveNestingRectanglesAndOrdering()
    {
        var source = Frame(
            new MachinaPush("outer", new Rect(1, 1, 10, 10)),
            new MachinaPush("inner", new Rect(3, 3, 5, 5)),
            new MachinaFill("content", new Rect(0, 0, 20, 20), ColorToken.White),
            new MachinaPop(),
            new MachinaPop());

        Resolved2DPlan plan = MachinaPresentationTranslator.Translate(source);

        var outer = Assert.IsType<AurelianPush>(plan.Operations[0]);
        var inner = Assert.IsType<AurelianPush>(plan.Operations[1]);
        var firstPop = Assert.IsType<AurelianPop>(plan.Operations[3]);
        var secondPop = Assert.IsType<AurelianPop>(plan.Operations[4]);
        Assert.Equal("outer.0", outer.OperationId);
        Assert.Equal(new Resolved2DRectangle(1, 1, 10, 10), outer.Rectangle);
        Assert.Equal("inner.1", inner.OperationId);
        Assert.Equal(new Resolved2DRectangle(3, 3, 5, 5), inner.Rectangle);
        Assert.Equal("pop.3", firstPop.OperationId);
        Assert.Equal("pop.4", secondPop.OperationId);
    }

    [Fact]
    public void RepeatedTranslation_IsDeterministic()
    {
        var source = Frame(new MachinaFill("same", new Rect(0, 0, 1, 1), ColorToken.White));

        Resolved2DPlan first = MachinaPresentationTranslator.Translate(source);
        Resolved2DPlan second = MachinaPresentationTranslator.Translate(source);

        Assert.Equal(first.Viewport, second.Viewport);
        Assert.Equal(first.Operations, second.Operations);
    }

    [Fact]
    public void NullFrame_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => MachinaPresentationTranslator.Translate(null!));
    }

    [Fact]
    public void FutureOperationKind_FailsExplicitly()
    {
        var source = Frame(new UnknownOperation());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => MachinaPresentationTranslator.Translate(source));

        Assert.Contains(typeof(UnknownOperation).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedTextEnum_FailsExplicitly()
    {
        var source = Frame(new MachinaText(
            "invalid-text",
            new Rect(0, 0, 10, 10),
            "Text",
            new TextStyle(Size: (TextSize)99),
            ColorToken.White));

        Assert.Throws<ArgumentOutOfRangeException>(() => MachinaPresentationTranslator.Translate(source));
    }

    [Fact]
    public void InconsistentTextPresentationColor_FailsExplicitly()
    {
        var source = Frame(new MachinaText(
            "invalid-color",
            new Rect(0, 0, 10, 10),
            "Text",
            new TextStyle(Color: ColorToken.White),
            ColorToken.Hex(0x000000FF)));

        Assert.Throws<InvalidOperationException>(() => MachinaPresentationTranslator.Translate(source));
    }

    [Fact]
    public void BridgeAssembly_HasNoForbiddenProductionDependencies()
    {
        string[] references = typeof(MachinaPresentationTranslator).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("Machina.Presentation", references);
        Assert.Contains("Aurelian.Rendering.Contracts", references);
        Assert.DoesNotContain("Aurelian.Rendering.Raster", references);
        Assert.DoesNotContain("Aurelian.Core", references);
        Assert.DoesNotContain("Aurelian.Runtime", references);
        Assert.DoesNotContain("Aurelian.Graphics", references);
        Assert.DoesNotContain("Machina.Dominatus", references);
        Assert.DoesNotContain("Machina.Pipeline", references);
        Assert.DoesNotContain("Machina.Renderer.Raster", references);
    }

    private static MachinaPresentationFrame Frame(params MachinaPresentationOperation[] operations)
    {
        return new MachinaPresentationFrame(new MachinaPresentationViewport(80, 40), operations);
    }

    private sealed record UnknownOperation : MachinaPresentationOperation;
}
