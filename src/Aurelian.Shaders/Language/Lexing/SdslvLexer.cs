using Aurelian.Shaders.Language.Ast;
using Aurelian.Shaders.Language.Diagnostics;
using Aurelian.Shaders.Language.Tokens;

namespace Aurelian.Shaders.Language.Lexing;

public static class SdslvLexer
{
    private static readonly IReadOnlyDictionary<string, SdslvTokenKind> Keywords = new Dictionary<string, SdslvTokenKind>(StringComparer.Ordinal)
    {
        ["namespace"] = SdslvTokenKind.KeywordNamespace,
        ["use"] = SdslvTokenKind.KeywordUse,
        ["type"] = SdslvTokenKind.KeywordType,
        ["record"] = SdslvTokenKind.KeywordRecord,
        ["stream"] = SdslvTokenKind.KeywordStream,
        ["enum"] = SdslvTokenKind.KeywordEnum,
        ["interface"] = SdslvTokenKind.KeywordInterface,
        ["shader"] = SdslvTokenKind.KeywordShader,
        ["material"] = SdslvTokenKind.KeywordMaterial,
        ["stage"] = SdslvTokenKind.KeywordStage,
        ["fn"] = SdslvTokenKind.KeywordFn,
        ["implements"] = SdslvTokenKind.KeywordImplements,
        ["where"] = SdslvTokenKind.KeywordWhere,
        ["override"] = SdslvTokenKind.KeywordOverride,
        ["compile"] = SdslvTokenKind.KeywordCompile,
        ["flow"] = SdslvTokenKind.KeywordFlow,
        ["board"] = SdslvTokenKind.KeywordBoard,
        ["state"] = SdslvTokenKind.KeywordState,
        ["when"] = SdslvTokenKind.KeywordWhen,
        ["case"] = SdslvTokenKind.KeywordCase,
        ["goto"] = SdslvTokenKind.KeywordGoto,
        ["return"] = SdslvTokenKind.KeywordReturn,
        ["let"] = SdslvTokenKind.KeywordLet,
        ["if"] = SdslvTokenKind.KeywordIf,
        ["else"] = SdslvTokenKind.KeywordElse,
        ["for"] = SdslvTokenKind.KeywordFor,
        ["in"] = SdslvTokenKind.KeywordIn,
        ["switch"] = SdslvTokenKind.KeywordSwitch,
        ["match"] = SdslvTokenKind.KeywordMatch,
        ["utility"] = SdslvTokenKind.KeywordUtility,
        ["score"] = SdslvTokenKind.KeywordScore,
        ["with"] = SdslvTokenKind.KeywordWith,
        ["step"] = SdslvTokenKind.KeywordStep,
        ["ok"] = SdslvTokenKind.KeywordOk,
        ["err"] = SdslvTokenKind.KeywordErr,
        ["try"] = SdslvTokenKind.KeywordTry,
        ["unwrap"] = SdslvTokenKind.KeywordUnwrap,
        ["array"] = SdslvTokenKind.KeywordArray,
        ["true"] = SdslvTokenKind.KeywordTrue,
        ["false"] = SdslvTokenKind.KeywordFalse,
    };

    public static SdslvLexResult Lex(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var lexer = new Lexer(source);
        return lexer.Lex();
    }

    private sealed class Lexer(string source)
    {
        private readonly List<SdslvToken> _tokens = [];
        private readonly List<SdslvDiagnostic> _diagnostics = [];
        private int _index;
        private int _line = 1;
        private int _column = 1;

        public SdslvLexResult Lex()
        {
            while (!IsAtEnd)
            {
                var c = Current;
                if (char.IsWhiteSpace(c))
                {
                    Advance();
                    continue;
                }

                if (c == '/' && Peek(1) == '/')
                {
                    SkipLineComment();
                    continue;
                }

                if (c == '/' && Peek(1) == '*')
                {
                    SkipBlockComment();
                    continue;
                }

                var start = Mark();
                if (char.IsLetter(c) || c == '_')
                {
                    LexIdentifierOrKeyword(start);
                    continue;
                }

                if (char.IsDigit(c))
                {
                    LexNumber(start);
                    continue;
                }

                if (c == '"')
                {
                    LexString(start);
                    continue;
                }

                LexPunctuationOrOperator(start);
            }

            _tokens.Add(new SdslvToken(SdslvTokenKind.EndOfFile, string.Empty, new SdslvSpan(_index, _index, _line, _column)));
            return new SdslvLexResult(_tokens, _diagnostics);
        }

        private bool IsAtEnd => _index >= source.Length;
        private char Current => source[_index];
        private char? Peek(int offset) => _index + offset < source.Length ? source[_index + offset] : null;
        private SdslvSpan Mark() => new(_index, _index, _line, _column);
        private SdslvSpan SpanFrom(SdslvSpan start) => start with { End = _index };

        private char Advance()
        {
            var c = source[_index++];
            if (c == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }

            return c;
        }

        private void Add(SdslvTokenKind kind, string text, SdslvSpan start) => _tokens.Add(new SdslvToken(kind, text, SpanFrom(start)));

        private void AddDiagnostic(string code, string message, SdslvSpan span) =>
            _diagnostics.Add(new SdslvDiagnostic(code, SdslvDiagnosticSeverity.Error, SdslvDiagnosticPhase.Lexing, message, span));

        private void SkipLineComment()
        {
            while (!IsAtEnd && Current != '\n')
            {
                Advance();
            }
        }

        private void SkipBlockComment()
        {
            var start = Mark();
            Advance();
            Advance();
            while (!IsAtEnd)
            {
                if (Current == '*' && Peek(1) == '/')
                {
                    Advance();
                    Advance();
                    return;
                }

                Advance();
            }

            AddDiagnostic("SDSL-L001", "Unterminated block comment.", SpanFrom(start));
        }

        private void LexIdentifierOrKeyword(SdslvSpan start)
        {
            while (!IsAtEnd && (char.IsLetterOrDigit(Current) || Current == '_'))
            {
                Advance();
            }

            var text = source[start.Start.._index];
            if (text is "true" or "false")
            {
                Add(SdslvTokenKind.BoolLiteral, text, start);
                return;
            }

            Add(Keywords.TryGetValue(text, out var keyword) ? keyword : SdslvTokenKind.Identifier, text, start);
        }

        private void LexNumber(SdslvSpan start)
        {
            while (!IsAtEnd && char.IsDigit(Current))
            {
                Advance();
            }

            var kind = SdslvTokenKind.IntegerLiteral;
            if (!IsAtEnd && Current == '.' && Peek(1) is char next && char.IsDigit(next))
            {
                kind = SdslvTokenKind.FloatLiteral;
                Advance();
                while (!IsAtEnd && char.IsDigit(Current))
                {
                    Advance();
                }
            }

            Add(kind, source[start.Start.._index], start);
        }

        private void LexString(SdslvSpan start)
        {
            Advance();
            var valueStart = _index;
            while (!IsAtEnd && Current != '"')
            {
                if (Current == '\n')
                {
                    AddDiagnostic("SDSL-L002", "Unterminated string literal.", SpanFrom(start));
                    Add(SdslvTokenKind.StringLiteral, source[valueStart.._index], start);
                    return;
                }

                if (Current == '\\' && Peek(1) is not null)
                {
                    Advance();
                }

                Advance();
            }

            if (IsAtEnd)
            {
                AddDiagnostic("SDSL-L002", "Unterminated string literal.", SpanFrom(start));
                Add(SdslvTokenKind.StringLiteral, source[valueStart.._index], start);
                return;
            }

            var value = source[valueStart.._index];
            Advance();
            Add(SdslvTokenKind.StringLiteral, value, start);
        }

        private void LexPunctuationOrOperator(SdslvSpan start)
        {
            var c = Advance();
            switch (c)
            {
                case '{': Add(SdslvTokenKind.LeftBrace, "{", start); break;
                case '}': Add(SdslvTokenKind.RightBrace, "}", start); break;
                case '(': Add(SdslvTokenKind.LeftParen, "(", start); break;
                case ')': Add(SdslvTokenKind.RightParen, ")", start); break;
                case '[': Add(SdslvTokenKind.LeftBracket, "[", start); break;
                case ']': Add(SdslvTokenKind.RightBracket, "]", start); break;
                case '<': Add(Match('=') ? SdslvTokenKind.LessEq : SdslvTokenKind.LeftAngle, source[start.Start.._index], start); break;
                case '>': Add(Match('=') ? SdslvTokenKind.GreaterEq : SdslvTokenKind.RightAngle, source[start.Start.._index], start); break;
                case ',': Add(SdslvTokenKind.Comma, ",", start); break;
                case ':': Add(SdslvTokenKind.Colon, ":", start); break;
                case ';': Add(SdslvTokenKind.Semicolon, ";", start); break;
                case '.': Add(Match('.') ? SdslvTokenKind.Range : SdslvTokenKind.Dot, source[start.Start.._index], start); break;
                case '=': Add(Match('>') ? SdslvTokenKind.FatArrow : Match('=') ? SdslvTokenKind.EqEq : SdslvTokenKind.Equals, source[start.Start.._index], start); break;
                case '-': Add(Match('>') ? SdslvTokenKind.Arrow : SdslvTokenKind.Minus, source[start.Start.._index], start); break;
                case '+': Add(SdslvTokenKind.Plus, "+", start); break;
                case '*': Add(SdslvTokenKind.Star, "*", start); break;
                case '/': Add(SdslvTokenKind.Slash, "/", start); break;
                case '%': Add(SdslvTokenKind.Percent, "%", start); break;
                case '!': Add(Match('=') ? SdslvTokenKind.BangEq : SdslvTokenKind.Bang, source[start.Start.._index], start); break;
                case '&' when Match('&'): Add(SdslvTokenKind.AmpAmp, "&&", start); break;
                case '|' when Match('|'): Add(SdslvTokenKind.PipePipe, "||", start); break;
                case '?': Add(SdslvTokenKind.Question, "?", start); break;
                default:
                    Add(SdslvTokenKind.Unknown, c.ToString(), start);
                    AddDiagnostic("SDSL-L003", $"Unknown character '{c}'.", SpanFrom(start));
                    break;
            }
        }

        private bool Match(char expected)
        {
            if (IsAtEnd || Current != expected)
            {
                return false;
            }

            Advance();
            return true;
        }
    }
}
