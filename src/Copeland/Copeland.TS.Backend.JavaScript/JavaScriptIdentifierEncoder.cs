namespace Copeland.TS.Backend.JavaScript;

internal static class JavaScriptIdentifierEncoder
{
    private static readonly HashSet<string> ReservedWords = new(StringComparer.Ordinal)
    {
        "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default",
        "delete", "do", "else", "enum", "export", "extends", "false", "finally", "for", "function",
        "if", "implements", "import", "in", "instanceof", "interface", "let", "new", "null", "package",
        "private", "protected", "public", "return", "super", "switch", "static", "this", "throw", "true",
        "try", "typeof", "var", "void", "while", "with", "yield"
    };

    public static string Encode(string name)
    {
        if (IsSafeIdentifier(name) && !ReservedWords.Contains(name))
        {
            return name;
        }

        var encoded = new System.Text.StringBuilder("__cope_");
        foreach (char character in name)
        {
            encoded.Append(((int)character).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
        }

        return encoded.ToString();
    }

    private static bool IsSafeIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || !(char.IsAsciiLetter(name[0]) || name[0] == '_' || name[0] == '$'))
        {
            return false;
        }

        foreach (char character in name.AsSpan(1))
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character == '_' || character == '$'))
            {
                return false;
            }
        }

        return true;
    }
}
