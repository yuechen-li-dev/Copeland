using Aurelian.Shaders.Language.Ast;
using Aurelian.Shaders.Language.Diagnostics;
using Aurelian.Shaders.Language.Parsing;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class SdslvParserM1ExpressionTests
{
    [Fact]
    public void SdslvParser_ParseBinaryExpression_RespectsPrecedence()
    {
        var statement = ParseSingleReturn("return a + b * c;");
        var add = Assert.IsType<SdslvBinaryExpression>(statement.Value);
        Assert.Equal(SdslvBinaryOperator.Add, add.Operator);
        var multiply = Assert.IsType<SdslvBinaryExpression>(add.Right);
        Assert.Equal(SdslvBinaryOperator.Multiply, multiply.Operator);
    }

    [Fact]
    public void SdslvParser_ParseLogicalExpression_ProducesNestedBinaryShape()
    {
        var statement = ParseSingleReturn("return a || b && c;");
        var or = Assert.IsType<SdslvBinaryExpression>(statement.Value);
        Assert.Equal(SdslvBinaryOperator.LogicalOr, or.Operator);
        var and = Assert.IsType<SdslvBinaryExpression>(or.Right);
        Assert.Equal(SdslvBinaryOperator.LogicalAnd, and.Operator);
    }

    [Fact]
    public void SdslvParser_ParseUnaryExpression_ProducesUnaryShape()
    {
        var statement = ParseSingleReturn("return !ready;");
        var unary = Assert.IsType<SdslvUnaryExpression>(statement.Value);
        Assert.Equal(SdslvUnaryOperator.Not, unary.Operator);
    }

    [Fact]
    public void SdslvParser_ParseFieldCallAndIndexChain_ProducesPostfixShape()
    {
        var statement = ParseSingleReturn("return foo.bar(1, 2).baz[index];");
        var index = Assert.IsType<SdslvIndexExpression>(statement.Value);
        var field = Assert.IsType<SdslvFieldAccessExpression>(index.Base);
        Assert.Equal("baz", field.Field);
        var call = Assert.IsType<SdslvCallExpression>(field.Base);
        Assert.Equal(2, call.Arguments.Count);
        var bar = Assert.IsType<SdslvFieldAccessExpression>(call.Callee);
        Assert.Equal("bar", bar.Field);
    }

    [Fact]
    public void SdslvParser_ParseArrayLiteral_ProducesArrayExpression()
    {
        var statement = ParseSingleReturn("return [1, 2, 3];");
        var array = Assert.IsType<SdslvArrayLiteralExpression>(statement.Value);
        Assert.Equal(3, array.Elements.Count);

        var empty = ParseSingleReturn("return [];");
        Assert.Empty(Assert.IsType<SdslvArrayLiteralExpression>(empty.Value).Elements);
    }

    [Fact]
    public void SdslvParser_ParseWithExpression_ProducesWithUpdates()
    {
        var statement = ParseSingleReturn("return baseValue with { Field = value; Other = 2; };");
        var with = Assert.IsType<SdslvWithExpression>(statement.Value);
        Assert.IsType<SdslvIdentifierExpression>(with.Base);
        Assert.Equal(["Field", "Other"], with.Updates.Select(update => update.Field).ToArray());
    }

    [Fact]
    public void SdslvParser_ParseSwitchExpression_ProducesSwitchCases()
    {
        var statement = ParseSingleReturn("return switch x { case x > 0 => 1; else => 0; };");
        var switchExpression = Assert.IsType<SdslvSwitchExpression>(statement.Value);
        Assert.NotNull(switchExpression.Subject);
        Assert.Single(switchExpression.Cases);
        Assert.IsType<SdslvIntegerLiteralExpression>(switchExpression.ElseValue);
    }

    [Fact]
    public void SdslvParser_ParseMatchExpression_ProducesMatchArms()
    {
        var statement = ParseSingleReturn("return match kind { LightKind.Directional => 1; LightKind.Point => 2; else => 0; };");
        var matchExpression = Assert.IsType<SdslvMatchExpression>(statement.Value);
        Assert.Equal(3, matchExpression.Arms.Count);
        Assert.IsType<SdslvEnumVariantMatchArmKind>(matchExpression.Arms[0].Kind);
        Assert.IsType<SdslvElseMatchArmKind>(matchExpression.Arms[2].Kind);
    }

    [Fact]
    public void SdslvParser_ParseTryAndUnwrapExpressions_ProduceFallibilityShapes()
    {
        var shader = ParseShader("""
            shader Basic {
                fn Compute(value: i32) -> i32 {
                    let a: i32 = try loadValue;
                    let b: i32 = unwrap optionalValue;
                    return a + b;
                }
            }
            """);

        var body = Assert.Single(shader.Methods).Body!;
        var firstLet = Assert.IsType<SdslvLetStatement>(body.Statements[0]);
        var secondLet = Assert.IsType<SdslvLetStatement>(body.Statements[1]);
        Assert.IsType<SdslvTryPropagateExpression>(firstLet.Initializer);
        Assert.IsType<SdslvUnwrapExpression>(secondLet.Initializer);
    }

    [Fact]
    public void SdslvParser_InvalidExpression_ReturnsDiagnostics()
    {
        var result = SdslvParser.ParseModule("""
            shader Basic {
                fn Compute(value: i32) -> i32 {
                    return value + ;
                }
            }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Phase == SdslvDiagnosticPhase.Parsing);
    }

    private static SdslvReturnStatement ParseSingleReturn(string statement)
    {
        var shader = ParseShader($$"""
            shader Basic {
                fn Compute(value: i32) -> i32 {
                    {{statement}}
                }
            }
            """);

        return Assert.IsType<SdslvReturnStatement>(Assert.Single(Assert.Single(shader.Methods).Body!.Statements));
    }

    private static SdslvShaderDecl ParseShader(string source)
    {
        var result = SdslvParser.ParseModule(source);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return Assert.IsType<SdslvShaderDecl>(Assert.Single(result.Module!.Declarations));
    }
}
