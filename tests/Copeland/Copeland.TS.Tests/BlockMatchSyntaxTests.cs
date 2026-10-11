using Copeland.TS.Compiler;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class BlockMatchSyntaxTests
{
    [Fact]
    public void Standalone_expression_match_can_return_record_literals()
    {
        const string source = """
            enum Phase { Start, Stop }
            function Main(): void {
                match Phase.Start {
                    case Phase.Start => { value: 1 },
                    case Phase.Stop => { value: 2 },
                };
            }
            """;
        var syntax = SyntaxTree.Parse(source);
        Assert.Empty(syntax.Diagnostics);
        var function = Assert.Single(syntax.Root.Members.OfType<FunctionDeclarationSyntax>());
        var statement = Assert.IsType<ExpressionStatementSyntax>(Assert.Single(function.Body!.Statements));
        Assert.IsType<MatchExpressionSyntax>(statement.Expression);
    }

    [Fact]
    public void Block_match_lookahead_handles_record_arguments_in_the_subject()
    {
        const string source = """
            function Main(): void {
                match Select({ value: 1 }) {
                    case Phase.Start => { return; }
                    case Phase.Stop => { return; }
                }
            }
            """;
        var syntax = SyntaxTree.Parse(source);
        Assert.Empty(syntax.Diagnostics);
        var function = Assert.Single(syntax.Root.Members.OfType<FunctionDeclarationSyntax>());
        Assert.IsType<RuntimeMatchStatementSyntax>(Assert.Single(function.Body!.Statements));
    }

    [Fact]
    public void Host_profile_reports_the_compute_only_statement_match_boundary()
    {
        const string source = """
            enum Phase { Start, Stop }
            function Main(): void {
                match Phase.Start {
                    case Phase.Start => { return; }
                    case Phase.Stop => { return; }
                }
            }
            """;
        var compilation = CopelandCompiler.CompileToMir(source);
        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, item => item.Id == "COPE-MATCH-0008");
    }
}
