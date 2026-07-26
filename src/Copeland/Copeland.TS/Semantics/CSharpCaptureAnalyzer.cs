namespace Copeland.TS.Semantics;

/// <summary>Small lexical helper for the Copeland-owned capture boundary.</summary>
internal static class CSharpCaptureAnalyzer
{
    public static IReadOnlySet<string> FindReferencedNames(string text, IReadOnlySet<string> candidates)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (CSharpLexeme lexeme in Lex(text))
        {
            if (lexeme.IsIdentifier && candidates.Contains(lexeme.Text)) names.Add(lexeme.Text);
        }

        return names;
    }

    public static IReadOnlySet<string> FindAssignedNames(string text, IReadOnlySet<string> candidates)
    {
        CSharpLexeme[] lexemes = Lex(text).ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < lexemes.Length; index++)
        {
            if (!lexemes[index].IsIdentifier || !candidates.Contains(lexemes[index].Text)) continue;
            string next = index + 1 < lexemes.Length ? lexemes[index + 1].Text : string.Empty;
            if (next is "=" or "+=" or "-=" or "*=" or "/=" or "%=" or "++" or "--") names.Add(lexemes[index].Text);
        }

        return names;
    }

    private static IEnumerable<CSharpLexeme> Lex(string text)
    {
        for (var position = 0; position < text.Length;)
        {
            char current = text[position];
            if (char.IsWhiteSpace(current))
            {
                position++;
                continue;
            }

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
                position = SkipVerbatimString(text, position + 2) + 1;
                continue;
            }

            if (current == '"' && CountQuotes(text, position) >= 3)
            {
                position = SkipRawString(text, position, CountQuotes(text, position)) + 1;
                continue;
            }

            if (current is '\'' or '"')
            {
                position = SkipQuotedString(text, position + 1, current) + 1;
                continue;
            }
            if (char.IsLetter(current) || current == '_')
            {
                int start = position++;
                while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_')) position++;
                yield return new CSharpLexeme(text[start..position], true);
                continue;
            }

            string operation = position + 1 < text.Length && text[position + 1] == '=' && current is '=' or '+' or '-' or '*' or '/' or '%'
                ? text.Substring(position, 2)
                : current is '+' or '-' && position + 1 < text.Length && text[position + 1] == current
                    ? text.Substring(position, 2)
                    : current.ToString();
            position += operation.Length;
            yield return new CSharpLexeme(operation, false);
        }
    }

    private static int SkipLineComment(string text, int position)
    {
        while (position < text.Length && text[position] is not '\r' and not '\n') position++;
        return position;
    }

    private static int SkipBlockComment(string text, int position)
    {
        while (position + 1 < text.Length && (text[position] != '*' || text[position + 1] != '/')) position++;
        return Math.Min(text.Length, position + 2);
    }

    private static int SkipVerbatimString(string text, int position)
    {
        while (position < text.Length)
        {
            if (text[position] == '"' && (position + 1 == text.Length || text[position + 1] != '"')) return position;
            position += text[position] == '"' ? 2 : 1;
        }

        return text.Length - 1;
    }

    private static int SkipQuotedString(string text, int position, char quote)
    {
        while (position < text.Length)
        {
            if (text[position] == '\\')
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

    private readonly record struct CSharpLexeme(string Text, bool IsIdentifier);
}
