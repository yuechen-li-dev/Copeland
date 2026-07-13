using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class MirEvaluationOrderTests
{
    [Fact]
    public void Binary_Operands_Retain_Left_To_Right_Tree_Order_And_Number_Type()
    {
        var program = CompileProgram("""
function value(): number {
  return (1 + 2) * (3 + 4);
}
""");

        var expression = Assert.IsType<MirBinaryExpression>(Assert.IsType<MirReturnStatement>(program.Functions.Single().Body.Single()).Expression);
        var left = Assert.IsType<MirBinaryExpression>(expression.Left);
        var right = Assert.IsType<MirBinaryExpression>(expression.Right);

        Assert.Equal("*", expression.Operator);
        Assert.Equal("number", expression.Type.Name);
        Assert.Equal("+", left.Operator);
        Assert.Equal(1, Assert.IsType<MirLiteralExpression>(left.Left).Value);
        Assert.Equal(2, Assert.IsType<MirLiteralExpression>(left.Right).Value);
        Assert.Equal("+", right.Operator);
        Assert.Equal(3, Assert.IsType<MirLiteralExpression>(right.Left).Value);
        Assert.Equal(4, Assert.IsType<MirLiteralExpression>(right.Right).Value);
    }

    [Fact]
    public void Call_And_Payload_Arguments_Retain_Source_Order()
    {
        var program = CompileProgram("""
enum Pair {
  Value(first: number, second: number),
}

function choose(first: number, second: number): number {
  return first;
}

function call(): number {
  return choose(10 - 3, 8 - 2);
}

function payload(): Pair {
  return Pair.Value(1 + 2, 3 + 4);
}
""");

        var call = Assert.IsType<MirCallExpression>(ReturnExpression(program, "call"));
        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal(10, Assert.IsType<MirLiteralExpression>(Assert.IsType<MirBinaryExpression>(call.Arguments[0]).Left).Value);
        Assert.Equal(8, Assert.IsType<MirLiteralExpression>(Assert.IsType<MirBinaryExpression>(call.Arguments[1]).Left).Value);

        var payload = Assert.IsType<MirEnumValueExpression>(ReturnExpression(program, "payload"));
        Assert.Equal(2, payload.Arguments.Count);
        Assert.Equal(1, Assert.IsType<MirLiteralExpression>(Assert.IsType<MirBinaryExpression>(payload.Arguments[0]).Left).Value);
        Assert.Equal(3, Assert.IsType<MirLiteralExpression>(Assert.IsType<MirBinaryExpression>(payload.Arguments[1]).Left).Value);
    }

    [Fact]
    public void Match_Contains_One_Scrutinee_And_Logical_Operators_Remain_Distinct()
    {
        var program = CompileProgram("""
enum Choice {
  Yes,
  No,
}

function select(choice: Choice): number {
  return match choice {
    Yes => 1,
    No => 0,
  };
}

function both(left: boolean, right: boolean): boolean {
  return left && right;
}
""");

        var match = Assert.IsType<MirMatchExpression>(ReturnExpression(program, "select"));
        Assert.IsType<MirVariableExpression>(match.Scrutinee);
        Assert.Equal(2, match.Arms.Count);

        var logical = Assert.IsType<MirBinaryExpression>(ReturnExpression(program, "both"));
        Assert.Equal("&&", logical.Operator);
        Assert.IsType<MirVariableExpression>(logical.Left);
        Assert.IsType<MirVariableExpression>(logical.Right);
    }

    private static MirProgram CompileProgram(string source)
    {
        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        return Assert.IsType<MirProgram>(compilation.MirCompilation?.Program);
    }

    private static MirExpression ReturnExpression(MirProgram program, string functionName)
    {
        var function = Assert.Single(program.Functions, function => function.Name == functionName);
        return Assert.IsType<MirReturnStatement>(Assert.Single(function.Body)).Expression!;
    }
}
