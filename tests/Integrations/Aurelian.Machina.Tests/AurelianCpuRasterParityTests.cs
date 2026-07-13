using System.Security.Cryptography;
using Aurelian.Machina;
using Aurelian.Rendering.Raster;
using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Compilation;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Presentation;
using Machina.Standard.Authoring;
using Xunit;
using MachinaFill = Machina.Presentation.FillRectangleOperation;
using MachinaPop = Machina.Presentation.PopClipOperation;
using MachinaPush = Machina.Presentation.PushRectangularClipOperation;
using MachinaStroke = Machina.Presentation.StrokeRectangleOperation;
using MachinaText = Machina.Presentation.PositionedTextOperation;

namespace Aurelian.Machina.Tests;

public sealed class AurelianCpuRasterParityTests
{
    [Fact]
    public void EmptyFrame_MatchesFrozenLegacyPpmHash()
    {
        AssertFrozen(
            new MachinaPresentationFrame(new MachinaPresentationViewport(3, 2), []),
            "91B6337CA9E21BCD020CB747BB39C11D4084B8B849C7D7E41F2C03633962900D");
    }

    [Fact]
    public void NestedClipAndFill_MatchFrozenLegacyPpmHash()
    {
        AssertFrozen(
            Frame(
                6,
                6,
                new MachinaPush("clip-1", new Rect(1, 1, 4, 4)),
                new MachinaPush("clip-2", new Rect(3, 0, 4, 4)),
                new MachinaFill("fill", new Rect(0, 0, 6, 6), ColorToken.White),
                new MachinaPop(),
                new MachinaPop()),
            "1F8B32C10B951A6B78BD8764CEFAC995F5F2184CFF2643F1249CA5802383CA35");
    }

    [Fact]
    public void MixedPresentationOperations_MatchFrozenLegacyPpmHash()
    {
        AssertFrozen(
            Frame(
                40,
                25,
                new MachinaFill("background", new Rect(0, 0, 40, 25), ColorToken.Hex(0x102030FF)),
                new MachinaStroke("border", new Rect(1, 1, 38, 23), ColorToken.White, 1),
                new MachinaPush("content-clip", new Rect(3, 3, 20, 15)),
                new MachinaText(
                    "label",
                    new Rect(3, 3, 30, 20),
                    "Hello",
                    new TextStyle(Color: ColorToken.Hex(0xFFCC00FF), Size: TextSize.Md),
                    ColorToken.Hex(0xFFCC00FF)),
                new MachinaPop()),
            "CA7222F2D2826ABC7B4A14C4F5606AADCD1CA02E114BF83F106994AE7D7E15FA");
    }

    [Fact]
    public void AuthoredStandardDocument_MatchesFrozenLegacyPpmHash()
    {
        UiNode ui = StandardUI.Card(
            id: "card",
            width: 120,
            height: 80,
            child: UI.Column(
                id: "content",
                gap: 4,
                children:
                [
                    UI.Text("Hi", id: "title", color: ColorToken.White),
                    StandardUI.Button("Go", id: "go", action: UiAction.Named("go"))
                ]));

        UiLoweringResult lowering = UiLowerer.Lower(ui);
        LayoutDocument document = LayoutCompiler.CompileLayoutRows(lowering.Rows);
        ResolvedLayoutDocument resolved = LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, 140, 120));
        MachinaPresentationFrame frame = MachinaPresentationFrameBuilder.Build(
            lowering,
            resolved,
            new MachinaPresentationViewport(140, 120));

        AssertFrozen(frame, "552082FE8DBFE0A3901EFDBF28B79C8647B1AD155750F0FECC5799C995A15045");
    }

    private static MachinaPresentationFrame Frame(int width, int height, params MachinaPresentationOperation[] operations)
    {
        return new MachinaPresentationFrame(new MachinaPresentationViewport(width, height), operations);
    }

    private static void AssertFrozen(MachinaPresentationFrame frame, string expectedPpmSha256)
    {
        RasterFrame rasterFrame = new AurelianCpuRasterRenderer().Render(
            MachinaPresentationTranslator.Translate(frame));

        Assert.Equal(frame.Viewport.Width, rasterFrame.Surface.Width);
        Assert.Equal(frame.Viewport.Height, rasterFrame.Surface.Height);
        Assert.Equal(expectedPpmSha256, Convert.ToHexString(SHA256.HashData(RasterPpmEncoder.EncodeP6(rasterFrame.Surface))));
    }
}
