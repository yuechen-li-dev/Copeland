using System.Text.RegularExpressions;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Lowering;
using Copeland.TS.Mir;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class CSharpBackendTests
{
    [Fact]
    public void Emits_Private_Unwrap_Panic_Only_For_Unwrap()
    {
        var unwrap = CSharpBackend.Emit(Lower("function parse(): number ! string { return err(\"bad\"); } function main(): number { return parse()!; }"));
        var ordinaryResult = CSharpBackend.Emit(Lower("function parse(): number ! string { return err(\"bad\"); }"));

        Assert.Empty(unwrap.Diagnostics);
        Assert.Contains("COPE-PANIC-UNWRAP: Result unwrap encountered err", unwrap.SourceText, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(unwrap.SourceText, @"= parse\(\);").Cast<Match>());
        Assert.DoesNotContain("COPE-PANIC-UNWRAP", ordinaryResult.SourceText, StringComparison.Ordinal);
    }

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
