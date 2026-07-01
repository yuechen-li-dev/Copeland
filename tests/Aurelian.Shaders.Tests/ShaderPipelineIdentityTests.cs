using Aurelian.Shaders.Diagnostics;
using Aurelian.Shaders.Lexing;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class ShaderPipelineIdentityTests
{
    [Fact]
    public void AssemblyName_IsAurelianShaders()
    {
        var assemblyName = typeof(ShaderLexer).Assembly.GetName().Name;

        Assert.Equal("Aurelian.Shaders", assemblyName);
    }

    [Fact]
    public void Lexer_CanTokenizeTinyShaderLikeSource()
    {
        var tokens = new ShaderLexer().Lex("shader Tiny { stage vertex; }");

        Assert.Contains(tokens, token => token is { Kind: TokenKind.Keyword, Text: "shader" });
        Assert.Contains(tokens, token => token is { Kind: TokenKind.Identifier, Text: "Tiny" });
        Assert.Equal(TokenKind.EndOfFile, tokens[^1].Kind);
    }

    [Fact]
    public void DiagnosticFactory_CreatesDiagnostic()
    {
        var diagnostic = Diagnostic.Create("AURSH000", "identity smoke", 2, 3);

        Assert.Equal("AURSH000", diagnostic.Code);
        Assert.Equal("identity smoke", diagnostic.Message);
        Assert.Equal(2, diagnostic.Line);
        Assert.Equal(3, diagnostic.Column);
    }
}
