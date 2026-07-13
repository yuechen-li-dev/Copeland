using System.Globalization;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Lowering;
using Copeland.TS.Mir;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class JavaScriptBackendTests
{
    [Fact]
    public void Emits_Minimal_Void_Function()
    {
        JavaScriptCompilation result = Emit("function noop(): void { return; }");

        Assert.True(result.Success);
        Assert.Equal("\"use strict\";\n\nfunction noop() {\n    return;\n}\n", result.SourceText);
    }

    [Fact]
    public void Emits_Deterministic_Lf_JavaScript_For_Supported_Program()
    {
        MirProgram program = Lower("""
            function add(left: number, right: number): number {
              return left + right;
            }

            function main(): number {
              const answer: number = add(40, 2);
              return if true { answer } else { 0 };
            }
            """);

        JavaScriptCompilation first = JavaScriptBackend.Emit(program);
        JavaScriptCompilation second = JavaScriptBackend.Emit(program);

        Assert.True(first.Success);
        Assert.Equal(first.SourceText, second.SourceText);
        Assert.DoesNotContain("\r", first.SourceText, StringComparison.Ordinal);
        Assert.Equal("""
            "use strict";

            function add(left, right) {
                return (left + right);
            }

            function main() {
                const answer = add(40, 2);
                return (true ? answer : 0);
            }
            
            """.Replace("\r\n", "\n", StringComparison.Ordinal), first.SourceText);
    }

    [Theory]
    [InlineData("+", "(left + right)")]
    [InlineData("-", "(left - right)")]
    [InlineData("*", "(left * right)")]
    [InlineData("/", "(left / right)")]
    [InlineData("%", "(left % right)")]
    public void Emits_Supported_Arithmetic(string operation, string expectedExpression)
    {
        JavaScriptCompilation result = Emit($"function calculate(left: number, right: number): number {{ return left {operation} right; }}");

        Assert.True(result.Success);
        Assert.Contains($"return {expectedExpression};", result.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void Formats_Numbers_Invariantly()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            JavaScriptCompilation result = JavaScriptBackend.Emit(new MirProgram([], [
                new MirFunction("main", [], new MirType("number"), null, [], [
                    new MirReturnStatement(new MirLiteralExpression(1.5d, new MirType("number")))])
            ]));

            Assert.True(result.Success);
            Assert.Contains("return 1.5;", result.SourceText, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void Rejects_Unsupported_Mir_Without_Partial_Artifact()
    {
        MirType number = new("number");
        MirType boolean = new("boolean");
        var program = new MirProgram(
            [new MirEnum("Choice", [new MirEnumCase("Some", [new MirEnumPayloadField("value", number)])])],
            [
                new MirFunction("fallible", [], number, new MirType("ParseError"), [], []),
                new MirFunction("array", [], new MirType("number[]"), null, [], [new MirReturnStatement(new MirArrayExpression([], new MirType("number[]")))]),
                new MirFunction("equality", [], boolean, null, [], [new MirReturnStatement(new MirBinaryExpression("==", new MirLiteralExpression(1, number), new MirLiteralExpression(1, number), boolean))]),
                new MirFunction("loop", [], number, null, [], [new MirWhileStatement(new MirLiteralExpression(true, boolean), [])]),
                new MirFunction("assignment", [], number, null, [], [new MirExpressionStatement(new MirAssignmentExpression("value", new MirLiteralExpression(1, number), number))]),
                new MirFunction("match", [], number, null, [], [new MirReturnStatement(new MirMatchExpression(new MirVariableExpression("choice", new MirType("Choice")), [], number))]),
                new MirFunction("unknown", [], number, null, [], [new MirReturnStatement(new MirBinaryExpression("**", new MirLiteralExpression(2, number), new MirLiteralExpression(3, number), number))])
            ]);

        JavaScriptCompilation result = JavaScriptBackend.Emit(program);

        Assert.False(result.Success);
        Assert.Null(result.SourceText);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("COPE-JS-0001", diagnostic.Id));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("fallible function 'fallible'", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("enum 'Choice'", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("array expression", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("binary operator '=='", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("while loop", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("assignment to 'value'", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("match expression", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("binary operator '**'", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_Fallible_And_Propagated_Calls()
    {
        MirType number = new("number");
        MirType error = new("ParseError");
        var program = new MirProgram([], [
            new MirFunction("parse", [], number, error, [], []),
            new MirFunction("main", [], number, null, [], [
                new MirReturnStatement(new MirCallExpression("parse", [], number, true, error, true))])
        ]);

        JavaScriptCompilation result = JavaScriptBackend.Emit(program);

        Assert.False(result.Success);
        Assert.Null(result.SourceText);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("fallible call 'parse'", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("propagated call 'parse'", StringComparison.Ordinal));
    }

    private static JavaScriptCompilation Emit(string source)
    {
        return JavaScriptBackend.Emit(Lower(source));
    }

    private static MirProgram Lower(string source)
    {
        var mir = MirLowerer.Lower(SyntaxTree.Parse(source));
        Assert.Empty(mir.Diagnostics);
        Assert.NotNull(mir.Program);
        return mir.Program;
    }
}
