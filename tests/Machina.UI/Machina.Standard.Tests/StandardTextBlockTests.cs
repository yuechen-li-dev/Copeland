using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;
using Machina.Standard.Components;
using Machina.Standard.Text;
using Machina.Standard.Theme;
using Xunit;
using StandardText = Machina.Standard.Text.Text;

namespace Machina.Standard.Tests;

public sealed class StandardTextBlockTests
{
    [Fact]
    public void StandardUI_TextBlock_CreatesExpectedNodeMetadata()
    {
        var component = StandardUI.TextBlock(
            text: StandardText.Plain("Hello from Standard.Text"),
            id: "body-copy",
            theme: StandardTheme.Default);
        var lowered = UiLowerer.Lower(component);

        var rootRow = Assert.Single(lowered.Rows, row => row.Id == new NodeId("body-copy"));
        Assert.IsType<RectNode>(component);
        Assert.Equal("body-copy.content", Assert.Single(lowered.NodePayloads.Keys).Value);
        Assert.Equal("RichText", Assert.Single(lowered.Rows, row => row.Id == new NodeId("body-copy.content")).DebugLabel);
        Assert.Equal("body-copy", rootRow.Id.Value);
    }

    [Fact]
    public void TextBlock_DefaultThemePolicy_IsStable()
    {
        var theme = StandardTheme.Default;
        var lowered = UiLowerer.Lower(StandardUI.TextBlock(
            text: StandardText.Markup("[docs](https://example.test)"),
            id: "body-copy",
            theme: theme));

        var metadata = Assert.IsType<StandardTextBlockMetadata>(lowered.NodePayloads[new NodeId("body-copy.content")]);

        Assert.Equal(theme.Colors.Foreground, metadata.Foreground);
        Assert.Equal(theme.Colors.Primary, metadata.LinkForeground);
        Assert.Equal(MachinaTextWrap.Word, metadata.Text.Wrap);
        Assert.Equal(MachinaTextOverflow.Clip, metadata.Text.Overflow);
    }

    [Fact]
    public void TextBlock_PreservesMachinaTextSpec()
    {
        var spec = StandardText.Markup(
            "- One\n- Two",
            variant: MachinaTextVariant.Caption,
            wrap: MachinaTextWrap.Word,
            overflow: MachinaTextOverflow.Clip,
            align: MachinaTextAlign.Center,
            leading: MachinaTextLeading.Loose,
            blockGap: 12,
            listGap: 4,
            verticalAlign: MachinaTextVerticalAlign.Bottom);

        var lowered = UiLowerer.Lower(StandardUI.TextBlock(text: spec, id: "body-copy"));
        var metadata = Assert.IsType<StandardTextBlockMetadata>(lowered.NodePayloads[new NodeId("body-copy.content")]);

        Assert.Same(spec, metadata.Text);
    }
}
