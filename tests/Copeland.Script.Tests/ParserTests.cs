using Copeland.Script.Syntax;
using Xunit;

namespace Copeland.Script.Tests;

public sealed class ParserTests
{
    [Theory]
    [InlineData("1 + 2 * 3;", "BinaryExpression", "StarToken")]
    [InlineData("(1 + 2) * 3;", "ParenthesizedExpression", "PlusToken")]
    public void Parses_Binary_Precedence(string source, string mustContainNode, string mustContainToken)
    {
        var tree = SyntaxTree.Parse(source);
        var dump = SyntaxTreeDumper.Dump(tree.Root);

        Assert.Contains(mustContainNode, dump, StringComparison.Ordinal);
        Assert.Contains(mustContainToken, dump, StringComparison.Ordinal);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
    }

    [Fact]
    public void Parses_Statements_And_Members()
    {
        const string source = """
function add(a, b) { return a + b; }
let x = add(1, 2);
if (x > 1) { x = x - 1; } else { x = 0; }
while (x > 0) x = x - 1;
for (let i = 0; i < 3; i = i + 1) { x = x + i; }
x = foo.bar(1, { y: 2 }, [3]);
""";
        var tree = SyntaxTree.Parse(source);

        Assert.DoesNotContain(tree.Diagnostics, d => d.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
        Assert.Equal(SyntaxKind.CompilationUnit, tree.Root.Kind);
    }

    [Theory]
    [InlineData("let x = ;", "COPE-PARSE-0002")]
    [InlineData("if (x { x = 1; }", "COPE-PARSE-0004")]
    [InlineData("foo(1, ;", "COPE-PARSE-0002")]
    [InlineData("1 = 2;", "COPE-PARSE-0005")]
    [InlineData("let = 1;", "COPE-PARSE-0004")]
    public void Reports_Parse_Diagnostics(string source, string expectedId)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.Contains(tree.Diagnostics, d => d.Id == expectedId);
    }
}
