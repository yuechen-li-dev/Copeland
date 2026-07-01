using System.Text;

namespace Aurelian.Shaders.Lexing;

public sealed class ShaderLexer
{
    private static readonly HashSet<string> Keywords = ["struct", "return", "void", "float", "float2", "float3", "float4", "shader", "stage", "stream", "override", "streams"];
    public IReadOnlyList<Token> Lex(string source)
    {
        var tokens = new List<Token>();
        var i = 0; var line = 1; var col = 1;
        while (i < source.Length)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c)) { Advance(c, ref i, ref line, ref col); continue; }
            var start = i; var sl = line; var sc = col;
            if (c == '#' && (i == 0 || source[i - 1] == '\n'))
            {
                while (i < source.Length && source[i] != '\n') Advance(source[i], ref i, ref line, ref col);
                tokens.Add(new(TokenKind.PreprocessorDirective, source[start..i], new(start, i - start, sl, sc))); continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) Advance(source[i], ref i, ref line, ref col);
                var text = source[start..i];
                tokens.Add(new(Keywords.Contains(text) ? TokenKind.Keyword : TokenKind.Identifier, text, new(start, i - start, sl, sc))); continue;
            }
            if (char.IsDigit(c))
            {
                while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.')) Advance(source[i], ref i, ref line, ref col);
                tokens.Add(new(TokenKind.NumericLiteral, source[start..i], new(start, i - start, sl, sc))); continue;
            }
            if (c == '"')
            {
                Advance(c, ref i, ref line, ref col);
                while (i < source.Length && source[i] != '"') Advance(source[i], ref i, ref line, ref col);
                if (i < source.Length) Advance(source[i], ref i, ref line, ref col);
                tokens.Add(new(TokenKind.StringLiteral, source[start..i], new(start, i - start, sl, sc))); continue;
            }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') Advance(source[i], ref i, ref line, ref col);
                tokens.Add(new(TokenKind.Comment, source[start..i], new(start, i - start, sl, sc))); continue;
            }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                Advance(source[i], ref i, ref line, ref col); Advance(source[i], ref i, ref line, ref col);
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) Advance(source[i], ref i, ref line, ref col);
                if (i + 1 < source.Length) { Advance(source[i], ref i, ref line, ref col); Advance(source[i], ref i, ref line, ref col); }
                tokens.Add(new(TokenKind.Comment, source[start..i], new(start, i - start, sl, sc))); continue;
            }
            Advance(c, ref i, ref line, ref col);
            tokens.Add(new(char.IsPunctuation(c) ? TokenKind.Punctuation : TokenKind.Operator, source[start..i], new(start, 1, sl, sc)));
        }
        tokens.Add(new(TokenKind.EndOfFile, string.Empty, new(source.Length, 0, line, col)));
        return tokens;
    }
    private static void Advance(char c, ref int i, ref int line, ref int col) { i++; if (c == '\n') { line++; col = 1; } else col++; }
}
