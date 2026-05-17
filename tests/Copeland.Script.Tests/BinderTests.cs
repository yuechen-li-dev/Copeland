using Copeland.Script.Semantics;
using Copeland.Script.Semantics.Bound;
using Copeland.Script.Syntax;
using Xunit;

namespace Copeland.Script.Tests;

public sealed class BinderTests
{
    [Fact]
    public void Binds_Typed_Variable_And_Assignment()
    {
        var tree = SyntaxTree.Parse("let x: number = 1; x = 2;");
        var bound = Binder.Bind(tree);
        Assert.DoesNotContain(bound.Diagnostics, d => d.Id.StartsWith("COPE-BIND") || d.Id.StartsWith("COPE-TYPE") || d.Id.StartsWith("COPE-PROFILE"));
        var dump = BoundTreeDumper.Dump(bound.Program);
        Assert.Contains("VariableDeclaration let x: number", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_Assignment_To_Const()
    {
        var tree = SyntaxTree.Parse("const x: number = 1; x = 2;");
        var bound = Binder.Bind(tree);
        Assert.Contains(bound.Diagnostics, d => d.Id == "COPE-BIND-0003");
    }

    [Fact]
    public void Reports_Missing_Type_Annotation()
    {
        var tree = SyntaxTree.Parse("let x = 1;");
        var bound = Binder.Bind(tree);
        Assert.Contains(bound.Diagnostics, d => d.Id == "COPE-TYPE-0002");
    }
}
