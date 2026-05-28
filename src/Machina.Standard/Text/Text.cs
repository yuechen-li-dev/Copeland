namespace Machina.Standard.Text;

public static class Text
{
    public static MachinaTextSpec Plain(
        string text,
        MachinaTextVariant variant = MachinaTextVariant.Body,
        MachinaTextWrap wrap = MachinaTextWrap.Word,
        MachinaTextOverflow overflow = MachinaTextOverflow.Clip,
        MachinaTextAlign align = MachinaTextAlign.Start,
        MachinaTextLeading leading = default,
        double blockGap = 8,
        double listGap = 2,
        MachinaTextVerticalAlign verticalAlign = MachinaTextVerticalAlign.Top)
    {
        return new MachinaTextSpec(
            new PlainTextSource(text),
            variant,
            wrap,
            overflow,
            align,
            leading,
            blockGap,
            listGap,
            verticalAlign);
    }

    public static MachinaTextSpec Markup(
        string text,
        MachinaTextVariant variant = MachinaTextVariant.Body,
        MachinaTextWrap wrap = MachinaTextWrap.Word,
        MachinaTextOverflow overflow = MachinaTextOverflow.Clip,
        MachinaTextAlign align = MachinaTextAlign.Start,
        MachinaTextLeading leading = default,
        double blockGap = 8,
        double listGap = 2,
        MachinaTextVerticalAlign verticalAlign = MachinaTextVerticalAlign.Top)
    {
        return new MachinaTextSpec(
            new MachinaMarkupSource(text),
            variant,
            wrap,
            overflow,
            align,
            leading,
            blockGap,
            listGap,
            verticalAlign);
    }

    public static ParagraphBlock Paragraph(string text)
    {
        return new ParagraphBlock([new TextRun(text)]);
    }

    public static ParagraphBlock Paragraph(params MachinaInline[] inline)
    {
        return new ParagraphBlock(inline);
    }

    public static BulletListBlock BulletList(params MachinaBulletItem[] items)
    {
        return new BulletListBlock(items);
    }

    public static MachinaBulletItem Item(string text)
    {
        return new MachinaBulletItem([new TextRun(text)]);
    }

    public static MachinaBulletItem Item(MachinaInline[] inline, params MachinaBulletItem[] children)
    {
        return new MachinaBulletItem(inline, children);
    }

    public static TextRun Run(string text)
    {
        return new TextRun(text);
    }

    public static StrongRun Strong(params MachinaInline[] children)
    {
        return new StrongRun(children);
    }

    public static EmphasisRun Emphasis(params MachinaInline[] children)
    {
        return new EmphasisRun(children);
    }

    public static CodeRun Code(string text)
    {
        return new CodeRun(text);
    }

    public static LinkRun Link(string href, params MachinaInline[] children)
    {
        return new LinkRun(href, children);
    }
}
