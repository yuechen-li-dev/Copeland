namespace Machina.Fonts.ReferenceRendering;

public enum MachinaTextTokenKind
{
    Word,
    Punctuation,
    Whitespace,
}

public sealed record MachinaTextSpan(int Start, int Length)
{
    public int End => Start + Length;
}

public sealed record MachinaPlaneBounds(
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    public double Width => Right - Left;

    public double Height => Bottom - Top;
}

public sealed record MachinaGlyphPlacement(
    GlyphKey Key,
    ushort? GlyphId,
    MachinaTextSpan SourceSpan,
    double OriginX,
    double BaselineY,
    double Advance,
    MachinaPlaneBounds PlaneBounds,
    int TokenId,
    bool IsWhitespace);

public sealed record MachinaTokenPlacement(
    int Id,
    MachinaTextTokenKind Kind,
    string Text,
    MachinaTextSpan SourceSpan,
    int? AnchorGlyphIndex,
    double? AnchorOriginX,
    double? AnchorOriginY,
    double AdvanceWidth,
    MachinaPlaneBounds? InkBounds);

public sealed record MachinaLinePlacement(
    int Index,
    MachinaTextSpan SourceSpan,
    double BaselineY,
    double AdvanceWidth,
    double LineHeight,
    MachinaPlaneBounds? InkBounds);

public sealed record MachinaGlyphRun(
    string Text,
    IReadOnlyList<MachinaLinePlacement> Lines,
    IReadOnlyList<MachinaTokenPlacement> Tokens,
    IReadOnlyList<MachinaGlyphPlacement> Glyphs);

public static class MachinaTextTokenizer
{
    public static IReadOnlyList<MachinaTokenPlacement> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<MachinaTokenPlacement> tokens = [];
        int start = 0;

        while (start < text.Length)
        {
            MachinaTextTokenKind kind = Classify(text[start]);
            int end = start + 1;

            while (end < text.Length && Classify(text[end]) == kind)
            {
                end++;
            }

            tokens.Add(new MachinaTokenPlacement(
                tokens.Count,
                kind,
                text[start..end],
                new MachinaTextSpan(start, end - start),
                AnchorGlyphIndex: null,
                AnchorOriginX: null,
                AnchorOriginY: null,
                AdvanceWidth: 0d,
                InkBounds: null));

            start = end;
        }

        return tokens;
    }

    private static MachinaTextTokenKind Classify(char value)
    {
        if (char.IsWhiteSpace(value))
        {
            return MachinaTextTokenKind.Whitespace;
        }

        if (char.IsLetterOrDigit(value) || value == '_')
        {
            return MachinaTextTokenKind.Word;
        }

        return MachinaTextTokenKind.Punctuation;
    }
}
