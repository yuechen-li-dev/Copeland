using Machina.Standard.Text;
using Xunit;
using StandardText = Machina.Standard.Text.Text;

namespace Machina.Standard.Tests.Text;

public sealed class MachinaTextLayoutTests
{
    private static readonly IMachinaTextMeasurer Measurer = MachinaTextMeasurers.Deterministic;

    [Fact]
    public void Layout_PlainParagraph_NoWrap_ProducesSingleLine()
    {
        var spec = StandardText.Plain("Hello world", wrap: MachinaTextWrap.None);

        var result = MachinaTextLayoutEngine.Layout(spec, new MachinaTextBox(0, 0, 200, 40), Measurer);

        var line = Assert.Single(result.Lines);
        var run = Assert.Single(line.Runs);

        Assert.False(result.HasOverflow);
        Assert.Equal("Hello world", run.Text);
        Assert.Equal(0, line.Bounds.X);
        Assert.Equal(0, line.Bounds.Y);
        AssertClose(130, line.Bounds.Width);
        AssertClose(19.6, line.Bounds.Height);
    }

    [Fact]
    public void Layout_WordWrap_SplitsAtWhitespace()
    {
        var spec = StandardText.Plain("Hello world from Machina", wrap: MachinaTextWrap.Word);

        var result = MachinaTextLayoutEngine.Layout(spec, new MachinaTextBox(0, 0, 150, 80), Measurer);

        Assert.False(result.HasOverflow);
        Assert.Collection(
            result.Lines,
            line => Assert.Equal("Hello world", LineText(line)),
            line => Assert.Equal("from Machina", LineText(line)));
    }

    [Fact]
    public void Layout_NoWrap_ReportsWidthOverflow()
    {
        var spec = StandardText.Plain("Hello world from Machina", wrap: MachinaTextWrap.None);

        var result = MachinaTextLayoutEngine.Layout(spec, new MachinaTextBox(0, 0, 40, 40), Measurer);

        Assert.True(result.HasOverflow);
        Assert.Single(result.Lines);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == MachinaTextLayoutDiagnosticCode.ContentOverflow);
    }

    [Fact]
    public void Layout_AlignCenter_OffsetsLineX()
    {
        var spec = StandardText.Plain("Hello", wrap: MachinaTextWrap.None, align: MachinaTextAlign.Center);

        var result = MachinaTextLayoutEngine.Layout(spec, new MachinaTextBox(0, 0, 200, 40), Measurer);

        var line = Assert.Single(result.Lines);
        AssertClose(71, line.Bounds.X);
    }

    [Fact]
    public void Layout_AlignEnd_OffsetsLineX()
    {
        var spec = StandardText.Plain("Hello", wrap: MachinaTextWrap.None, align: MachinaTextAlign.End);

        var result = MachinaTextLayoutEngine.Layout(spec, new MachinaTextBox(0, 0, 200, 40), Measurer);

        var line = Assert.Single(result.Lines);
        AssertClose(142, line.Bounds.X);
    }

    [Fact]
    public void Layout_VerticalAlignCenter_OffsetsContentY()
    {
        var spec = StandardText.Plain("Hello", wrap: MachinaTextWrap.None, verticalAlign: MachinaTextVerticalAlign.Center);

        var result = MachinaTextLayoutEngine.Layout(spec, new MachinaTextBox(0, 0, 200, 100), Measurer);

        var line = Assert.Single(result.Lines);
        AssertClose(40.2, line.Bounds.Y);
    }

    [Fact]
    public void Layout_VerticalAlignBottom_OffsetsContentY()
    {
        var spec = StandardText.Plain("Hello", wrap: MachinaTextWrap.None, verticalAlign: MachinaTextVerticalAlign.Bottom);

        var result = MachinaTextLayoutEngine.Layout(spec, new MachinaTextBox(0, 0, 200, 100), Measurer);

        var line = Assert.Single(result.Lines);
        AssertClose(80.4, line.Bounds.Y);
    }

    [Fact]
    public void Layout_BlockGap_SeparatesParagraphs()
    {
        var document = new MachinaTextDocument(
        [
            StandardText.Paragraph("First"),
            StandardText.Paragraph("Second"),
        ]);

        var policy = new MachinaTextPolicy(blockGap: 12);
        var result = MachinaTextLayoutEngine.Layout(document, policy, new MachinaTextBox(0, 0, 200, 100), Measurer);

        Assert.Equal(2, result.Lines.Count);
        AssertClose(0, result.Lines[0].Bounds.Y);
        AssertClose(31.6, result.Lines[1].Bounds.Y);
    }

    [Fact]
    public void Layout_Leading_ChangesLineHeight()
    {
        var box = new MachinaTextBox(0, 0, 200, 100);

        var tight = MachinaTextLayoutEngine.Layout(StandardText.Plain("Hello", leading: MachinaTextLeading.Tight), box, Measurer);
        var normal = MachinaTextLayoutEngine.Layout(StandardText.Plain("Hello", leading: MachinaTextLeading.Normal), box, Measurer);
        var loose = MachinaTextLayoutEngine.Layout(StandardText.Plain("Hello", leading: MachinaTextLeading.Loose), box, Measurer);
        var numeric = MachinaTextLayoutEngine.Layout(StandardText.Plain("Hello", leading: MachinaTextLeading.Numeric(2.0)), box, Measurer);

        AssertClose(16.1, Assert.Single(tight.Lines).Bounds.Height);
        AssertClose(19.6, Assert.Single(normal.Lines).Bounds.Height);
        AssertClose(22.4, Assert.Single(loose.Lines).Bounds.Height);
        AssertClose(28, Assert.Single(numeric.Lines).Bounds.Height);
    }

    [Fact]
    public void Layout_BulletList_ProducesMarkerAndIndentedText()
    {
        var spec = StandardText.Markup("- Parent\n  - Child");

        var result = MachinaTextLayoutEngine.Layout(spec, new MachinaTextBox(0, 0, 200, 120), Measurer);

        Assert.False(result.HasOverflow);
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal("\u2022", result.Lines[0].Runs[0].Text);
        Assert.Contains("Parent", LineText(result.Lines[0]));
        Assert.Equal("\u2022", result.Lines[1].Runs[0].Text);
        Assert.True(result.Lines[1].Bounds.X > result.Lines[0].Bounds.X);
        Assert.True(result.Lines[1].Runs[1].Bounds.X > result.Lines[1].Runs[0].Bounds.X);
    }

    [Fact]
    public void Layout_InlineStrongEmphasisCodeLink_PreservesRunStyle()
    {
        var spec = StandardText.Markup("A **bold** *soft* `code` [docs](https://example.test)");

        var result = MachinaTextLayoutEngine.Layout(spec, new MachinaTextBox(0, 0, 300, 80), Measurer);

        var bold = Assert.Single(result.Runs, run => run.Text == "bold");
        var soft = Assert.Single(result.Runs, run => run.Text == "soft");
        var code = Assert.Single(result.Runs, run => run.Text == "code");
        var docs = Assert.Single(result.Runs, run => run.Text == "docs");

        Assert.True(bold.Style.Strong);
        Assert.True(soft.Style.Emphasis);
        Assert.True(code.Style.Code);
        Assert.Equal(MachinaTextVariant.Mono, code.Style.Variant);
        Assert.Equal("https://example.test", docs.Style.LinkHref);
    }

    [Fact]
    public void Layout_ContentHeightOverflow_IsReported()
    {
        var spec = StandardText.Plain("Hello world from Machina", wrap: MachinaTextWrap.Word);

        var result = MachinaTextLayoutEngine.Layout(spec, new MachinaTextBox(0, 0, 150, 20), Measurer);

        Assert.True(result.HasOverflow);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == MachinaTextLayoutDiagnosticCode.ContentOverflow);
    }

    [Fact]
    public void Layout_BoxTooSmall_IsDiagnostic()
    {
        var spec = StandardText.Plain("Hello");

        var result = MachinaTextLayoutEngine.Layout(spec, new MachinaTextBox(0, 0, 0, -10), Measurer);

        Assert.True(result.HasOverflow);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == MachinaTextLayoutDiagnosticCode.BoxTooSmall);
    }

    [Fact]
    public void Layout_IsDeterministic()
    {
        var spec = StandardText.Markup("- One\n- Two\n\nA `code` line");
        var box = new MachinaTextBox(0, 0, 180, 160);

        var first = MachinaTextLayoutEngine.Layout(spec, box, Measurer);
        var second = MachinaTextLayoutEngine.Layout(spec, box, Measurer);

        Assert.Equivalent(first, second, strict: true);
    }

    [Fact]
    public void Layout_SpecMarkup_UsesParser()
    {
        var spec = StandardText.Markup("- One\n- Two");

        var result = MachinaTextLayoutEngine.Layout(spec, new MachinaTextBox(0, 0, 200, 80), Measurer);

        Assert.Empty(result.ParseDiagnostics);
        Assert.Equal(2, result.Lines.Count);
        Assert.All(result.Lines, line => Assert.Equal("\u2022", line.Runs[0].Text));
    }

    private static string LineText(MachinaTextLineBox line)
    {
        return string.Concat(line.Runs.Select(run => run.Text));
    }

    private static void AssertClose(double expected, double actual)
    {
        Assert.Equal(expected, actual, 3);
    }
}
