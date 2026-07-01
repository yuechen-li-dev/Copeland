using Aurelian.Shaders.Language.Ast;
using Aurelian.Shaders.Language.Parsing;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class SdslvParserM1StatementTests
{
    [Fact]
    public void SdslvParser_ParseIfElseStatement_ProducesIfStatement()
    {
        var body = ParseBody("""
            if value > 0 {
                return value;
            } else {
                return 0;
            }
            """);

        var statement = Assert.IsType<SdslvIfStatement>(Assert.Single(body.Statements));
        Assert.IsType<SdslvBinaryExpression>(statement.Condition);
        Assert.Single(statement.ThenBody);
        Assert.Single(statement.ElseBody!);
    }

    [Fact]
    public void SdslvParser_ParseForStatement_ProducesForStatement()
    {
        var body = ParseBody("""
            for i in 0..4 step 1 {
                value = value + i;
            }
            return value;
            """);

        var statement = Assert.IsType<SdslvForStatement>(body.Statements[0]);
        Assert.Equal("i", statement.Iterator);
        Assert.NotNull(statement.Step);
        Assert.Single(statement.Body);
        Assert.IsType<SdslvReturnStatement>(body.Statements[1]);
    }

    [Fact]
    public void SdslvParser_ParseLetAssignExpressionAndEmptyStatements_ProducesStatementShapes()
    {
        var body = ParseBody("""
            ;
            let local: i32 = 1;
            value = local;
            value;
            """);

        Assert.IsType<SdslvEmptyStatement>(body.Statements[0]);
        Assert.IsType<SdslvLetStatement>(body.Statements[1]);
        Assert.IsType<SdslvAssignStatement>(body.Statements[2]);
        Assert.IsType<SdslvExpressionStatement>(body.Statements[3]);
    }

    private static SdslvBody ParseBody(string statements)
    {
        var result = SdslvParser.ParseModule($$"""
            shader Basic {
                fn Compute(value: i32) -> i32 {
                    {{statements}}
                }
            }
            """);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var shader = Assert.IsType<SdslvShaderDecl>(Assert.Single(result.Module!.Declarations));
        return Assert.Single(shader.Methods).Body!;
    }
}
