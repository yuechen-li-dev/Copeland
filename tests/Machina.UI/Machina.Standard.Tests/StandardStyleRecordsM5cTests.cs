using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Compilation;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class StandardStyleRecordsM5cTests
{
    [Fact]
    public void StandardButton_ExplicitStyleOverridesShellAndText()
    {
        var style = StandardTheme.Default.Button.Default with
        {
            Background = ColorToken.Hex(0xFF0000FF),
            Foreground = ColorToken.Hex(0x00FF00FF),
            BorderColor = ColorToken.Hex(0x0000FFFF),
            BorderThickness = 2,
            Width = 140,
            Height = 32,
            TextStyle = StandardTheme.Default.Button.Default.TextStyle with
            {
                Size = TextSize.Sm,
                AlignX = TextAlignX.Center,
                AlignY = TextAlignY.Center,
            },
        };

        var lowered = LowerHostedComponent(StandardUI.Button("Go", id: "go", action: UiAction.Named("go"), style: style), style.Width, style.Height);
        var resolved = Resolve(lowered, 320, 120);
        var shellRect = resolved.Nodes[new NodeId("host/go")].Rect;

        Assert.Equal(style.Width, shellRect.Width);
        Assert.Equal(style.Height, shellRect.Height);
        Assert.Equal(style.Background, lowered.Styles[new NodeId("host/go")].Background);
        Assert.Equal(style.BorderColor, lowered.Styles[new NodeId("host/go")].BorderColor);
        Assert.Equal(style.BorderThickness, lowered.Styles[new NodeId("host/go")].BorderThickness);
        Assert.Equal(style.TextStyle.Size, lowered.TextStyles[new NodeId("host/go.label")].Size);
        Assert.Equal(style.TextStyle.AlignX, lowered.TextStyles[new NodeId("host/go.label")].AlignX);
        Assert.Equal(style.TextStyle.AlignY, lowered.TextStyles[new NodeId("host/go.label")].AlignY);

        var actionNode = lowered.Actions.Single(pair => pair.Value.Name == "go").Key;
        Assert.Equal("host/go", actionNode.Value);
        Assert.Equal(0, lowered.Styles[new NodeId("host/go")].Padding);
    }

    [Fact]
    public void StandardButton_DefaultStyleMatchesThemePrimary()
    {
        var lowered = UiLowerer.Lower(StandardUI.Button("Go", id: "go", action: UiAction.Named("go")));
        var themeStyle = StandardTheme.Default.Button.Default;

        Assert.Equal(themeStyle.Background, lowered.Styles[new NodeId("go")].Background);
        Assert.Equal(themeStyle.Foreground, lowered.Styles[new NodeId("go")].Foreground);
        Assert.Equal(themeStyle.BorderColor, lowered.Styles[new NodeId("go")].BorderColor);
        Assert.Equal(themeStyle.BorderThickness, lowered.Styles[new NodeId("go")].BorderThickness);
        Assert.Equal(TextSize.Md, lowered.TextStyles[new NodeId("go.label")].Size);
    }

    [Fact]
    public void StandardCard_ExplicitStyleControlsShellAndContentInset()
    {
        var style = StandardTheme.Default.Card.Default with
        {
            Background = ColorToken.Hex(0x111827FF),
            BorderColor = ColorToken.Hex(0x334155FF),
            BorderThickness = 2,
            ContentInset = 24,
        };

        var lowered = LowerHostedComponent(
            StandardUI.Card(id: "card", style: style, child: UI.Text("Hello", id: "text")),
            220,
            120);
        var resolved = Resolve(lowered, 400, 220);

        Assert.Equal(style.Background, lowered.Styles[new NodeId("host/card")].Background);
        Assert.Equal(style.BorderColor, lowered.Styles[new NodeId("host/card")].BorderColor);
        Assert.Equal(style.BorderThickness, lowered.Styles[new NodeId("host/card")].BorderThickness);

        var shellRect = resolved.Nodes[new NodeId("host/card")].Rect;
        var contentRect = resolved.Nodes[new NodeId("host/card.content")].Rect;
        var childRect = resolved.Nodes[new NodeId("host/text")].Rect;

        Assert.Equal(shellRect.X + style.ContentInset, contentRect.X);
        Assert.Equal(shellRect.Y + style.ContentInset, contentRect.Y);
        Assert.Equal(shellRect.Width - (style.ContentInset * 2), contentRect.Width);
        Assert.Equal(shellRect.Height - (style.ContentInset * 2), contentRect.Height);
        Assert.True(childRect.X >= contentRect.X);
        Assert.True(childRect.Y >= contentRect.Y);
    }

    private static UiLoweringResult LowerHostedComponent(UiNode component, double width, double height)
    {
        return UiDocumentLowerer.Lower(UiDocument.Create([
            Row.Root("root"),
            Row.Anchor("host", "root", left: 20, top: 20, width: width, height: height, component: component),
        ]));
    }

    private static ResolvedLayoutDocument Resolve(UiLoweringResult lowered, int width, int height)
    {
        var compiled = LayoutCompiler.CompileLayoutRows(lowered.Rows);
        return LayoutDocumentResolver.ResolveLayoutDocument(compiled, new Rect(0, 0, width, height));
    }
}
