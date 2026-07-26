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
                    return SingleCharToken(SyntaxKind.OpenParenToken);
                case ')':
                    return SingleCharToken(SyntaxKind.CloseParenToken);
                case '{':
                    return SingleCharToken(SyntaxKind.OpenBraceToken);
                case '}':
                    return SingleCharToken(SyntaxKind.CloseBraceToken);
                case '[':
                    return SingleCharToken(SyntaxKind.OpenBracketToken);
                case ']':
                    return SingleCharToken(SyntaxKind.CloseBracketToken);
                case ',':
                    return SingleCharToken(SyntaxKind.CommaToken);
                case '.':
                    return SingleCharToken(SyntaxKind.DotToken);
                case ':':
                    return SingleCharToken(SyntaxKind.ColonToken);
                case ';':
                    return SingleCharToken(SyntaxKind.SemicolonToken);
                case '?':
                    return SingleCharToken(SyntaxKind.QuestionToken);
                case '+':
                    return SingleCharToken(SyntaxKind.PlusToken);
                case '-':
                    if (Peek(1) == '>')
                    {
                        return DoubleCharToken(SyntaxKind.ArrowToken);
                    }
                    return SingleCharToken(SyntaxKind.MinusToken);
                case '*':
                    return SingleCharToken(SyntaxKind.StarToken);
                case '%':
                    return SingleCharToken(SyntaxKind.PercentToken);
                case '/':
                    return SingleCharToken(SyntaxKind.SlashToken);
                case '<':
                    return MatchOrSingle('=', SyntaxKind.LessOrEqualsToken, SyntaxKind.LessToken);
                case '>':
                    return MatchOrSingle('=', SyntaxKind.GreaterOrEqualsToken, SyntaxKind.GreaterToken);
                case '&':
                    if (Peek(1) == '&')
                    {
                        return DoubleCharToken(SyntaxKind.AmpersandAmpersandToken);
                    }
                    return SingleCharToken(SyntaxKind.AmpersandToken);
                case '|':
                    if (Peek(1) == '>')
                    {
                        return DoubleCharToken(SyntaxKind.PipeGreaterToken);
                    }
                    if (Peek(1) == '|')
                    {
                        return DoubleCharToken(SyntaxKind.PipePipeToken);
                    }
                    return SingleCharToken(SyntaxKind.PipeToken);
                case '=':
                    if (Peek(1) == '=' && Peek(2) == '=')
                    {
                        return TripleCharToken(SyntaxKind.EqualsEqualsEqualsToken);
                    }

                    if (Peek(1) == '=')
                    {
                        return DoubleCharToken(SyntaxKind.EqualsEqualsToken);
                    }

                    if (Peek(1) == '>')
                    {
                        return DoubleCharToken(SyntaxKind.ArrowToken);
                    }

                    return SingleCharToken(SyntaxKind.EqualsToken);
                case '!':
                    if (Peek(1) == '=' && Peek(2) == '=')
                    {
                        return TripleCharToken(SyntaxKind.BangEqualsEqualsToken);
                    }

                    if (Peek(1) == '=')
                    {
                        return DoubleCharToken(SyntaxKind.BangEqualsToken);
                    }

                    return SingleCharToken(SyntaxKind.BangToken);
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

        var text = _text.Substring(start, _position - start);
        var kind = SyntaxFacts.GetKeywordKind(text);
        return new SyntaxToken(kind, start, text, null);
    }

    private SyntaxToken LexNumber()
    {
        var start = _position;
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

        var invalid = false;
        if (IsIdentifierStart(Current))
        {
            invalid = true;
            while (IsIdentifierPart(Current))
            {
                _position++;
            }
        }

        var text = _text.Substring(start, _position - start);
        if (invalid)
        {
            _diagnostics.Report("COPE-LEX-0004", "Invalid number literal.", start, text.Length);
            return new SyntaxToken(SyntaxKind.NumberToken, start, text, null);
        }

        double floatValue = default;
        int intValue = default;
        var parsed = hasDecimalPoint
            ? double.TryParse(text, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out floatValue)
            : int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out intValue);
        object? value = !parsed
            ? null
            : hasDecimalPoint
                ? (object)floatValue
                : intValue;
        if (!parsed)
        {
            _diagnostics.Report("COPE-LEX-0004", "Invalid number literal.", start, text.Length);
        }

        return new SyntaxToken(SyntaxKind.NumberToken, start, text, value);
    }

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
                var text = _text.Substring(start, _position - start);
                return new SyntaxToken(SyntaxKind.TemplateToken, start, text, text[1..^1]);
            }

            _position++;
        }

        var unterminated = _text.Substring(start, _position - start);
        _diagnostics.Report("COPE-LEX-0005", "Unterminated template literal.", start, Math.Max(1, unterminated.Length));
        return new SyntaxToken(SyntaxKind.TemplateToken, start, unterminated, unterminated.Length > 1 ? unterminated[1..] : string.Empty);
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
                var text = _text.Substring(start, _position - start);
                _diagnostics.Report("COPE-LEX-0001", "Unterminated string literal.", start, Math.Max(1, text.Length));
                return new SyntaxToken(SyntaxKind.StringToken, start, text, text.Length > 1 ? text[1..] : string.Empty);
            }

            if (Current == '\\' && _position + 1 < _text.Length)
            {
                _position += 2;
                continue;
            }

            if (Current == quote)
            {
                _position++;
                var text = _text.Substring(start, _position - start);
                var value = text[1..^1];
                return new SyntaxToken(SyntaxKind.StringToken, start, text, value);
            }

            _position++;
        }
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

    private SyntaxToken MatchOrSingle(char expected, SyntaxKind matchKind, SyntaxKind singleKind)
    {
        if (Peek(1) == expected)
        {
            return DoubleCharToken(matchKind);
        }

        return SingleCharToken(singleKind);
    }

    private SyntaxToken SingleCharToken(SyntaxKind kind)
    {
        var start = _position;
        _position++;
        return new SyntaxToken(kind, start, _text.Substring(start, 1), null);
    }

    private SyntaxToken DoubleCharToken(SyntaxKind kind)
    {
        var start = _position;
        _position += 2;
        return new SyntaxToken(kind, start, _text.Substring(start, 2), null);
    }

    private SyntaxToken TripleCharToken(SyntaxKind kind)
    {
        var start = _position;
        _position += 3;
        return new SyntaxToken(kind, start, _text.Substring(start, 3), null);
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
