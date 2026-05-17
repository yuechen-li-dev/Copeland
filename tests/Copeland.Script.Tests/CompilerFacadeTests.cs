using Copeland.Script.Codegen.CSharp;
using Copeland.Script.Compiler;
using Copeland.Script.Mir;
using Copeland.Script.Syntax;
using Xunit;

namespace Copeland.Script.Tests;

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
        Assert.Null(compilation.CSharpText);

        var expectedMir = MirTextWriter.Write(MirLowerer.Lower(SyntaxTree.Parse(source)).Program!);
        Assert.Equal(expectedMir, compilation.MirText);
    }

    [Fact]
    public void CompileToCSharp_Succeeds_And_Matches_Lower_Level_Output()
    {
        const string source = """
function add(a: number, b: number): number {
  return a + b;
}
""";

        var compilation = CopelandCompiler.CompileToCSharp(source);

        Assert.True(compilation.Success);
        Assert.Empty(compilation.Diagnostics);
        Assert.NotNull(compilation.MirText);
        Assert.NotNull(compilation.CSharpText);

        var mir = MirLowerer.Lower(SyntaxTree.Parse(source)).Program!;
        var expectedCSharp = CSharpBackend.Emit(mir).SourceText;
        Assert.Equal(expectedCSharp, compilation.CSharpText);
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
        Assert.Null(compilation.CSharpText);
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
        Assert.Null(compilation.CSharpText);
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
        Assert.Null(compilation.CSharpText);
    }

    [Fact]
    public void Fallibility_Is_Visible_In_Mir_And_CSharp()
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

        var mirCompilation = CopelandCompiler.CompileToMir(source);
        Assert.True(mirCompilation.Success);
        Assert.NotNull(mirCompilation.MirText);
        Assert.Contains("! ParseError", mirCompilation.MirText, StringComparison.Ordinal);

        var csharpCompilation = CopelandCompiler.CompileToCSharp(source);
        Assert.True(csharpCompilation.Success);
        Assert.NotNull(csharpCompilation.CSharpText);
        Assert.Contains("CopeResult<double, ParseError>", csharpCompilation.CSharpText, StringComparison.Ordinal);
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

        var a = CopelandCompiler.CompileToCSharp(source);
        var b = CopelandCompiler.CompileToCSharp(source);

        Assert.Equal(a.Diagnostics, b.Diagnostics);
        Assert.Equal(a.MirText, b.MirText);
        Assert.Equal(a.CSharpText, b.CSharpText);
    }
}
