namespace Copeland.Markdown;

public static class MarkdownLexer
{
    public static MarkdownTokenizedSource Tokenize(string text)
    {
        return Tokenize(new MarkdownSourceText(text));
    }

    public static MarkdownTokenizedSource Tokenize(MarkdownSourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<MarkdownLine> lines = [];
        List<MarkdownToken> tokens = [];
        string text = source.Text;

        int lineNumber = 1;
        int index = 0;

        while (index <= text.Length)
        {
            int contentStart = index;
            while (index < text.Length && text[index] != '\r' && text[index] != '\n')
            {
                index += 1;
            }

            int contentLength = index - contentStart;
            string lineText = text.Substring(contentStart, contentLength);
            SourceSpan lineSpan = source.CreateSpan(contentStart, contentLength);
            IReadOnlyList<MarkdownToken> lineTokens = TokenizeLine(source, lineText, contentStart);
            lines.Add(new MarkdownLine(lineNumber, lineText, lineSpan, lineTokens));
            tokens.AddRange(lineTokens);

            if (index >= text.Length)
            {
                break;
            }

            int newlineStart = index;
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                index += 2;
            }
            else
            {
                index += 1;
            }

            int newlineLength = index - newlineStart;
            tokens.Add(new MarkdownToken(
                MarkdownTokenKind.NewLine,
                text.Substring(newlineStart, newlineLength),
                source.CreateSpan(newlineStart, newlineLength)));

            lineNumber += 1;
        }

        tokens.Add(new MarkdownToken(
            MarkdownTokenKind.EndOfFile,
            string.Empty,
            source.CreateSpan(text.Length, 0)));

        return new MarkdownTokenizedSource(source, lines, tokens);
    }

    private static IReadOnlyList<MarkdownToken> TokenizeLine(MarkdownSourceText source, string lineText, int absoluteStart)
    {
        List<MarkdownToken> tokens = [];
        int cursor = 0;

        while (cursor < lineText.Length)
        {
            char current = lineText[cursor];
            MarkdownTokenKind? singleCharKind = ClassifySingleCharToken(current);

            if (singleCharKind is not null)
            {
                tokens.Add(new MarkdownToken(
                    singleCharKind.Value,
                    current.ToString(),
                    source.CreateSpan(absoluteStart + cursor, 1)));
                cursor += 1;
                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                int start = cursor;
                while (cursor < lineText.Length && char.IsWhiteSpace(lineText[cursor]))
                {
                    cursor += 1;
                }

                tokens.Add(new MarkdownToken(
                    MarkdownTokenKind.WhiteSpace,
                    lineText[start..cursor],
                    source.CreateSpan(absoluteStart + start, cursor - start)));
                continue;
            }

            int textStart = cursor;
            while (cursor < lineText.Length &&
                   !char.IsWhiteSpace(lineText[cursor]) &&
                   ClassifySingleCharToken(lineText[cursor]) is null)
            {
                cursor += 1;
            }

            tokens.Add(new MarkdownToken(
                MarkdownTokenKind.Text,
                lineText[textStart..cursor],
                source.CreateSpan(absoluteStart + textStart, cursor - textStart)));
        }

        return tokens;
    }

    private static MarkdownTokenKind? ClassifySingleCharToken(char character)
    {
        return character switch
        {
            '#' => MarkdownTokenKind.Hash,
            '*' => MarkdownTokenKind.Star,
            '-' => MarkdownTokenKind.Dash,
            '`' => MarkdownTokenKind.Backtick,
            '[' => MarkdownTokenKind.OpenBracket,
            ']' => MarkdownTokenKind.CloseBracket,
            '(' => MarkdownTokenKind.OpenParen,
            ')' => MarkdownTokenKind.CloseParen,
            '.' => MarkdownTokenKind.Dot,
            '>' => MarkdownTokenKind.GreaterThan,
            '!' => MarkdownTokenKind.Exclamation,
            _ => null,
        };
    }
}
