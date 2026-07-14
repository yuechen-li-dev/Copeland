using System.Text.RegularExpressions;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Lowering;
using Copeland.TS.Mir;
using Copeland.TS.Syntax;
using Copeland.TS.Backend.CSharp.Tests.Runtime;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class CSharpBackendTests
{
    [Fact]
    public void Rejects_record_mir_once_without_partial_artifact()
    {
        var program = Lower("record Point { x: number; } function main(): Point { return { x: 1 }; }");

        var first = CSharpBackend.Emit(program);
        var second = CSharpBackend.Emit(program);

        var diagnostic = Assert.Single(first.Diagnostics);
        Assert.Equal("COPE-CS-REC-0001", diagnostic.Id);
        Assert.Equal(string.Empty, first.SourceText);
        Assert.Equal(first.Diagnostics, second.Diagnostics);
        Assert.Equal(string.Empty, second.SourceText);
    }

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

    [Fact]
    public void Lowers_Try_Except_Using_Branches_Without_Csharp_Exception_Handling()
    {
        var compilation = CSharpBackend.Emit(Lower("function read(): number ! string { return err(\"bad\"); } function main(): number { return try { read()? } except (error) { 42 }; }"));

        Assert.Empty(compilation.Diagnostics);
        Assert.Contains("goto __cope_try_handler_h1_0;", compilation.SourceText, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\btry\s*\{", compilation.SourceText);
        Assert.DoesNotMatch(@"\bcatch\s*\(", compilation.SourceText);
        var generated = RoslynCompileHelper.CompileGeneratedSource(compilation.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(42d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
    }

    [Fact]
    public void Lowers_Nested_Handler_Propagation_To_The_Next_Outer_Handler()
    {
        var compilation = CSharpBackend.Emit(Lower("function read(): number ! string { return err(\"bad\"); } function main(): number { return try { try { read()? } except (inner) { read()? } } except (outer) { 7 }; }"));

        Assert.Empty(compilation.Diagnostics);
        var generated = RoslynCompileHelper.CompileGeneratedSource(compilation.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(7d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
    }

    [Fact]
    public void Postfix_Unwrap_Panic_Bypasses_Try_Except_Handler()
    {
        var compilation = CSharpBackend.Emit(Lower("function good(): number ! string { return ok(1); } function bad(): number ! string { return err(\"bad\"); } function main(): number { return try { good()?; bad()! } except (error) { 0 }; }"));

        Assert.Empty(compilation.Diagnostics);
        Assert.Contains("COPE-PANIC-UNWRAP", compilation.SourceText, StringComparison.Ordinal);
        var generated = RoslynCompileHelper.CompileGeneratedSource(compilation.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        var panic = Assert.ThrowsAny<Exception>(() => GeneratedModuleInvoker.Invoke(generated.Assembly!, "main"));
        Assert.Equal("COPE-PANIC-UNWRAP: Result unwrap encountered err", panic.Message);
    }

    [Fact]
    public void Rejects_Malformed_Synthetic_Lexical_Handler_Target()
    {
        var number = new MirNamedType("number");
        var stringType = new MirNamedType("string");
        var result = new MirResultType(number, stringType);
        var malformed = new MirTryExpression(
            new MirHandlerId(1),
            new MirValueBlock([], new MirPropagateExpression(new MirCallExpression("read", [], result), new MirPropagationTarget.LexicalExcept(new MirHandlerId(2)), number)),
            new MirTryBinding("error", stringType),
            stringType,
            new MirValueBlock([], new MirLiteralExpression(0, number)),
            number);
        var program = new MirProgram([], [new MirFunction("main", [], number, [], [new MirReturnStatement(malformed)])]);

        var compilation = CSharpBackend.Emit(program);

        Assert.Empty(compilation.SourceText);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Message.Contains("Lexical propagation target 'h2'", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_Incompatible_Function_Return_Propagation_At_The_Mir_Boundary()
    {
        var number = new MirNamedType("number");
        var stringType = new MirNamedType("string");
        var result = new MirResultType(number, stringType);
        var propagation = new MirPropagateExpression(
            new MirCallExpression("read", [], result),
            new MirPropagationTarget.FunctionReturn(),
            number);
        var program = new MirProgram([], [
            new MirFunction("main", [], number, [], [new MirReturnStatement(propagation)]),
        ]);

        CSharpCompilation compilation = CSharpBackend.Emit(program);

        Assert.Empty(compilation.SourceText);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Message.Contains(
            "Function-return propagation requires a Result function return type.",
            StringComparison.Ordinal));
    }

    private static string Emit(string source) => CSharpBackend.Emit(Lower(source)).SourceText;

    private static MirProgram Lower(string source)
    {
        var mir = MirLowerer.Lower(SyntaxTree.Parse(source));
        Assert.NotNull(mir.Program);
        return mir.Program!;
    }
}
