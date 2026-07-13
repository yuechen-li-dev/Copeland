using Copeland.TS.Compiler;
using Copeland.TS.Lowering;
using Copeland.TS.Mir;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class CompilerFacadeTests
{
    [Fact]
    public void CompileToMir_Succeeds_And_Matches_Lower_Level_Output()
    {
        const string source = """
function one(): number {
  return 1;
}
""";

        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success);
        Assert.Empty(compilation.Diagnostics);
        Assert.NotNull(compilation.MirText);

        var expectedMir = MirTextWriter.Write(MirLowerer.Lower(SyntaxTree.Parse(source)).Program!);
        Assert.Equal(expectedMir, compilation.MirText);
    }

    [Fact]
    public void Invalid_Type_Source_Stops_Before_Mir()
    {
        const string source = """
function main(): number {
  const x: number = "bad";
  return x;
}
""";

        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, d => d.Id == "COPE-TYPE-0001");
        Assert.Null(compilation.MirCompilation);
        Assert.Null(compilation.MirText);
    }

    [Fact]
    public void Syntax_Error_Stops_Before_Binder_And_Mir()
    {
        const string source = """
function main(): number {
  const x: number = ;
  return x;
}
""";

        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.False(compilation.Success);
        Assert.NotEmpty(compilation.Diagnostics);
        Assert.Null(compilation.BoundCompilation);
        Assert.Null(compilation.MirCompilation);
        Assert.Null(compilation.MirText);
    }

    [Fact]
    public void Null_Ban_Is_Visible_Through_Facade()
    {
        const string source = """
function main(): number {
  const x: number = null;
  return x;
}
""";

        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, d => d.Id == "COPE-PROFILE-0005");
        Assert.Null(compilation.MirText);
    }

    [Theory]
    [InlineData("var value: number = 1;")]
    [InlineData("function equal(left: number, right: number): boolean { return left === right; }")]
    [InlineData("function different(left: number, right: number): boolean { return left !== right; }")]
    public void Profile_Rejections_Stop_Before_Mir(string source)
    {
        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.False(compilation.Success);
        Assert.NotNull(compilation.BoundCompilation);
        Assert.Null(compilation.MirCompilation);
        Assert.Null(compilation.MirText);
    }

    [Fact]
    public void Fallibility_Is_Visible_In_Mir()
    {
        const string source = """
function parseNumber(text: string): number ! ParseError {
  return 1;
}

function caller(text: string): number ! ParseError {
  const x: number = parseNumber(text)?;
  return x + 1;
}
""";

        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success);
        Assert.NotNull(compilation.MirText);
        Assert.Contains("! ParseError", compilation.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Enum_Match_Invalid_Does_Not_Emit_Mir()
    {
        const string source = """
enum Choice {
  A,
  B,
}

function value(choice: Choice): number {
  return match choice {
    A => 1,
  };
}
""";

        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.False(compilation.Success);
        Assert.NotEmpty(compilation.Diagnostics);
        Assert.Null(compilation.MirCompilation?.Program);
        Assert.Null(compilation.MirText);
    }

    [Fact]
    public void Compilation_Is_Deterministic()
    {
        const string source = """
function stable(a: number, b: number): number {
  const c: number = a + b;
  return c;
}
""";

        var first = CopelandCompiler.CompileToMir(source);
        var second = CopelandCompiler.CompileToMir(source);

        Assert.Equal(first.Diagnostics, second.Diagnostics);
        Assert.Equal(first.MirText, second.MirText);
    }
}
