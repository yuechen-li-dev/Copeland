using Copeland.Script.Codegen.CSharp;
using Copeland.Script.Mir;
using Copeland.Script.Syntax;
using Xunit;

namespace Copeland.Script.Tests;

public sealed class CSharpBackendTests
{
    [Fact]
    public void Deterministic_Emit_Repeats()
    {
        var program = Lower("function one(): number { return 1; }");
        var a = CSharpBackend.Emit(program).SourceText;
        var b = CSharpBackend.Emit(program).SourceText;
        Assert.Equal(a, b);
    }

    [Fact]
    public void Escapes_String_Literals()
    {
        var text = Emit("function one(): string { return \"a\\n\\t\\\\\\\"b\"; }");
        Assert.Contains("\\n", text, StringComparison.Ordinal);
        Assert.Contains("\\t", text, StringComparison.Ordinal);
        Assert.Contains("\\\\\\\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Mangles_Keyword_Name()
    {
        var text = Emit("function f(class: number): number { return class; }");
        Assert.Contains("double @class", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_Support_Types_Only_When_Needed()
    {
        var nonFallible = Emit("function one(): number { return 1; }");
        Assert.DoesNotContain("CopeResult", nonFallible, StringComparison.Ordinal);

        var fallible = Emit("function one(): number ! ParseError { return 1; }");
        Assert.Contains("CopeResult", fallible, StringComparison.Ordinal);
        Assert.Contains("record struct ParseError", fallible, StringComparison.Ordinal);
    }

    private static string Emit(string source) => CSharpBackend.Emit(Lower(source)).SourceText;

    private static MirProgram Lower(string source)
    {
        var mir = MirLowerer.Lower(SyntaxTree.Parse(source));
        Assert.NotNull(mir.Program);
        return mir.Program!;
    }
}
