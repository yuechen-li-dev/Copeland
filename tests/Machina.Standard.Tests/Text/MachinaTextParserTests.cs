using Machina.Standard.Text;
using Xunit;

namespace Machina.Standard.Tests.Text;

public sealed class MachinaTextParserTests
{
    [Fact]
    public void PlainText_ParsesToSingleParagraph()
    {
        var result = MachinaTextParser.ParsePlain("Hello **not strong**");

        Assert.True(result.Ok);
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(result.Document.Blocks));
        var run = Assert.IsType<TextRun>(Assert.Single(paragraph.Inline));
        Assert.Equal("Hello **not strong**", run.Text);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Markup_ParsesParagraphs()
    {
        var result = MachinaTextParser.ParseMarkup("First paragraph\n\nSecond paragraph");

        Assert.True(result.Ok);
        Assert.Collection(
            result.Document.Blocks,
            first => AssertParagraphText(first, "First paragraph"),
            second => AssertParagraphText(second, "Second paragraph"));
    }

    [Fact]
    public void Markup_ParsesStrongEmphasisCodeLink()
    {
        var result = MachinaTextParser.ParseMarkup("A **bold** and *soft* with `code` plus [docs](https://example.test).");

        Assert.True(result.Ok);
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(result.Document.Blocks));

        Assert.Collection(
            paragraph.Inline,
            run => AssertTextRun(run, "A "),
            run =>
            {
                var strong = Assert.IsType<StrongRun>(run);
                AssertTextRun(Assert.Single(strong.Children), "bold");
            },
            run => AssertTextRun(run, " and "),
            run =>
            {
                var emphasis = Assert.IsType<EmphasisRun>(run);
                AssertTextRun(Assert.Single(emphasis.Children), "soft");
            },
            run => AssertTextRun(run, " with "),
            run =>
            {
                var code = Assert.IsType<CodeRun>(run);
                Assert.Equal("code", code.Text);
            },
            run => AssertTextRun(run, " plus "),
            run =>
            {
                var link = Assert.IsType<LinkRun>(run);
                Assert.Equal("https://example.test", link.Href);
                AssertTextRun(Assert.Single(link.Children), "docs");
            },
            run => AssertTextRun(run, "."));
    }

    [Fact]
    public void Markup_ParsesBulletList()
    {
        var result = MachinaTextParser.ParseMarkup("- One\n- Two\n  - Child");

        Assert.True(result.Ok);
        var list = Assert.IsType<BulletListBlock>(Assert.Single(result.Document.Blocks));

        Assert.Collection(
            list.Items,
            item =>
            {
                AssertTextRun(Assert.Single(item.Inline), "One");
                Assert.Null(item.Children);
            },
            item =>
            {
                AssertTextRun(Assert.Single(item.Inline), "Two");
                Assert.NotNull(item.Children);
                var child = Assert.Single(item.Children);
                AssertTextRun(Assert.Single(child.Inline), "Child");
            });
    }

    [Fact]
    public void Markup_RejectsHeadingButPreservesText()
    {
        var result = MachinaTextParser.ParseMarkup("# Heading");

        Assert.False(result.Ok);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MachinaTextDiagnosticCode.HeadingForbidden, diagnostic.Code);
        Assert.Equal(0, diagnostic.Index);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(1, diagnostic.Column);
        AssertParagraphText(Assert.Single(result.Document.Blocks), "# Heading");
    }

    [Fact]
    public void Markup_DiagnosesMalformedLink()
    {
        var result = MachinaTextParser.ParseMarkup("Before [broken link after");

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == MachinaTextDiagnosticCode.MalformedLink);
        AssertParagraphText(Assert.Single(result.Document.Blocks), "Before [broken link after");
    }

    [Fact]
    public void Markup_DiagnosesUnclosedInline()
    {
        var result = MachinaTextParser.ParseMarkup("Before **open strong");

        Assert.False(result.Ok);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MachinaTextDiagnosticCode.UnclosedInline, diagnostic.Code);
        Assert.Equal(7, diagnostic.Index);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(8, diagnostic.Column);
        AssertParagraphText(Assert.Single(result.Document.Blocks), "Before **open strong");
    }

    [Fact]
    public void Markup_DiagnosesMaxListDepth()
    {
        var result = MachinaTextParser.ParseMarkup("- One\n  - Two\n    - Three");

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == MachinaTextDiagnosticCode.MaxListDepthExceeded);
        Assert.IsType<BulletListBlock>(result.Document.Blocks[0]);
        AssertParagraphText(result.Document.Blocks[1], "- Three");
    }

    [Fact]
    public void Markup_DiagnosesInvalidEscape()
    {
        var result = MachinaTextParser.ParseMarkup("Bad \\q escape");

        Assert.False(result.Ok);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(MachinaTextDiagnosticCode.InvalidEscape, diagnostic.Code);
        AssertParagraphText(Assert.Single(result.Document.Blocks), "Bad q escape");
    }

    private static void AssertParagraphText(MachinaTextBlock block, string expectedText)
    {
        var paragraph = Assert.IsType<ParagraphBlock>(block);
        var text = string.Concat(paragraph.Inline.Select(InlineToText));
        Assert.Equal(expectedText, text);
    }

    private static string InlineToText(MachinaInline inline)
    {
        return inline switch
        {
            TextRun text => text.Text,
            StrongRun strong => string.Concat(strong.Children.Select(InlineToText)),
            EmphasisRun emphasis => string.Concat(emphasis.Children.Select(InlineToText)),
            CodeRun code => code.Text,
            LinkRun link => string.Concat(link.Children.Select(InlineToText)),
            _ => string.Empty,
        };
    }

    private static void AssertTextRun(MachinaInline inline, string expectedText)
    {
        var textRun = Assert.IsType<TextRun>(inline);
        Assert.Equal(expectedText, textRun.Text);
    }
}
