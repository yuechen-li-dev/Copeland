using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ParserTests
{
    [Fact]
    public void Parses_Function_With_Parameters_And_ReturnType()
    {
        const string source = "function add(a: number, b: number): number { return a + b; }";
        var tree = SyntaxTree.Parse(source);
        var dump = SyntaxTreeDumper.Dump(tree.Root);
        Assert.Contains("FunctionDeclaration", dump, StringComparison.Ordinal);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
    }

    [Fact]
    public void Parses_Fallible_Return_Syntax()
    {
        const string source = "function parse(text: string): number ! ParseError { return 1; }";
        var tree = SyntaxTree.Parse(source);
        var dump = SyntaxTreeDumper.Dump(tree.Root);
        Assert.Contains("!", dump, StringComparison.Ordinal);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
    }

    [Fact]
    public void Parses_Postfix_Unwrap_Independently_From_Prefix_And_Result_Type_Bang()
    {
        const string source = "function unwrap(value: number ! string, condition: boolean): number { const negated: boolean = !condition; return value!; }";
        var tree = SyntaxTree.Parse(source);
        var dump = SyntaxTreeDumper.Dump(tree.Root);

        Assert.Contains("UnaryExpression", dump, StringComparison.Ordinal);
        Assert.Contains("UnwrapExpression", dump, StringComparison.Ordinal);
        Assert.DoesNotContain(tree.Diagnostics, diagnostic => diagnostic.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
    }

    [Fact]
    public void Parses_Var_Declaration_For_Profile_Validation()
    {
        var tree = SyntaxTree.Parse("var value: number = 1;");

        Assert.DoesNotContain(tree.Diagnostics, diagnostic =>
            diagnostic.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
        Assert.Contains("VariableDeclaration", SyntaxTreeDumper.Dump(tree.Root), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("==")]
    [InlineData("!=")]
    [InlineData("===")]
    [InlineData("!==")]
    public void Parses_All_Equality_Spellings_For_Profile_Validation(string equalityOperator)
    {
        var tree = SyntaxTree.Parse($"function equal(left: number, right: number): boolean {{ return left {equalityOperator} right; }}");

        Assert.DoesNotContain(tree.Diagnostics, diagnostic =>
            diagnostic.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
    }

    [Fact]
    public void Parses_If_Expression_Syntax()
    {
        const string source = "function choose(flag: boolean): number { return if flag { 1 } else { 2 }; }";
        var tree = SyntaxTree.Parse(source);
        var dump = SyntaxTreeDumper.Dump(tree.Root);
        Assert.Contains("IfExpression", dump, StringComparison.Ordinal);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("let x: = 1;", "COPE-PARSE-0006")]
    [InlineData("let xs: number[ = [1];", "COPE-PARSE-0008")]
    public void Reports_Type_Annotation_Diagnostics(string source, string expectedId)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.Contains(tree.Diagnostics, d => d.Id == expectedId);
    }

    [Fact]
    public void Parses_Enum_Declarations_With_Payloads()
    {
        const string source = """
enum Shape {
  Point,
  Circle(radius: number),
  Rect(width: number, height: number),
}
""";
        var tree = SyntaxTree.Parse(source);
        var dump = SyntaxTreeDumper.Dump(tree.Root);

        Assert.Contains("EnumDeclaration", dump, StringComparison.Ordinal);
        Assert.Contains("EnumCase", dump, StringComparison.Ordinal);
        Assert.Contains("EnumPayloadField", dump, StringComparison.Ordinal);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
    }

    [Fact]
    public void Reports_Missing_Else_For_If_Expression()
    {
        var tree = SyntaxTree.Parse("""
function value(flag: boolean): number {
  return if flag {
    1
  };
}
""");

        Assert.Contains(tree.Diagnostics, d => d.Id.StartsWith("COPE-PARSE-", StringComparison.Ordinal));
    }
}
