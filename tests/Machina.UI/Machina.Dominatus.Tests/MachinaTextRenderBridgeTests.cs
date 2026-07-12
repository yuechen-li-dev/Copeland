using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Bridge;
using Machina.Dominatus.Rendering.Commands;
using Machina.Standard.Text;
using Xunit;
using StandardText = Machina.Standard.Text.Text;

namespace Machina.Dominatus.Tests;

public sealed class MachinaTextRenderBridgeTests
{
    private static readonly MachinaTextRenderStyle RenderStyle = new(
        new TextStyle(Color: ColorToken.White, Size: TextSize.Md),
        LinkColor: ColorToken.Hex(0x3B82F6FF));

    [Fact]
    public void TextLayoutRenderBridge_EmitsDrawTextForEachRun()
    {
        var layout = Layout(StandardText.Markup("Hello **world**"));

        var commands = MachinaTextRenderBridge.ToDrawTextCommands("rich-text", layout, RenderStyle);
        var visibleRunCount = layout.Runs.Count(run => !string.IsNullOrWhiteSpace(run.Text));

        Assert.Equal(visibleRunCount, commands.Count);
        Assert.All(commands, command => Assert.False(string.IsNullOrWhiteSpace(command.Text)));
    }

    [Fact]
    public void TextLayoutRenderBridge_UsesRunBounds()
    {
        var layout = Layout(StandardText.Markup("Hello **world**"));

        var commands = MachinaTextRenderBridge.ToDrawTextCommands("rich-text", layout, RenderStyle);
        var visibleRuns = layout.Lines
            .SelectMany(line => line.Runs)
            .Where(run => !string.IsNullOrWhiteSpace(run.Text))
            .ToList();

        Assert.Equal(visibleRuns.Count, commands.Count);

        for (var index = 0; index < visibleRuns.Count; index++)
        {
            var run = visibleRuns[index];
            var command = commands[index];

            Assert.Equal(run.Text, command.Text);
            Assert.Equal(run.Bounds.X, command.Rect.X);
            Assert.Equal(run.Bounds.Y, command.Rect.Y);
            Assert.Equal(run.Bounds.Width, command.Rect.Width);
            Assert.Equal(run.Bounds.Height, command.Rect.Height);
        }
    }

    [Fact]
    public void TextLayoutRenderBridge_IsDeterministic()
    {
        var layout = Layout(StandardText.Markup("- One\n- Two\n\nTail `code_value`"));

        var first = MachinaTextRenderBridge.ToDrawTextCommands("rich-text", layout, RenderStyle);
        var second = MachinaTextRenderBridge.ToDrawTextCommands("rich-text", layout, RenderStyle);

        Assert.Equal(first, second);
    }

    [Fact]
    public void TextLayoutRenderBridge_PreservesTextOrder()
    {
        var layout = Layout(StandardText.Markup("- First\n- Second\n\nTail"));

        var commands = MachinaTextRenderBridge.ToDrawTextCommands("rich-text", layout, RenderStyle);

        Assert.Equal(["\u2022", "First", "\u2022", "Second", "Tail"], commands.Select(command => command.Text).ToArray());
    }

    [Fact]
    public void TextLayoutRenderBridge_HandlesInlineStylesWithoutRendererCoupling()
    {
        var layout = Layout(StandardText.Markup("A **bold** *soft* `code_value` [docs](https://example.test)"));

        var commands = MachinaTextRenderBridge.ToDrawTextCommands("rich-text", layout, RenderStyle);

        Assert.Contains(commands, command => command.Text == "bold" && command.Style.Size == TextSize.Md);
        Assert.Contains(commands, command => command.Text == "soft" && command.Style.Size == TextSize.Md);
        Assert.Contains(commands, command => command.Text == "code_value" && command.Style.Size == TextSize.Sm);
        Assert.Contains(commands, command => command.Text == "docs" && command.Style.Color == ColorToken.Hex(0x3B82F6FF));
    }

    [Fact]
    public void TextLayoutRenderBridge_ReportsOrSkipsOverflowConsistently()
    {
        var layout = MachinaTextLayoutEngine.Layout(
            StandardText.Plain("Hello world from Machina", wrap: MachinaTextWrap.None),
            new MachinaTextBox(0, 0, 40, 40),
            MachinaTextMeasurers.Deterministic);

        var commands = MachinaTextRenderBridge.ToDrawTextCommands("overflow", layout, RenderStyle);
        var boxRight = layout.Box.X + layout.Box.Width;

        Assert.True(layout.HasOverflow);
        Assert.Single(commands);
        Assert.True(commands[0].Rect.X + commands[0].Rect.Width > boxRight);
    }

    private static MachinaTextLayoutResult Layout(MachinaTextSpec spec)
    {
        return MachinaTextLayoutEngine.Layout(spec, new MachinaTextBox(0, 0, 320, 160), MachinaTextMeasurers.Deterministic);
    }
}
