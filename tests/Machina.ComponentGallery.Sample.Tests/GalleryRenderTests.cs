using Machina.ComponentGallery.Sample;
using Machina.Core.Styling;
using Dominatus.Core.Runtime;
using Machina.Dominatus.Rendering.Commands;
using Machina.Pipeline;
using Machina.Standard.Components;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.ComponentGallery.Sample.Tests;

public sealed class GalleryRenderTests
{
    [Fact]
    public void Gallery_ContainsExpectedComponentIds()
    {
        var frame = Render(GalleryState.Default, StandardTheme.Default);
        var ids = frame.Resolved.Nodes.Keys.Select(key => key.Value).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("header/gallery-title", ids);
        Assert.Contains("text-section/text-plain", ids);
        Assert.Contains("text-section/text-markup", ids);
        Assert.Contains("text-section/text-bullets", ids);
        Assert.Contains("buttons-section/button-primary", ids);
        Assert.Contains("buttons-section/button-outline", ids);
        Assert.Contains("selection-section/checkbox-unchecked", ids);
        Assert.Contains("selection-section/checkbox-checked", ids);
        Assert.Contains("selection-section/switch-off", ids);
        Assert.Contains("selection-section/switch-on", ids);
        Assert.Contains("actions-section/live-checkbox", ids);
        Assert.Contains("actions-section/live-switch", ids);
        Assert.Contains("input-section/input-empty", ids);
        Assert.Contains("input-section/input-value", ids);
        Assert.Contains("badges-section/badge-stable", ids);
        Assert.Contains("badges-section/separator-horizontal", ids);
        Assert.Contains("cards-section/simple-card", ids);
        Assert.Contains("cards-section/rich-card", ids);
        Assert.Contains("theme-section/theme-card", ids);
    }

    [Fact]
    public void Gallery_CheckedCheckbox_HasVisibleMark()
    {
        var frame = Render(GalleryState.Default, StandardTheme.Default);

        var mark = Assert.Single(
            frame.RenderCommands.OfType<FillRectCommand>(),
            command => command.Id == "selection-section/checkbox-checked.mark");

        var box = Assert.Single(
            frame.RenderCommands.OfType<FillRectCommand>(),
            command => command.Id == "selection-section/checkbox-checked.box");

        Assert.NotEqual(ColorToken.Hex(0x00000000), mark.Color);
        Assert.NotEqual(box.Color, mark.Color);
    }

    [Fact]
    public void Gallery_TextBlock_EmitsWrappedDrawText()
    {
        var frame = Render(GalleryState.Default, StandardTheme.Default);
        var commands = frame.RenderCommands
            .OfType<DrawTextCommand>()
            .Where(command => command.Id.StartsWith("text-section/", StringComparison.Ordinal) || command.Id.StartsWith("cards-section/rich-card-textblock.content.", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(commands);
        Assert.Contains(commands, command => command.Text == "Standard.Text");
        Assert.Contains(commands, command => command.Text == "markup");
        Assert.Contains(commands, command => command.Text == "code");
        Assert.Contains(commands, command => command.Text == "\u2022");
        Assert.Contains(commands, command => command.Text == "deterministic");
    }

    [Fact]
    public void Gallery_CustomTheme_PropagatesToComponents()
    {
        var customTheme = StandardTheme.Default with
        {
            Colors = StandardTheme.Default.Colors with
            {
                Background = ColorToken.Hex(0xFFF7EDFF),
                Foreground = ColorToken.Hex(0x431407FF),
                Primary = ColorToken.Hex(0x9A3412FF),
                PrimaryForeground = ColorToken.Hex(0xFFF7EDFF),
                Border = ColorToken.Hex(0xFDBA74FF),
                MutedForeground = ColorToken.Hex(0x9A3412FF),
            },
            Card = StandardTheme.Default.Card with
            {
                Default = StandardTheme.Default.Card.Default with
                {
                    Background = ColorToken.Hex(0xFFEDD5FF),
                    BorderColor = ColorToken.Hex(0xFDBA74FF),
                    ContentInset = 16,
                },
            },
            Button = StandardTheme.Default.Button with
            {
                Default = StandardTheme.Default.Button.Default with
                {
                    Background = ColorToken.Hex(0x9A3412FF),
                    Foreground = ColorToken.Hex(0xFFF7EDFF),
                    Width = 140,
                },
            },
            Checkbox = StandardTheme.Default.Checkbox with
            {
                Default = StandardTheme.Default.Checkbox.Default with
                {
                    MarkColor = ColorToken.Hex(0x7C2D12FF),
                },
            },
        };

        var state = GalleryState.Default with { LiveCheckboxChecked = true };
        var frame = Render(state, customTheme);

        var rootFill = Assert.Single(frame.RenderCommands.OfType<FillRectCommand>(), command => command.Id == "root");
        Assert.Equal(customTheme.Colors.Background, rootFill.Color);

        Assert.Equal(customTheme.Card.Default.Background, frame.Lowering.Styles[new Machina.Layout.Rows.NodeId("cards-section/simple-card")].Background);
        Assert.Equal(customTheme.Button.Default.Background, frame.Lowering.Styles[new Machina.Layout.Rows.NodeId("buttons-section/button-primary")].Background);

        var liveMark = Assert.Single(frame.RenderCommands.OfType<FillRectCommand>(), command => command.Id == "actions-section/live-checkbox.mark");
        Assert.Equal(customTheme.Checkbox.Default.MarkColor, liveMark.Color);

        var richTextMetadata = Assert.IsType<StandardTextBlockMetadata>(frame.Lowering.NodePayloads[new Machina.Layout.Rows.NodeId("text-section/text-markup.content")]);
        Assert.Equal(customTheme.Colors.Foreground, richTextMetadata.Foreground);
    }

    [Fact]
    public void Gallery_RenderCommands_AreDeterministic()
    {
        var first = Render(GalleryState.Default, StandardTheme.Default);
        var second = Render(GalleryState.Default, StandardTheme.Default);

        Assert.Equal(Summarize(first.RenderCommands), Summarize(second.RenderCommands));
    }

    private static MachinaFrame Render(GalleryState state, StandardTheme theme)
    {
        var document = GalleryScreen.Build(state, theme);
        return new MachinaRasterPipeline().Render(document, GalleryScreen.Width, GalleryScreen.Height);
    }

    private static string[] Summarize(IReadOnlyList<IActuationCommand> commands)
    {
        return commands.Select(
            command => command switch
            {
                FillRectCommand fill => $"fill|{fill.Id}|{fill.Rect.X},{fill.Rect.Y},{fill.Rect.Width},{fill.Rect.Height}|{fill.Color}",
                StrokeRectCommand stroke => $"stroke|{stroke.Id}|{stroke.Rect.X},{stroke.Rect.Y},{stroke.Rect.Width},{stroke.Rect.Height}|{stroke.Color}|{stroke.Thickness}",
                DrawTextCommand text => $"text|{text.Id}|{text.Rect.X},{text.Rect.Y},{text.Rect.Width},{text.Rect.Height}|{text.Text}|{text.Style.Color}|{text.Style.Size}",
                PushClipCommand clip => $"clip|{clip.Id}|{clip.Rect.X},{clip.Rect.Y},{clip.Rect.Width},{clip.Rect.Height}",
                PopClipCommand => "pop-clip",
                BeginFrameCommand begin => $"begin|{begin.Width}|{begin.Height}",
                EndFrameCommand => "end",
                _ => command.GetType().Name,
            })
            .ToArray();
    }
}
