using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ParserTests
{
    [Theory]
    [InlineData("record table Marks { valid: boolean = [true ... 3; id: int = [10, 20, 30]; }")]
    [InlineData("record table Marks { valid: boolean = [true ... ; id: int = [10, 20, 30]; }")]
    [InlineData("record table Marks { ] id: int = [10, 20, 30]; }")]
    [InlineData("record table Marks = derive Source as row { ] id: int = row.id; }")]
    public void Malformed_Table_Columns_Report_Diagnostics_And_Recover_Later_Declarations(string declaration)
    {
        const string followingFunction = "function after(): int { return 1; }";
        SyntaxTree tree = SyntaxTree.Parse(declaration + followingFunction);

        Assert.Contains(tree.Diagnostics, diagnostic =>
            diagnostic.Id.StartsWith("COPE-PARSE-", StringComparison.Ordinal));
        TableDeclarationSyntax table = Assert.IsType<TableDeclarationSyntax>(tree.Root.Members[0]);
        if (table.DerivedClause is not null)
        {
            Assert.Contains(table.DerivedClause.Columns, column => column.Identifier.Text == "id");
        }
        else
        {
            Assert.Contains(table.Columns, column => column.Identifier.Text == "id");
        }
        Assert.Contains(tree.Root.Members.OfType<FunctionDeclarationSyntax>(), function =>
            function.Identifier.Text == "after");
    }

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
    public void Parses_Contextual_Interfaces_And_Explicit_Generic_Calls_Without_Changing_Comparisons()
    {
        const string source = "interface Positioned { x: number; y: number; } function sum<T extends Positioned>(value: T): number { return value.x + value.y; } function compare(a: number, b: number): boolean { return a < b && b > a; } const answer: number = sum<number>(42);";
        var tree = SyntaxTree.Parse(source);

        Assert.Contains("InterfaceDeclaration", SyntaxTreeDumper.Dump(tree.Root), StringComparison.Ordinal);
        Assert.Contains("GenericCallExpression", SyntaxTreeDumper.Dump(tree.Root), StringComparison.Ordinal);
        Assert.DoesNotContain(tree.Diagnostics, diagnostic => diagnostic.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
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

    [Fact]
    public void Parses_Loop_Transfers_As_Dedicated_Statements()
    {
        var tree = SyntaxTree.Parse("function main(): void { while (true) { break; } for (;;) { continue; } }");
        var dump = SyntaxTreeDumper.Dump(tree.Root);

        Assert.Contains("WhileStatement", dump, StringComparison.Ordinal);
        Assert.Contains("ForStatement", dump, StringComparison.Ordinal);
        Assert.Contains("BreakStatement", dump, StringComparison.Ordinal);
        Assert.Contains("ContinueStatement", dump, StringComparison.Ordinal);
        Assert.DoesNotContain(tree.Diagnostics, diagnostic => diagnostic.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
    }

    [Fact]
    public void Parses_Try_Except_Value_Blocks_And_Postfix_Propagation()
    {
        var tree = SyntaxTree.Parse("function read(): number ! string { return ok(1); } function main(): number { return try { const value: number = read()?; value } except (error) { 0 }; }");

        Assert.DoesNotContain(tree.Diagnostics, diagnostic => diagnostic.Id.StartsWith("COPE-", StringComparison.Ordinal));
        var dump = SyntaxTreeDumper.Dump(tree.Root);
        Assert.Contains("TryExceptExpression", dump, StringComparison.Ordinal);
        Assert.Contains("TryValueBlock", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_Control_Flow_In_Try_Value_Block()
    {
        var tree = SyntaxTree.Parse("function main(): number { return try { return 1; } except (error) { 0 }; }");

        Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Id == "COPE-TRY-0005");
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
