using Copeland.TS.Diagnostics;

namespace Copeland.TS.Syntax;

public sealed class Lexer
{
    private readonly string _text;
    private readonly DiagnosticBag _diagnostics = new();
    private int _position;

    public Lexer(string text)
    {
        _text = text;
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.Diagnostics;

    public SyntaxToken NextToken()
    {
        while (true)
        {
            if (_position >= _text.Length)
            {
                return new SyntaxToken(SyntaxKind.EndOfFileToken, _position, string.Empty, null);
            }

            var start = _position;
            var current = Current;

            if (char.IsWhiteSpace(current))
            {
                SkipWhitespace();
                continue;
            }

            if (current == '/' && Peek(1) == '/')
            {
                SkipSingleLineComment();
                continue;
            }

            if (current == '/' && Peek(1) == '*')
            {
                if (!SkipMultiLineComment())
                {
                    _diagnostics.Report(
                        "COPE-LEX-0002",
                        "Unterminated multi-line comment.",
                        start,
                        Math.Max(1, _position - start));
                }

                continue;
            }

            if (IsIdentifierStart(current))
            {
                return LexIdentifierOrKeyword();
            }

            if (char.IsAsciiDigit(current))
            {
                return LexNumber();
            }

            if (current is '\'' or '"')
            {
                return LexString();
            }

            if (current == '`')
            {
                return LexTemplate();
            }

            switch (current)
            {
                case '(':
                    return FixedToken(SyntaxKind.OpenParenToken, "(");
                case ')':
                    return FixedToken(SyntaxKind.CloseParenToken, ")");
                case '{':
                    return FixedToken(SyntaxKind.OpenBraceToken, "{");
                case '}':
                    return FixedToken(SyntaxKind.CloseBraceToken, "}");
                case '[':
                    return FixedToken(SyntaxKind.OpenBracketToken, "[");
                case ']':
                    return FixedToken(SyntaxKind.CloseBracketToken, "]");
                case ',':
                    return FixedToken(SyntaxKind.CommaToken, ",");
                case '.':
                    if (Peek(1) == '.' && Peek(2) == '.')
                    {
                        return FixedToken(SyntaxKind.EllipsisToken, "...");
                    }
                    return FixedToken(SyntaxKind.DotToken, ".");
                case ':':
                    return FixedToken(SyntaxKind.ColonToken, ":");
                case ';':
                    return FixedToken(SyntaxKind.SemicolonToken, ";");
                case '?':
                    return FixedToken(SyntaxKind.QuestionToken, "?");
                case '@':
                    return FixedToken(SyntaxKind.AtToken, "@");
                case '+':
                    if (Peek(1) == '=') return FixedToken(SyntaxKind.PlusEqualsToken, "+=");
                    if (Peek(1) == '+') return FixedToken(SyntaxKind.PlusPlusToken, "++");
                    return FixedToken(SyntaxKind.PlusToken, "+");
                case '-':
                    if (Peek(1) == '=') return FixedToken(SyntaxKind.MinusEqualsToken, "-=");
                    if (Peek(1) == '-') return FixedToken(SyntaxKind.MinusMinusToken, "--");
                    if (Peek(1) == '>')
                    {
                        return FixedToken(SyntaxKind.ArrowToken, "->");
                    }
                    return FixedToken(SyntaxKind.MinusToken, "-");
                case '*':
                    if (Peek(1) == '=') return FixedToken(SyntaxKind.StarEqualsToken, "*=");
                    return FixedToken(SyntaxKind.StarToken, "*");
                case '%':
                    if (Peek(1) == '=') return FixedToken(SyntaxKind.PercentEqualsToken, "%=");
                    return FixedToken(SyntaxKind.PercentToken, "%");
                case '/':
                    if (Peek(1) == '=') return FixedToken(SyntaxKind.SlashEqualsToken, "/=");
                    return FixedToken(SyntaxKind.SlashToken, "/");
                case '<':
                    if (Peek(1) == '=') return FixedToken(SyntaxKind.LessOrEqualsToken, "<=");
                    return FixedToken(SyntaxKind.LessToken, "<");
                case '>':
                    if (Peek(1) == '=') return FixedToken(SyntaxKind.GreaterOrEqualsToken, ">=");
                    return FixedToken(SyntaxKind.GreaterToken, ">");
                case '^':
                    return FixedToken(SyntaxKind.CaretToken, "^");
                case '&':
                    if (Peek(1) == '&')
                    {
                        return FixedToken(SyntaxKind.AmpersandAmpersandToken, "&&");
                    }
                    return FixedToken(SyntaxKind.AmpersandToken, "&");
                case '|':
                    if (Peek(1) == '>')
                    {
                        return FixedToken(SyntaxKind.PipeGreaterToken, "|>");
                    }
                    if (Peek(1) == '|')
                    {
                        return FixedToken(SyntaxKind.PipePipeToken, "||");
                    }
                    return FixedToken(SyntaxKind.PipeToken, "|");
                case '=':
                    if (Peek(1) == '=' && Peek(2) == '=')
                    {
                        return FixedToken(SyntaxKind.EqualsEqualsEqualsToken, "===");
                    }

                    if (Peek(1) == '=')
                    {
                        return FixedToken(SyntaxKind.EqualsEqualsToken, "==");
                    }

                    if (Peek(1) == '>')
                    {
                        return FixedToken(SyntaxKind.ArrowToken, "=>");
                    }

                    return FixedToken(SyntaxKind.EqualsToken, "=");
                case '!':
                    if (Peek(1) == '=' && Peek(2) == '=')
                    {
                        return FixedToken(SyntaxKind.BangEqualsEqualsToken, "!==");
                    }

                    if (Peek(1) == '=')
                    {
                        return FixedToken(SyntaxKind.BangEqualsToken, "!=");
                    }

                    return FixedToken(SyntaxKind.BangToken, "!");
            }

            _position++;
            _diagnostics.Report("COPE-LEX-0003", "Invalid character.", start, 1);
            return new SyntaxToken(SyntaxKind.BadToken, start, _text.Substring(start, 1), null);
        }
    }

    private SyntaxToken LexIdentifierOrKeyword()
    {
        var start = _position;
        _position++;
        while (IsIdentifierPart(Current))
        {
            _position++;
        }

        ReadOnlyMemory<char> text = _text.AsMemory(start, _position - start);
        SyntaxKind kind = SyntaxFacts.GetKeywordKind(text.Span);
        return kind == SyntaxKind.IdentifierToken
            ? new SyntaxToken(kind, start, text, null)
            : new SyntaxToken(kind, start, SyntaxFacts.GetText(kind)!, null);
    }

    private SyntaxToken LexNumber()
    {
        var start = _position;
        if (Current == '0' && Peek(1) is 'x' or 'X')
        {
            _position += 2;
            int digitsStart = _position;
            while (char.IsAsciiLetterOrDigit(Current)) _position++;
            if (_position - digitsStart is >= 1 and <= 8
                && uint.TryParse(_text.AsSpan(digitsStart, _position - digitsStart),
                    System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out uint bits))
            {
                return NumberToken(start, unchecked((int)bits));
            }
            _diagnostics.Report("COPE-LEX-0004", "Hexadecimal int literals must contain one to eight hex digits (a signed 32-bit bit pattern).", start, _position - start);
            return NumberToken(start, null);
        }

        while (char.IsAsciiDigit(Current))
        {
            _position++;
        }

        var hasDecimalPoint = false;
        if (Current == '.' && char.IsAsciiDigit(Peek(1)))
        {
            hasDecimalPoint = true;
            _position++;
            while (char.IsAsciiDigit(Current))
            {
                _position++;
            }
        }

        // Exponent notation (`1e-3`, `2.5E+10`) is ordinary TypeScript float
        // syntax. It is consumed only when a digit follows the `e` (after an
        // optional sign), so an identifier-like suffix still reaches the unit
        // and invalid-literal handling below. An exponent always makes a float.
        bool hasExponent = false;
        if ((Current == 'e' || Current == 'E')
            && (char.IsAsciiDigit(Peek(1)) || ((Peek(1) == '+' || Peek(1) == '-') && char.IsAsciiDigit(Peek(2)))))
        {
            hasExponent = true;
            hasDecimalPoint = true;
            _position += Peek(1) == '+' || Peek(1) == '-' ? 2 : 1;
            while (char.IsAsciiDigit(Current))
            {
                _position++;
            }
        }

        ReadOnlySpan<char> numericText = _text.AsSpan(start, _position - start);
        double floatValue = default;
        int intValue = default;
        System.Globalization.NumberStyles floatStyles = hasExponent
            ? System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowExponent
            : System.Globalization.NumberStyles.AllowDecimalPoint;
        bool parsed = hasDecimalPoint
            ? double.TryParse(numericText, floatStyles, System.Globalization.CultureInfo.InvariantCulture, out floatValue)
            : int.TryParse(numericText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out intValue);

        if (IsIdentifierStart(Current))
        {
            int suffixStart = _position;
            while (IsIdentifierPart(Current))
            {
                _position++;
            }

            ReadOnlySpan<char> suffix = _text.AsSpan(suffixStart, _position - suffixStart);
            if (!hasDecimalPoint && suffix is "L")
            {
                if (long.TryParse(numericText, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out long integer))
                {
                    return NumberToken(start, integer);
                }
                if (numericText is "9223372036854775808")
                {
                    return NumberToken(start, new MinimumLongMagnitudeTokenValue());
                }
                _diagnostics.Report("COPE-NUM-0064", "Int64 literal is outside the signed 64-bit range.", start, _position - start);
                return NumberToken(start, null);
            }
            if (parsed && suffix is "px" or "ui")
            {
                double unitValue = hasDecimalPoint ? floatValue : intValue;
                return NumberToken(start, new LengthLiteralTokenValue(unitValue, suffix.ToString()));
            }

            _diagnostics.Report("COPE-LEX-0004", "Invalid number literal.", start, _position - start);
            return NumberToken(start, null);
        }

        object? value = null;
        if (parsed)
        {
            value = hasDecimalPoint ? (object)floatValue : intValue;
        }
        else if (numericText is "2147483648")
        {
            // Only unary minus may consume this magnitude as the minimum int.
            value = new MinimumIntMagnitudeTokenValue();
        }
        if (!parsed && value is null)
        {
            _diagnostics.Report("COPE-LEX-0004", "Invalid number literal.", start, numericText.Length);
        }

        return NumberToken(start, value);
    }

    private SyntaxToken NumberToken(int start, object? value)
        => new(SyntaxKind.NumberToken, start, _text.AsMemory(start, _position - start), value);

    private SyntaxToken LexTemplate()
    {
        var start = _position;
        _position++;
        var interpolationDepth = 0;
        while (_position < _text.Length)
        {
            if (Current == '\\' && _position + 1 < _text.Length)
            {
                _position += 2;
                continue;
            }

            if (Current == '$' && Peek(1) == '{')
            {
                interpolationDepth++;
                _position += 2;
                continue;
            }

            if (Current == '}' && interpolationDepth > 0)
            {
                interpolationDepth--;
                _position++;
                continue;
            }

            if (Current == '`' && interpolationDepth == 0)
            {
                _position++;
                return QuotedToken(SyntaxKind.TemplateToken, start, terminated: true);
            }

            _position++;
        }

        _diagnostics.Report("COPE-LEX-0005", "Unterminated template literal.", start, Math.Max(1, _position - start));
        return QuotedToken(SyntaxKind.TemplateToken, start, terminated: false);
    }

    private SyntaxToken LexString()
    {
        var start = _position;
        var quote = Current;
        _position++;

        while (true)
        {
            if (_position >= _text.Length || Current is '\r' or '\n')
            {
                _diagnostics.Report("COPE-LEX-0001", "Unterminated string literal.", start, Math.Max(1, _position - start));
                return QuotedToken(SyntaxKind.StringToken, start, terminated: false);
            }

            if (Current == '\\' && _position + 1 < _text.Length)
            {
                _position += 2;
                continue;
            }

            if (Current == quote)
            {
                _position++;
                return QuotedToken(SyntaxKind.StringToken, start, terminated: true);
            }

            _position++;
        }
    }

    private SyntaxToken QuotedToken(SyntaxKind kind, int start, bool terminated)
    {
        ReadOnlyMemory<char> text = _text.AsMemory(start, _position - start);
        int valueLength = text.Length - (terminated ? 2 : 1);
        return new SyntaxToken(kind, start, text, text.Span.Slice(1, valueLength).ToString());
    }

    private void SkipWhitespace()
    {
        while (char.IsWhiteSpace(Current))
        {
            _position++;
        }
    }

    private void SkipSingleLineComment()
    {
        _position += 2;
        while (_position < _text.Length && Current is not '\r' and not '\n')
        {
            _position++;
        }
    }

    private bool SkipMultiLineComment()
    {
        _position += 2;

        while (_position < _text.Length)
        {
            if (Current == '*' && Peek(1) == '/')
            {
                _position += 2;
                return true;
            }

            _position++;
        }

        return false;
    }

    private SyntaxToken FixedToken(SyntaxKind kind, string text)
    {
        int start = _position;
        _position += text.Length;
        return new SyntaxToken(kind, start, text, null);
    }

    private char Current => Peek(0);

    private char Peek(int offset)
    {
        var index = _position + offset;
        if (index >= _text.Length)
        {
            return '\0';
        }

        return _text[index];
    }

    private static bool IsIdentifierStart(char ch)
        => char.IsAsciiLetter(ch) || ch is '_' or '$';

    private static bool IsIdentifierPart(char ch)
        => IsIdentifierStart(ch) || char.IsAsciiDigit(ch);
}

/// <summary>Raw dimensional literal syntax retained for semantic profiles.</summary>
public sealed record LengthLiteralTokenValue(double Value, string Unit);

/// <summary>The magnitude of int.MinValue is legal only directly after unary minus.</summary>
public sealed record MinimumIntMagnitudeTokenValue;

/// <summary>The magnitude of long.MinValue is legal only directly after unary minus.</summary>
public sealed record MinimumLongMagnitudeTokenValue;
