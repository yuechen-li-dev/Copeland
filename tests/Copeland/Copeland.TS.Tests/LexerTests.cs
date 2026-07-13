using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class LexerTests
{
    [Fact]
    public void Lexes_Keywords_And_Identifiers()
    {
        var kinds = LexKinds("const let function return if else while for true false null var with foo _bar $baz");

        Assert.Equal(
        [
            SyntaxKind.ConstKeyword,
            SyntaxKind.LetKeyword,
            SyntaxKind.FunctionKeyword,
            SyntaxKind.ReturnKeyword,
            SyntaxKind.IfKeyword,
            SyntaxKind.ElseKeyword,
            SyntaxKind.WhileKeyword,
            SyntaxKind.ForKeyword,
            SyntaxKind.TrueKeyword,
            SyntaxKind.FalseKeyword,
            SyntaxKind.NullKeyword,
            SyntaxKind.VarKeyword,
            SyntaxKind.WithKeyword,
            SyntaxKind.IdentifierToken,
            SyntaxKind.IdentifierToken,
            SyntaxKind.IdentifierToken,
            SyntaxKind.EndOfFileToken,
        ],
        kinds);
    }

    [Fact]
    public void Lexes_Longest_Match_Operators()
    {
        var kinds = LexKinds("=== == = !== != ! <= >= && || => < >");

        Assert.Equal(
        [
            SyntaxKind.EqualsEqualsEqualsToken,
            SyntaxKind.EqualsEqualsToken,
            SyntaxKind.EqualsToken,
            SyntaxKind.BangEqualsEqualsToken,
            SyntaxKind.BangEqualsToken,
            SyntaxKind.BangToken,
            SyntaxKind.LessOrEqualsToken,
            SyntaxKind.GreaterOrEqualsToken,
            SyntaxKind.AmpersandAmpersandToken,
            SyntaxKind.PipePipeToken,
            SyntaxKind.ArrowToken,
            SyntaxKind.LessToken,
            SyntaxKind.GreaterToken,
            SyntaxKind.EndOfFileToken,
        ],
        kinds);
    }

    [Fact]
    public void Lexes_Mixed_Sequence_With_Comments_And_Whitespace()
    {
        var tree = SyntaxTree.ParseTokens("let x = 12; // comment\n/*a*/ return 'ok';");

        Assert.Equal(
        [
            SyntaxKind.LetKeyword,
            SyntaxKind.IdentifierToken,
            SyntaxKind.EqualsToken,
            SyntaxKind.NumberToken,
            SyntaxKind.SemicolonToken,
            SyntaxKind.ReturnKeyword,
            SyntaxKind.StringToken,
            SyntaxKind.SemicolonToken,
            SyntaxKind.EndOfFileToken,
        ],
        tree.Tokens.Select(t => t.Kind).ToArray());

        Assert.Empty(tree.Diagnostics);
    }

    [Fact]
    public void Lexes_Number_And_String_Values()
    {
        var tree = SyntaxTree.ParseTokens("42 \"abc\" 'def'");

        Assert.Equal(42, Assert.IsType<int>(tree.Tokens[0].Value));
        Assert.Equal("abc", Assert.IsType<string>(tree.Tokens[1].Value));
        Assert.Equal("def", Assert.IsType<string>(tree.Tokens[2].Value));
    }

    [Fact]
    public void Reports_Unterminated_String()
    {
        var tree = SyntaxTree.ParseTokens("\"oops");

        Assert.Single(tree.Diagnostics);
        Assert.Equal("COPE-LEX-0001", tree.Diagnostics[0].Id);
    }

    [Fact]
    public void Reports_Unterminated_MultiLine_Comment()
    {
        var tree = SyntaxTree.ParseTokens("/* no close");

        Assert.Single(tree.Diagnostics);
        Assert.Equal("COPE-LEX-0002", tree.Diagnostics[0].Id);
    }

    [Fact]
    public void Reports_Invalid_Character()
    {
        var tree = SyntaxTree.ParseTokens("@");

        Assert.Single(tree.Diagnostics);
        Assert.Equal("COPE-LEX-0003", tree.Diagnostics[0].Id);
        Assert.Equal(SyntaxKind.BadToken, tree.Tokens[0].Kind);
    }

    private static SyntaxKind[] LexKinds(string text)
        => SyntaxTree.ParseTokens(text).Tokens.Select(t => t.Kind).ToArray();
}
