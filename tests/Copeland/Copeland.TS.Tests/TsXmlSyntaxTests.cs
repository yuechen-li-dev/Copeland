using Copeland.TS.Compiler;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TsXmlSyntaxTests
{
    [Fact]
    public void Parses_Nested_SelfClosing_Attributes_And_Expression_Children_From_Tsx_Fixture()
    {
        SyntaxTree tree = ParseFixture("positive-nesting-and-attributes.tsx", "manifest.tsx");

        Assert.Empty(tree.Diagnostics);
        string dump = SyntaxTreeDumper.Dump(tree.Root);
        Assert.Contains("TsXmlElementExpression", dump, StringComparison.Ordinal);
        Assert.Contains("TsXmlAttribute", dump, StringComparison.Ordinal);
        Assert.Contains("TsXmlText", dump, StringComparison.Ordinal);
        Assert.Contains("TsXmlExpressionChild", dump, StringComparison.Ordinal);
        Assert.Contains("TsXmlElementChild", dump, StringComparison.Ordinal);
        Assert.Contains("hello ", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Parses_Fragments_Without_React_Semantics()
    {
        SyntaxTree tree = SyntaxTree.Parse("const view = <><Item enabled /><Item>{value + 1}</Item></>;", "view.tsx");

        Assert.Empty(tree.Diagnostics);
        Assert.Contains("TsXmlFragmentExpression", SyntaxTreeDumper.Dump(tree.Root), StringComparison.Ordinal);
    }

    [Fact]
    public void Retains_Exact_Source_Positions_For_TsXml_Boundaries_And_Text()
    {
        const string source = "const view = <Item label=\"ok\">text{value}</Item>;";
        SyntaxTree tree = SyntaxTree.Parse(source, "view.tsx");
        var declaration = Assert.IsType<VariableDeclarationStatementSyntax>(
            Assert.IsType<GlobalStatementMemberSyntax>(tree.Root.Members[0]).Statement);
        var element = Assert.IsType<TsXmlElementExpressionSyntax>(declaration.Initializer);
        var text = Assert.IsType<TsXmlTextSyntax>(element.Children[0]);

        Assert.Equal(source.IndexOf("<Item", StringComparison.Ordinal), element.LessToken.Position);
        Assert.Equal(source.IndexOf("label", StringComparison.Ordinal), element.Attributes[0].NameToken.Position);
        Assert.Equal(source.IndexOf("text", StringComparison.Ordinal), text.TextToken.Position);
        Assert.Equal("text", text.TextToken.Text);
        Assert.Equal(source.IndexOf("</Item>", StringComparison.Ordinal), element.CloseLessToken!.Position);
    }

    [Theory]
    [InlineData("mismatched-names.tsx", "COPE-TSXML-0006")]
    [InlineData("malformed-element.tsx", "COPE-TSXML-0002")]
    [InlineData("malformed-attribute.tsx", "COPE-TSXML-0003")]
    public void Reports_Deterministic_TsXml_Diagnostics(string fixtureName, string expectedId)
    {
        SyntaxTree tree = ParseFixture(fixtureName, fixtureName);

        var diagnostic = Assert.Single(tree.Diagnostics, item => item.Id == expectedId);
        Assert.True(diagnostic.Length > 0);
    }

    [Fact]
    public void Parses_TsXml_Only_When_The_Source_Path_Is_Tsx()
    {
        const string source = "const view = <Item />;";

        SyntaxTree tsx = SyntaxTree.Parse(source, "view.tsx");
        SyntaxTree ts = SyntaxTree.Parse(source, "view.ts");
        SyntaxTree jsx = SyntaxTree.Parse(source, "view.jsx");

        Assert.Empty(tsx.Diagnostics);
        Assert.Contains("TsXmlElementExpression", SyntaxTreeDumper.Dump(tsx.Root), StringComparison.Ordinal);
        Assert.DoesNotContain("TsXmlElementExpression", SyntaxTreeDumper.Dump(ts.Root), StringComparison.Ordinal);
        Assert.NotEmpty(ts.Diagnostics);
        Assert.Contains(jsx.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSXML-0001");
    }

    [Fact]
    public void Preserves_Ordinary_LessThan_Expressions_In_Tsx_Mode()
    {
        SyntaxTree tree = SyntaxTree.Parse("const comparison: boolean = first < second;", "view.tsx");

        Assert.Empty(tree.Diagnostics);
        Assert.Contains("BinaryExpression", SyntaxTreeDumper.Dump(tree.Root), StringComparison.Ordinal);
        Assert.DoesNotContain("TsXml", SyntaxTreeDumper.Dump(tree.Root), StringComparison.Ordinal);
    }

    [Fact]
    public void Binding_Requires_A_Future_Semantic_Profile()
    {
        CopelandCompilation compilation = CopelandCompiler.Compile(
            "const view = <Workspace name=\"sample\" />;",
            new CopelandCompilationOptions
            {
                SourcePath = "manifest.tsx",
                TargetStage = CopelandCompilationStage.Bound,
            });

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSXML-0101");
    }

    private static SyntaxTree ParseFixture(string fixtureName, string sourcePath)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TsXml", fixtureName);
        return SyntaxTree.Parse(File.ReadAllText(path), sourcePath);
    }
}
