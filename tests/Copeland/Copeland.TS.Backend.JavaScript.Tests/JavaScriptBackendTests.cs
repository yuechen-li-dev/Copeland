using System.Globalization;
using System.Text.RegularExpressions;
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

    [Theory]
    [InlineData("true", "true", "==", "(true === true)")]
    [InlineData("true", "false", "!=", "(true !== false)")]
    [InlineData("42", "42", "==", "(42 === 42)")]
    [InlineData("42", "41", "!=", "(42 !== 41)")]
    [InlineData("\"same\"", "\"same\"", "==", "(\"same\" === \"same\")")]
    [InlineData("\"same\"", "\"different\"", "!=", "(\"same\" !== \"different\")")]
    public void Emits_Primitive_Equality_As_JavaScript_Strict_Equality(
        string left,
        string right,
        string operation,
        string expectedExpression)
    {
        JavaScriptCompilation result = Emit($"function main(): boolean {{ return {left} {operation} {right}; }}");

        Assert.True(result.Success);
        Assert.Contains($"return {expectedExpression};", result.SourceText, StringComparison.Ordinal);
        AssertNoLooseEquality(result.SourceText!);
    }

    [Fact]
    public void Emits_String_Literals_With_Deterministic_JavaScript_Escaping()
    {
        var program = new MirProgram([], [
            new MirFunction("main", [], new MirType("string"), null, [], [
                new MirReturnStatement(new MirLiteralExpression("\"\\\n\r\t\u0001\u2028\u2029\ud800", new MirType("string")))])
        ]);

        JavaScriptCompilation result = JavaScriptBackend.Emit(program);

        Assert.True(result.Success);
        Assert.Equal("\"use strict\";\n\nfunction main() {\n    return \"\\\"\\\\\\n\\r\\t\\u0001\\u2028\\u2029\\ud800\";\n}\n", result.SourceText);
    }

    [Fact]
    public void Preserves_Equality_Operand_Order_And_Evaluates_Each_Operand_Once()
    {
        JavaScriptCompilation result = Emit("""
            function left(): number { return 1; }
            function right(): number { return 1; }
            function main(): boolean { return left() == right(); }
            """);

        Assert.True(result.Success);
        Assert.Contains("return (left() === right());", result.SourceText, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(result.SourceText!, @"return \(left\(\) === right\(\)\);").Cast<Match>());
        AssertNoLooseEquality(result.SourceText!);
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
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-JS-0002"
            && diagnostic.Message.Contains("non-exhaustive match", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("fallible function 'fallible'", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("array expression", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("while loop", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("assignment to 'value'", StringComparison.Ordinal));
    }

    [Fact]
    public void Emits_Nominal_Frozen_NullPrototype_Enum_Values_And_Exhaustive_Matches()
    {
        JavaScriptCompilation result = Emit("""
            enum Choice {
              None,
              Pair(first: number, second: string),
            }

            function main(choice: Choice): string {
              return match choice {
                None => "none",
                Pair(first, second) => second,
              };
            }
            """);

        Assert.True(result.Success);
        Assert.Contains("Object.create(null)", result.SourceText, StringComparison.Ordinal);
        Assert.Contains("Object.freeze", result.SourceText, StringComparison.Ordinal);
        Assert.Contains("switch (__cope_m3_match_", result.SourceText, StringComparison.Ordinal);
        Assert.Contains("const first = __cope_m3_match_", result.SourceText, StringComparison.Ordinal);
        Assert.Contains("const second = __cope_m3_match_", result.SourceText, StringComparison.Ordinal);
        Assert.Contains("case \"None\"", result.SourceText, StringComparison.Ordinal);
        Assert.Contains("case \"Pair\"", result.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_Malformed_Enum_And_Match_Mir_Without_Partial_Artifact()
    {
        MirType number = new("number");
        MirType choice = new("Choice");
        var program = new MirProgram(
            [new MirEnum("Choice", [new MirEnumCase("Some", [new MirEnumPayloadField("value", number)])])],
            [
                new MirFunction("main", [], number, null, [], [
                    new MirReturnStatement(new MirMatchExpression(
                        new MirVariableExpression("choice", choice),
                        [new MirMatchArm("Some", [], new MirLiteralExpression(1, number))],
                        number))]),
                new MirFunction("bad", [], choice, null, [], [
                    new MirReturnStatement(new MirEnumValueExpression("Missing", "Some", [], choice))])
            ]);

        JavaScriptCompilation result = JavaScriptBackend.Emit(program);

        Assert.False(result.Success);
        Assert.Null(result.SourceText);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-JS-0002"
            && diagnostic.Message.Contains("has 0 bindings but case declares 1 payloads", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-JS-0002"
            && diagnostic.Message.Contains("unknown enum 'Missing'", StringComparison.Ordinal));
    }

    [Fact]
    public void Preserves_LeftToRight_Payload_Argument_Order()
    {
        JavaScriptCompilation result = Emit("""
            enum Pair {
              Value(first: number, second: number),
            }

            function first(): number { return 1; }
            function second(): number { return 2; }
            function make(): Pair { return Pair.Value(first(), second()); }
            """);

        Assert.True(result.Success);
        Assert.Contains("[first(), second()]", result.SourceText, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("number[]")]
    [InlineData("Choice")]
    [InlineData("Result<number, ParseError>")]
    [InlineData("object")]
    [InlineData("Closure")]
    [InlineData("future-value")]
    public void Rejects_Unsupported_Equality_Families_Without_Partial_Artifact(string typeName)
    {
        MirType type = new(typeName);
        MirType boolean = new("boolean");
        var program = new MirProgram([], [
            new MirFunction("main", [], boolean, null, [], [
                new MirReturnStatement(new MirBinaryExpression(
                    "==",
                    new MirVariableExpression("left", type),
                    new MirVariableExpression("right", type),
                    boolean))])
        ]);

        JavaScriptCompilation result = JavaScriptBackend.Emit(program);

        Assert.False(result.Success);
        Assert.Null(result.SourceText);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-JS-0001"
            && diagnostic.Message.Contains($"equality for type '{typeName}'", StringComparison.Ordinal));
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

    private static void AssertNoLooseEquality(string source)
    {
        Assert.DoesNotMatch(new Regex(@"(?<![=!])==(?!=)"), source);
        Assert.DoesNotMatch(new Regex(@"(?<!!)!=(?!=)"), source);
    }
}
