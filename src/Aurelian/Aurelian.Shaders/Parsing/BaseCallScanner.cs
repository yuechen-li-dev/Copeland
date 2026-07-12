using Aurelian.Shaders.Ast;
using Aurelian.Shaders.Lexing;

namespace Aurelian.Shaders.Parsing;

internal static class BaseCallScanner
{
    public static IReadOnlyList<BaseCall> Scan(string body, SourceSpan methodSpan)
    {
        var calls = new List<BaseCall>();
        var i = 0;
        while (i < body.Length)
        {
            if (TrySkipTrivia(body, ref i))
                continue;

            var probe = i;
            if (!TryReadKeyword(body, ref probe, "base"))
            {
                i++;
                continue;
            }

            SkipWhitespace(body, ref probe);
            if (!TryReadChar(body, ref probe, '.'))
            {
                i++;
                continue;
            }

            SkipWhitespace(body, ref probe);
            if (!TryReadIdentifier(body, ref probe, out var methodName))
            {
                i++;
                continue;
            }

            SkipWhitespace(body, ref probe);
            if (!TryReadChar(body, ref probe, '('))
            {
                i++;
                continue;
            }

            var argsStart = probe;
            if (!TryReadBalancedArguments(body, ref probe, out var argsText))
            {
                i++;
                continue;
            }

            var argCount = CountArguments(argsText);
            var span = GetRelativeSpan(body, i, probe - i, methodSpan);
            calls.Add(new BaseCall(methodName, argsText, argCount, span));
            i = probe;
        }

        return calls;
    }

    private static int CountArguments(string argumentText)
    {
        if (string.IsNullOrWhiteSpace(argumentText)) return 0;
        var depth = 0;
        var inString = false;
        var count = 1;
        for (var i = 0; i < argumentText.Length; i++)
        {
            var c = argumentText[i];
            if (inString)
            {
                if (c == '\\' && i + 1 < argumentText.Length) { i++; continue; }
                if (c == '"') inString = false;
                continue;
            }

            if (c == '"') { inString = true; continue; }
            if (c == '(' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '}') depth--;
            else if (c == ',' && depth == 0) count++;
        }

        return count;
    }

    private static bool TryReadBalancedArguments(string body, ref int i, out string argsText)
    {
        var start = i;
        var depth = 1;
        while (i < body.Length)
        {
            if (TrySkipTrivia(body, ref i))
                continue;

            var c = body[i++];
            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    argsText = body[start..(i - 1)].Trim();
                    return true;
                }
            }
        }

        argsText = string.Empty;
        return false;
    }

    private static SourceSpan GetRelativeSpan(string body, int start, int length, SourceSpan methodSpan)
    {
        var line = methodSpan.Line;
        var column = methodSpan.Column;
        for (var i = 0; i < start; i++)
        {
            if (body[i] == '\n') { line++; column = 1; }
            else column++;
        }

        return new(methodSpan.Start + start, length, line, column);
    }

    private static bool TrySkipTrivia(string s, ref int i)
    {
        if (i + 1 >= s.Length) return false;

        if (s[i] == '/' && s[i + 1] == '/')
        {
            i += 2;
            while (i < s.Length && s[i] != '\n') i++;
            return true;
        }

        if (s[i] == '/' && s[i + 1] == '*')
        {
            i += 2;
            while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
            i = Math.Min(i + 2, s.Length);
            return true;
        }

        if (s[i] == '"')
        {
            i++;
            while (i < s.Length)
            {
                if (s[i] == '\\' && i + 1 < s.Length) { i += 2; continue; }
                if (s[i] == '"') { i++; break; }
                i++;
            }
            return true;
        }

        return false;
    }

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    private static bool TryReadKeyword(string s, ref int i, string keyword)
    {
        if (i + keyword.Length > s.Length) return false;
        if (!s.AsSpan(i, keyword.Length).SequenceEqual(keyword)) return false;
        if (i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_')) return false;
        if (i + keyword.Length < s.Length && (char.IsLetterOrDigit(s[i + keyword.Length]) || s[i + keyword.Length] == '_')) return false;
        i += keyword.Length;
        return true;
    }

    private static bool TryReadIdentifier(string s, ref int i, out string identifier)
    {
        identifier = string.Empty;
        if (i >= s.Length || !(char.IsLetter(s[i]) || s[i] == '_')) return false;
        var start = i++;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
        identifier = s[start..i];
        return true;
    }

    private static bool TryReadChar(string s, ref int i, char c)
    {
        if (i >= s.Length || s[i] != c) return false;
        i++;
        return true;
    }
}
