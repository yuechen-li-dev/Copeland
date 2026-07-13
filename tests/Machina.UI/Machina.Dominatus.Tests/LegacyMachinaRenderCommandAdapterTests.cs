using Dominatus.Core.Runtime;
using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Bridge;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Geometry;
using Machina.Presentation;
using Xunit;

namespace Machina.Dominatus.Tests;

public sealed class LegacyMachinaRenderCommandAdapterTests
{
    [Fact]
    public void Adapter_TranslatesEveryPresentationOperationInOrder()
    {
        var style = new TextStyle(Color: ColorToken.Gold, Size: TextSize.Sm);
        var frame = new MachinaPresentationFrame(
            new MachinaPresentationViewport(80, 40),
            [
                new FillRectangleOperation("panel", new Rect(0, 0, 80, 40), ColorToken.White),
                new StrokeRectangleOperation("panel", new Rect(0, 0, 80, 40), ColorToken.Gold, 1),
                new PushRectangularClipOperation("panel", new Rect(2, 2, 76, 36)),
                new PositionedTextOperation("label", new Rect(4, 4, 60, 12), "Hello", style, ColorToken.Gold),
                new PopClipOperation(),
            ]);

        IReadOnlyList<IActuationCommand> commands = LegacyMachinaRenderCommandAdapter.ToLegacyCommands(frame);

        Assert.Collection(
            commands,
            command => Assert.Equal(new BeginFrameCommand(80, 40), command),
            command => Assert.Equal(new FillRectCommand("panel", new Rect(0, 0, 80, 40), ColorToken.White), command),
            command => Assert.Equal(new StrokeRectCommand("panel", new Rect(0, 0, 80, 40), ColorToken.Gold, 1), command),
            command => Assert.Equal(new PushClipCommand("panel", new Rect(2, 2, 76, 36)), command),
            command => Assert.Equal(new DrawTextCommand("label", new Rect(4, 4, 60, 12), "Hello", style), command),
            command => Assert.IsType<PopClipCommand>(command),
            command => Assert.IsType<EndFrameCommand>(command));
    }
}
