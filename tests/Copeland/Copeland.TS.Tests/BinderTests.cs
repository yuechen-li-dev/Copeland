using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

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

    [Fact]
    public void Match_Duplicate_Arm_Report()
    {
        var tree = SyntaxTree.Parse("""
enum Choice { A, B, }
function value(choice: Choice): number {
  return match choice {
    A => 1,
    A => 2,
    B => 3,
  };
}
""");
        var bound = Binder.Bind(tree);
        Assert.Contains(bound.Diagnostics, d => d.Id == "COPE-MATCH-0003");
    }

    [Fact]
    public void Match_Non_Exhaustive_Report()
    {
        var tree = SyntaxTree.Parse("""
enum Choice { A, B, C, }
function value(choice: Choice): number {
  return match choice {
    A => 1,
    B => 2,
  };
}
""");
        var bound = Binder.Bind(tree);
        Assert.Contains(bound.Diagnostics, d => d.Id == "COPE-MATCH-0004");
    }

    [Fact]
    public void Match_Payload_Arity_And_Duplicate_Name_Report()
    {
        var tree = SyntaxTree.Parse("""
enum Shape { Rect(width: number, height: number), }
function value(shape: Shape): number {
  return match shape {
    Rect(x, x) => x,
  };
}
""");
        var bound = Binder.Bind(tree);
        Assert.Contains(bound.Diagnostics, d => d.Id == "COPE-MATCH-0006");
    }

    [Fact]
    public void Match_Payload_Arity_Mismatch_Report()
    {
        var tree = SyntaxTree.Parse("""
enum Shape { Rect(width: number, height: number), }
function value(shape: Shape): number {
  return match shape {
    Rect(width) => width,
  };
}
""");
        var bound = Binder.Bind(tree);
        Assert.Contains(bound.Diagnostics, d => d.Id == "COPE-MATCH-0005");
    }

    [Fact]
    public void Match_Arm_Type_Mismatch_Report()
    {
        var tree = SyntaxTree.Parse("""
enum Choice { A, B, }
function value(choice: Choice): number {
  return match choice {
    A => 1,
    B => "bad",
  };
}
""");
        var bound = Binder.Bind(tree);
        Assert.Contains(bound.Diagnostics, d => d.Id == "COPE-MATCH-0007");
    }

    [Fact]
    public void Match_Payload_Variable_Does_Not_Leak()
    {
        var tree = SyntaxTree.Parse("""
enum Shape { Circle(radius: number), }
function value(shape: Shape): number {
  const x: number = match shape {
    Circle(radius) => radius,
  };
  return radius;
}
""");
        var bound = Binder.Bind(tree);
        Assert.Contains(bound.Diagnostics, d => d.Id == "COPE-BIND-0001");
    }

    [Fact]
    public void If_Expression_Invalid_Cases_Report()
    {
        var nonBool = Binder.Bind(SyntaxTree.Parse("function value(x: number): number { return if x { 1 } else { 2 }; }"));
        Assert.Contains(nonBool.Diagnostics, d => d.Id == "COPE-TYPE-0017");

        var mismatch = Binder.Bind(SyntaxTree.Parse("function value(flag: boolean): number { return if flag { 1 } else { \"bad\" }; }"));
        Assert.Contains(mismatch.Diagnostics, d => d.Id == "COPE-TYPE-0018");
    }

    [Fact]
    public void Profile_Bans_For_Ternary_And_Optional_Chaining_Report()
    {
        var ternary = Binder.Bind(SyntaxTree.Parse("function value(flag: boolean): number { return flag ? 1 : 2; }"));
        Assert.Contains(ternary.Diagnostics, d => d.Id == "COPE-PROFILE-0007");

        var optional = Binder.Bind(SyntaxTree.Parse("function value(x: number): number { return x?.toString(); }"));
        Assert.Contains(optional.Diagnostics, d => d.Id == "COPE-PROFILE-0008");
    }

}
