namespace Copeland.TS.Syntax;

/// <summary>
/// Locates the closing delimiter of an inline C# block without attempting to
/// parse C# grammar. Roslyn remains responsible for C# syntax and semantics.
/// This scanner only recognises lexical regions where braces are inert.
/// </summary>
internal static class CSharpBlockScanner
{
    public static bool TryFindClosingBrace(string text, int openBracePosition, out int closeBracePosition)
    {
        var depth = 0;
        for (var position = openBracePosition; position < text.Length; position++)
        {
            char current = text[position];
            if (current == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                position = SkipLineComment(text, position + 2);
                continue;
            }

            if (current == '/' && position + 1 < text.Length && text[position + 1] == '*')
            {
                position = SkipBlockComment(text, position + 2);
                continue;
            }

            if (current == '@' && position + 1 < text.Length && text[position + 1] == '"')
            {
                position = SkipVerbatimString(text, position + 2);
                continue;
            }

            if (current == '"')
            {
                int quoteCount = CountQuotes(text, position);
                if (quoteCount >= 3)
                {
                    position = SkipRawString(text, position, quoteCount);
                    continue;
                }

                position = SkipQuotedString(text, position + 1, '"');
                continue;
            }

            if (current == '\'')
            {
                position = SkipQuotedString(text, position + 1, '\'');
                continue;
            }

            if (current == '{')
            {
                depth++;
                continue;
            }

            if (current == '}' && --depth == 0)
            {
                closeBracePosition = position;
                return true;
            }
        }

        closeBracePosition = text.Length;
        return false;
    }

    private static int SkipLineComment(string text, int position)
    {
        while (position < text.Length && text[position] is not '\r' and not '\n') position++;
        return position;
    }

    private static int SkipBlockComment(string text, int position)
    {
        while (position + 1 < text.Length)
        {
            if (text[position] == '*' && text[position + 1] == '/') return position + 1;
            position++;
        }

        return text.Length - 1;
    }

    private static int SkipVerbatimString(string text, int position)
    {
        while (position < text.Length)
        {
            if (text[position] != '"')
            {
                position++;
                continue;
            }

            if (position + 1 < text.Length && text[position + 1] == '"')
            {
                position += 2;
                continue;
            }

            return position;
        }

        return text.Length - 1;
    }

    private static int SkipQuotedString(string text, int position, char quote)
    {
        while (position < text.Length)
        {
            if (text[position] == '\\' && position + 1 < text.Length)
            {
                position += 2;
                continue;
            }

            if (text[position] == quote) return position;
            position++;
        }

        return text.Length - 1;
    }

    private static int SkipRawString(string text, int position, int quoteCount)
    {
        position += quoteCount;
        while (position < text.Length)
        {
            if (text[position] == '"' && CountQuotes(text, position) >= quoteCount)
            {
                return position + quoteCount - 1;
            }

            position++;
        }

        return text.Length - 1;
    }

    private static int CountQuotes(string text, int position)
    {
        var count = 0;
        while (position + count < text.Length && text[position + count] == '"') count++;
        return count;
    }
}
