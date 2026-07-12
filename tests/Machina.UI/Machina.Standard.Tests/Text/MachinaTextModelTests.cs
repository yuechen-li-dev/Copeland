using Machina.Standard.Text;
using Xunit;
using StandardText = Machina.Standard.Text.Text;

namespace Machina.Standard.Tests.Text;

public sealed class MachinaTextModelTests
{
    [Fact]
    public void TextSpec_DefaultPolicy_IsStable()
    {
        var spec = new MachinaTextSpec(new PlainTextSource("Hello"));

        Assert.IsType<PlainTextSource>(spec.Source);
        Assert.Equal(MachinaTextVariant.Body, spec.Variant);
        Assert.Equal(MachinaTextWrap.Word, spec.Wrap);
        Assert.Equal(MachinaTextOverflow.Clip, spec.Overflow);
        Assert.Equal(MachinaTextAlign.Start, spec.Align);
        Assert.Equal(MachinaTextLeading.Normal, spec.Leading);
        Assert.Equal(8, spec.BlockGap);
        Assert.Equal(2, spec.ListGap);
        Assert.Equal(MachinaTextVerticalAlign.Top, spec.VerticalAlign);
    }

    [Fact]
    public void TextHelpers_CreateExpectedDocumentBlocks()
    {
        var paragraph = StandardText.Paragraph(
            StandardText.Run("Read "),
            StandardText.Strong(StandardText.Run("carefully")),
            StandardText.Run(" at "),
            StandardText.Link("https://example.test", StandardText.Run("docs")));
        var list = StandardText.BulletList(
            StandardText.Item("First"),
            StandardText.Item(
                [StandardText.Run("Second")],
                StandardText.Item("Nested")));
        var plain = StandardText.Plain("Plain", variant: MachinaTextVariant.Label);
        var markup = StandardText.Markup("**Markup**", wrap: MachinaTextWrap.None);

        Assert.Collection(
            paragraph.Inline,
            inline => Assert.Equal("Read ", Assert.IsType<TextRun>(inline).Text),
            inline => Assert.Equal("carefully", Assert.IsType<TextRun>(Assert.Single(Assert.IsType<StrongRun>(inline).Children)).Text),
            inline => Assert.Equal(" at ", Assert.IsType<TextRun>(inline).Text),
            inline =>
            {
                var link = Assert.IsType<LinkRun>(inline);
                Assert.Equal("https://example.test", link.Href);
                Assert.Equal("docs", Assert.IsType<TextRun>(Assert.Single(link.Children)).Text);
            });

        Assert.Equal(2, list.Items.Count);
        Assert.Equal("First", Assert.IsType<TextRun>(Assert.Single(list.Items[0].Inline)).Text);
        Assert.Equal("Second", Assert.IsType<TextRun>(Assert.Single(list.Items[1].Inline)).Text);
        Assert.NotNull(list.Items[1].Children);
        var nested = Assert.Single(list.Items[1].Children!);
        Assert.Equal("Nested", Assert.IsType<TextRun>(Assert.Single(nested.Inline)).Text);
        Assert.Equal(MachinaTextVariant.Label, plain.Variant);
        Assert.Equal(MachinaTextWrap.None, markup.Wrap);
    }

    [Fact]
    public void ModelValidation_RejectsNullAndInvalidPolicy()
    {
        Assert.Throws<ArgumentNullException>(() => new PlainTextSource(null!));
        Assert.Throws<ArgumentNullException>(() => new TextRun(null!));
        Assert.Throws<ArgumentNullException>(() => new MachinaTextDocument(null!));
        Assert.Throws<ArgumentException>(() => new ParagraphBlock([new TextRun("ok"), null!]));
        Assert.Throws<ArgumentOutOfRangeException>(() => MachinaTextLeading.Numeric(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MachinaTextSpec(new PlainTextSource("ok"), blockGap: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MachinaTextSpec(new PlainTextSource("ok"), listGap: double.NaN));
    }
}
