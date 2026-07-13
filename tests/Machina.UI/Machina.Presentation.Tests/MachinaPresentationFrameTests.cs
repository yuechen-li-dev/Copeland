using System.Reflection;
using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Styling;
using Machina.Layout.Compilation;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Presentation;
using Machina.Standard.Authoring;
using Machina.Standard.Text;
using Xunit;
using StandardText = Machina.Standard.Text.Text;

namespace Machina.Presentation.Tests;

public sealed class MachinaPresentationFrameTests
{
    [Fact]
    public void EmptyFrame_PreservesViewportAndHasNoOperations()
    {
        var frame = new MachinaPresentationFrame(new MachinaPresentationViewport(80, 40), []);

        Assert.Equal(80, frame.Viewport.Width);
        Assert.Equal(40, frame.Viewport.Height);
        Assert.Empty(frame.Operations);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    public void Viewport_RejectsNonPositiveDimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MachinaPresentationViewport(width, height));
    }

    [Fact]
    public void Frame_DefensivelyCopiesOperationStorage()
    {
        var source = new List<MachinaPresentationOperation>
        {
            new FillRectangleOperation("panel", new Rect(0, 0, 10, 10), ColorToken.White),
        };

        var frame = new MachinaPresentationFrame(new MachinaPresentationViewport(10, 10), source);
        source.Clear();

        Assert.Single(frame.Operations);
        var operations = Assert.IsAssignableFrom<IList<MachinaPresentationOperation>>(frame.Operations);
        Assert.Throws<NotSupportedException>(() => operations.Clear());
    }

    [Fact]
    public void Frame_RejectsUnbalancedClipOperations()
    {
        Assert.Throws<InvalidOperationException>(() => new MachinaPresentationFrame(
            new MachinaPresentationViewport(10, 10),
            [new PopClipOperation()]));

        Assert.Throws<InvalidOperationException>(() => new MachinaPresentationFrame(
            new MachinaPresentationViewport(10, 10),
            [new PushRectangularClipOperation("panel", new Rect(0, 0, 10, 10))]));
    }

    [Fact]
    public void Operations_RejectInvalidGeometryStrokeAndText()
    {
        Assert.Throws<ArgumentException>(() => new FillRectangleOperation(
            "panel",
            new Rect(double.NaN, 0, 1, 1),
            ColorToken.White));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StrokeRectangleOperation(
            "panel",
            new Rect(0, 0, 1, 1),
            ColorToken.White,
            0));
        Assert.Throws<ArgumentException>(() => new PositionedTextOperation(
            "label",
            new Rect(0, 0, 1, 1),
            " ",
            new TextStyle(),
            ColorToken.White));
    }

    [Fact]
    public void Builder_ProducesOrderedFillStrokePositionedTextAndNestedClips()
    {
        var childStyle = new UiStyle(
            Background: ColorToken.Hex(0x112233FF),
            BorderColor: ColorToken.White,
            BorderThickness: 1,
            ClipToBounds: true);
        var ui = UI.Rect(
            id: "root",
            width: 100,
            height: 80,
            style: new UiStyle(ClipToBounds: true),
            child: UI.Rect(
                id: "panel",
                width: 80,
                height: 50,
                style: childStyle,
                child: UI.Text("Hello", id: "label", color: ColorToken.Gold)));

        MachinaPresentationFrame frame = Build(ui, 100, 80);

        Assert.Collection(
            frame.Operations,
            operation => Assert.IsType<PushRectangularClipOperation>(operation),
            operation => Assert.IsType<FillRectangleOperation>(operation),
            operation => Assert.IsType<StrokeRectangleOperation>(operation),
            operation => Assert.IsType<PushRectangularClipOperation>(operation),
            operation => Assert.IsType<PositionedTextOperation>(operation),
            operation => Assert.IsType<PopClipOperation>(operation),
            operation => Assert.IsType<PopClipOperation>(operation));

        var text = Assert.IsType<PositionedTextOperation>(frame.Operations[4]);
        Assert.Equal("label", text.SourceId);
        Assert.Equal("Hello", text.Text);
        Assert.Equal(ColorToken.Gold, text.Color);
    }

    [Fact]
    public void Builder_PreservesRichTextRunOrderAndResolvedColors()
    {
        var ui = StandardUI.TextBlock(
            id: "rich",
            text: StandardText.Markup("A **bold** [link](https://example.test)"),
            foreground: ColorToken.White,
            linkForeground: ColorToken.Gold);

        MachinaPresentationFrame frame = Build(ui, 200, 80);
        var text = frame.Operations.OfType<PositionedTextOperation>().ToArray();

        Assert.Equal(["A", "bold", "link"], text.Select(operation => operation.Text).ToArray());
        Assert.Equal(ColorToken.Gold, Assert.Single(text, operation => operation.Text == "link").Color);
    }

    [Fact]
    public void PresentationAssembly_HasNoLegacyOrBackendDependencies()
    {
        Assembly assembly = typeof(MachinaPresentationFrame).Assembly;
        string[] references = assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();

        Assert.DoesNotContain(references, name => name.Contains("Dominatus", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.Contains("Raster", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.Contains("Aurelian", StringComparison.Ordinal));
    }

    private static MachinaPresentationFrame Build(Machina.Core.Nodes.UiNode ui, int width, int height)
    {
        UiLoweringResult lowering = UiLowerer.Lower(ui);
        LayoutDocument document = LayoutCompiler.CompileLayoutRows(lowering.Rows);
        ResolvedLayoutDocument resolved = LayoutDocumentResolver.ResolveLayoutDocument(
            document,
            new Rect(0, 0, width, height));
        return MachinaPresentationFrameBuilder.Build(
            lowering,
            resolved,
            new MachinaPresentationViewport(width, height));
    }
}
