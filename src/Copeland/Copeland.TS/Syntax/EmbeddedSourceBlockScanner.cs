namespace Copeland.TS.Syntax;

/// <summary>Delimiter scanner for bounded embedded source. It is lexical only.</summary>
internal static class EmbeddedSourceBlockScanner
{
    /// <summary>
    /// Hides typed source bodies from outer-module token scans. Their content
    /// belongs to the selected artifact language, not to the template module.
    /// </summary>
    public static string MaskBodies(string text)
    {
        var masked = text.ToCharArray();
        for (var position = 0; position < text.Length; position++)
        {
            if (!IsCodeWordAt(text, position)) continue;

            int cursor = position + 4;
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            if (cursor >= text.Length || text[cursor] != '{'
                || !TryFindClosingBrace(text, cursor, out int closeBrace)) continue;

            for (int index = position; index <= closeBrace; index++)
            {
                if (masked[index] is not '\r' and not '\n') masked[index] = ' ';
            }
            position = closeBrace;
        }

        return new string(masked);
    }

    public static bool TryFindClosingBrace(string text, int openBracePosition, out int closeBracePosition)
    {
        var depth = 0;
        for (var position = openBracePosition; position < text.Length; position++)
        {
            char current = text[position];
            if (current == '/' && position + 1 < text.Length && text[position + 1] == '/') { position = SkipLine(text, position + 2); continue; }
            if (current == '/' && position + 1 < text.Length && text[position + 1] == '*') { position = SkipBlock(text, position + 2); continue; }
            if (current is '\'' or '"') { position = SkipQuoted(text, position + 1, current); continue; }
            if (current == '`') { position = SkipTemplate(text, position + 1); continue; }
            if (current == '{') { depth++; continue; }
            if (current == '}' && --depth == 0) { closeBracePosition = position; return true; }
        }

        closeBracePosition = text.Length;
        return false;
    }

    private static int SkipLine(string text, int position) { while (position < text.Length && text[position] is not '\r' and not '\n') position++; return position; }
    private static int SkipBlock(string text, int position) { while (position + 1 < text.Length) { if (text[position] == '*' && text[position + 1] == '/') return position + 1; position++; } return text.Length - 1; }
    private static int SkipQuoted(string text, int position, char quote) { while (position < text.Length) { if (text[position] == '\\' && position + 1 < text.Length) { position += 2; continue; } if (text[position] == quote) return position; position++; } return text.Length - 1; }
    private static int SkipTemplate(string text, int position) => SkipQuoted(text, position, '`');

    private static bool IsCodeWordAt(string text, int position)
        => position + 4 <= text.Length
            && string.CompareOrdinal(text, position, "code", 0, 4) == 0
            && (position == 0 || !IsIdentifierPart(text[position - 1]))
            && (position + 4 == text.Length || !IsIdentifierPart(text[position + 4]));

    private static bool IsIdentifierPart(char value)
        => char.IsLetterOrDigit(value) || value is '_' or '$';
}
