using Aurelian.Shaders.Language.Diagnostics;
using Aurelian.Shaders.Language.Lexing;
using Aurelian.Shaders.Language.Tokens;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class SdslvLexerTests
{
    [Fact]
    public void SdslvLexer_LexesNamespaceUseRecordTokens()
    {
        var result = SdslvLexer.Lex("namespace Demo.Core; use Demo.Math; record Vertex { Position: float3; }");

        Assert.Empty(result.Diagnostics);
        Assert.Contains(result.Tokens, token => token.Kind == SdslvTokenKind.KeywordNamespace);
        Assert.Contains(result.Tokens, token => token.Kind == SdslvTokenKind.KeywordUse);
        Assert.Contains(result.Tokens, token => token.Kind == SdslvTokenKind.KeywordRecord);
        Assert.Contains(result.Tokens, token => token.Kind == SdslvTokenKind.Identifier && token.Text == "Position");
        Assert.Equal(SdslvTokenKind.EndOfFile, result.Tokens[^1].Kind);
    }

    [Fact]
    public void SdslvLexer_LexesOperatorsAndPunctuation()
    {
        var result = SdslvLexer.Lex("{ } ( ) [ ] < > <= >= == != && || -> => + - * / % ! ? , : ; . =");
        var kinds = result.Tokens.Select(token => token.Kind).ToArray();

        Assert.Empty(result.Diagnostics);
        Assert.Contains(SdslvTokenKind.LeftBrace, kinds);
        Assert.Contains(SdslvTokenKind.RightBrace, kinds);
        Assert.Contains(SdslvTokenKind.LeftAngle, kinds);
        Assert.Contains(SdslvTokenKind.RightAngle, kinds);
        Assert.Contains(SdslvTokenKind.LessEq, kinds);
        Assert.Contains(SdslvTokenKind.GreaterEq, kinds);
        Assert.Contains(SdslvTokenKind.EqEq, kinds);
        Assert.Contains(SdslvTokenKind.BangEq, kinds);
        Assert.Contains(SdslvTokenKind.AmpAmp, kinds);
        Assert.Contains(SdslvTokenKind.PipePipe, kinds);
        Assert.Contains(SdslvTokenKind.Arrow, kinds);
        Assert.Contains(SdslvTokenKind.FatArrow, kinds);
    }

    [Fact]
    public void SdslvLexer_UnknownCharacter_ProducesDiagnostic()
    {
        var result = SdslvLexer.Lex("record Bad { Field: float4; @ }");

        Assert.Contains(result.Tokens, token => token.Kind == SdslvTokenKind.Unknown && token.Text == "@");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Phase == SdslvDiagnosticPhase.Lexing && diagnostic.Code == "SDSL-L003");
    }

    [Fact]
    public void SdslvLexer_UnterminatedString_ProducesDiagnostic()
    {
        var result = SdslvLexer.Lex("\"unterminated");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Phase == SdslvDiagnosticPhase.Lexing && diagnostic.Code == "SDSL-L002");
        Assert.Contains(result.Tokens, token => token.Kind == SdslvTokenKind.StringLiteral);
    }
}
