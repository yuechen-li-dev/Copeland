using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Measurement;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Compilation;
using Machina.Layout.Documents;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class StandardBadgeTests
{
    [Fact]
    public void Badge_DefaultIntrinsicSize_IsDeterministic()
    {
        var theme = StandardTheme.Default;
        var style = theme.Badge.Secondary;
        var lowered = UiLowerer.Lower(
            UI.Row(
                id: "root",
                children:
                [
                    StandardUI.Badge("Admin", id: "badge", theme: theme),
                ]));
        var shellFrame = Assert.IsType<FixedFrame>(lowered.Rows.Single(row => row.Id == new NodeId("badge")).Frame);
        var measured = DeterministicTextMeasurer.Instance.MeasureText(
            "Admin",
            style.TextStyle with
            {
                Color = style.Foreground,
                AlignX = style.TextAlignX,
                AlignY = style.TextAlignY,
            });

        Assert.Equal(Math.Max(style.MinWidth, measured.Width + style.HorizontalAllowance), shellFrame.Width);
        Assert.Equal(style.Height, shellFrame.Height);
        Assert.True(shellFrame.Width >= 0);
        Assert.True(shellFrame.Height >= 0);
    }

    [Fact]
    public void Badge_TextRegion_StaysInsideShell()
    {
        var resolved = ResolveNode(
            UI.Row(
                id: "root",
                children:
                [
                    StandardUI.Badge("Admin", id: "badge"),
                ]));
        var shell = RectOf(resolved, "badge");
        var labelRegion = RectOf(resolved, "badge.label-region");

        Assert.True(labelRegion.Width > 0);
        Assert.True(labelRegion.Height > 0);
        Assert.True(labelRegion.X >= shell.X);
        Assert.True(labelRegion.Y >= shell.Y);
        Assert.True(labelRegion.X + labelRegion.Width <= shell.X + shell.Width);
        Assert.True(labelRegion.Y + labelRegion.Height <= shell.Y + shell.Height);
    }

    [Fact]
    public void Badge_TextPlacement_UsesVerticalCenterOrOffset()
    {
        var theme = StandardTheme.Default;
        var style = theme.Badge.Secondary;
        var lowered = UiLowerer.Lower(StandardUI.Badge("Admin", id: "badge", theme: theme));
        var resolved = ResolveNode(
            UI.Row(
                id: "root",
                children:
                [
                    StandardUI.Badge("Admin", id: "badge", theme: theme),
                ]));
        var shell = RectOf(resolved, "badge");
        var labelRegion = RectOf(resolved, "badge.label-region");
        var labelStyle = lowered.TextStyles[new NodeId("badge.label")];

        Assert.Equal(TextAlignY.Center, labelStyle.AlignY);
        Assert.Equal(style.TextAlignY, labelStyle.AlignY);
        Assert.Equal(shell.Y + Math.Max(0, style.TextOffsetY), labelRegion.Y);
        Assert.True(labelRegion.Y > shell.Y, "Default badge label region should not be flush to the top edge.");
    }

    [Fact]
    public void Badge_Row_DoesNotOverflowWithGalleryExamples()
    {
        var theme = StandardTheme.Default;
        var resolved = ResolveNode(
            UI.Row(
                id: "badges-row",
                gap: 8,
                children:
                [
                    StandardUI.Badge("Stable", id: "badge-stable", theme: theme),
                    StandardUI.Badge("Alert", id: "badge-alert", theme: theme, variant: Components.BadgeVariant.Destructive),
                ]),
            width: 140,
            height: 24);
        var row = RectOf(resolved, "badges-row");
        var stableShell = RectOf(resolved, "badge-stable");
        var alertShell = RectOf(resolved, "badge-alert");
        var stableLabelRegion = RectOf(resolved, "badge-stable.label-region");
        var alertLabelRegion = RectOf(resolved, "badge-alert.label-region");

        Assert.True(stableShell.Width > 0 && stableShell.Height > 0);
        Assert.True(alertShell.Width > 0 && alertShell.Height > 0);
        Assert.True(stableShell.X >= row.X);
        Assert.True(alertShell.X + alertShell.Width <= row.X + row.Width);
        Assert.True(stableShell.X + stableShell.Width <= alertShell.X);
        Assert.True(stableLabelRegion.X + stableLabelRegion.Width <= stableShell.X + stableShell.Width);
        Assert.True(alertLabelRegion.X + alertLabelRegion.Width <= alertShell.X + alertShell.Width);
    }

    [Fact]
    public void Badge_CustomStyle_OverridesPlacementLocally()
    {
        var theme = StandardTheme.Default;
        var style = theme.Badge.Secondary with
        {
            Background = ColorToken.Hex(0x112233FF),
            Foreground = ColorToken.Hex(0xFFF7EDFF),
            MinWidth = 70,
            Height = 24,
            HorizontalAllowance = 20,
            TextOffsetY = 2,
            TextStyle = theme.Badge.Secondary.TextStyle with
            {
                Size = TextSize.Sm,
            },
        };

        var lowered = UiLowerer.Lower(
            UI.Row(
                id: "root",
                children:
                [
                    StandardUI.Badge("Ops", id: "badge", theme: theme, style: style),
                ]));
        var resolved = ResolveNode(
            UI.Row(
                id: "root",
                children:
                [
                    StandardUI.Badge("Ops", id: "badge", theme: theme, style: style),
                ]),
            width: 160,
            height: 28);
        var shell = RectOf(resolved, "badge");
        var labelRegion = RectOf(resolved, "badge.label-region");
        var shellStyle = lowered.Styles[new NodeId("badge")];
        var shellFrame = Assert.IsType<FixedFrame>(lowered.Rows.Single(row => row.Id == new NodeId("badge")).Frame);

        Assert.Equal(style.Background, shellStyle.Background);
        Assert.Equal(style.Foreground, shellStyle.Foreground);
        Assert.Equal(style.Height, shellFrame.Height);
        Assert.True(shellFrame.Width >= style.MinWidth);
        Assert.Equal(shell.Y + style.TextOffsetY, labelRegion.Y);
        Assert.True(labelRegion.Y > shell.Y);
    }

    private static ResolvedLayoutDocument ResolveNode(UiNode root, double width = 120, double height = 24)
    {
        var lowered = UiLowerer.Lower(root);
        var document = LayoutCompiler.CompileLayoutRows(lowered.Rows);
        return LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, width, height));
    }

    private static Rect RectOf(ResolvedLayoutDocument resolved, string id)
    {
        return resolved.Nodes[new NodeId(id)].Rect;
    }
}
