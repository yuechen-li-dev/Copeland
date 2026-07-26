using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class MirEvaluationOrderTests
{
    [Fact]
    public void Unwrap_Lowers_To_Dedicated_Mir_And_Chains_Left_To_Right()
    {
        var program = CompileProgram("""
function nested(value: (number ! string) ! string): number {
  return value!!;
}
""");

        var outer = Assert.IsType<MirUnwrapExpression>(ReturnExpression(program, "nested"));
        var inner = Assert.IsType<MirUnwrapExpression>(outer.Operand);
        Assert.Equal("number", outer.Type.Name);
        Assert.Equal("number ! string", inner.Type.Name);
    }

    [Fact]
    public void Binary_Operands_Retain_Left_To_Right_Tree_Order_And_Float_Type()
    {
        var program = CompileProgram("""
function value(): number {
  return (1.0 + 2.0) * (3.0 + 4.0);
}
""");

        var expression = Assert.IsType<MirBinaryExpression>(Assert.IsType<MirReturnStatement>(program.Functions.Single().Body.Single()).Expression);
        var left = Assert.IsType<MirBinaryExpression>(expression.Left);
        var right = Assert.IsType<MirBinaryExpression>(expression.Right);

        Assert.Equal("*", expression.Operator);
        Assert.Equal("float", expression.Type.Name);
        Assert.Equal("+", left.Operator);
        Assert.Equal(1d, Assert.IsType<MirLiteralExpression>(left.Left).Value);
        Assert.Equal(2d, Assert.IsType<MirLiteralExpression>(left.Right).Value);
        Assert.Equal("+", right.Operator);
        Assert.Equal(3d, Assert.IsType<MirLiteralExpression>(right.Left).Value);
        Assert.Equal(4d, Assert.IsType<MirLiteralExpression>(right.Right).Value);
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
  return choose(10.0 - 3.0, 8.0 - 2.0);
}

function payload(): Pair {
  return Pair.Value(1.0 + 2.0, 3.0 + 4.0);
}
""");

        var call = Assert.IsType<MirCallExpression>(ReturnExpression(program, "call"));
        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal(10d, Assert.IsType<MirLiteralExpression>(Assert.IsType<MirBinaryExpression>(call.Arguments[0]).Left).Value);
        Assert.Equal(8d, Assert.IsType<MirLiteralExpression>(Assert.IsType<MirBinaryExpression>(call.Arguments[1]).Left).Value);

        var payload = Assert.IsType<MirEnumValueExpression>(ReturnExpression(program, "payload"));
        Assert.Equal(2, payload.Arguments.Count);
        Assert.Equal(1d, Assert.IsType<MirLiteralExpression>(Assert.IsType<MirBinaryExpression>(payload.Arguments[0]).Left).Value);
        Assert.Equal(3d, Assert.IsType<MirLiteralExpression>(Assert.IsType<MirBinaryExpression>(payload.Arguments[1]).Left).Value);
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
