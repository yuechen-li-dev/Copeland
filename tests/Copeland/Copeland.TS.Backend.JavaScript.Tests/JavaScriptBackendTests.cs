using System.Globalization;
using System.Text.RegularExpressions;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Lowering;
using Copeland.TS.Mir;
using Copeland.TS.Syntax;
using Copeland.TS.TestSupport;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class JavaScriptBackendTests
{
    [Fact]
    public void Valid_table_mir_emits_private_nominal_columnar_runtime()
    {
        MirProgram program = Lower("record table Samples { x: [1]; }");

        JavaScriptCompilation compilation = JavaScriptBackend.Emit(program);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains("Symbol(\"t1\")", compilation.SourceText, StringComparison.Ordinal);
        Assert.Contains("Object.freeze([1])", compilation.SourceText, StringComparison.Ordinal);
        Assert.Contains("Number.isFinite(index)", compilation.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("COPE-JS-TABLE-0001", compilation.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_table_mir_is_rejected_by_shared_validation_before_backend_realization()
    {
        MirProgram program = CreateMalformedTableProgram();

        JavaScriptCompilation compilation = JavaScriptBackend.Emit(program);

        Assert.False(compilation.Success);
        Assert.Null(compilation.SourceText);
        Assert.NotEmpty(compilation.Diagnostics);
        Assert.All(compilation.Diagnostics, diagnostic => Assert.Equal("COPE-JS-0002", diagnostic.Id));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Message.Contains("not a supported closed constant", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(TableMirValidationCases.Cases), MemberType = typeof(TableMirValidationCases))]
    public void Every_malformed_table_constant_is_rejected_before_javascript_table_realization(
        string _,
        MirProgram program,
        string expectedMessage)
    {
        JavaScriptCompilation compilation = JavaScriptBackend.Emit(program);

        Assert.False(compilation.Success);
        Assert.Null(compilation.SourceText);
        Assert.NotEmpty(compilation.Diagnostics);
        Assert.All(compilation.Diagnostics, diagnostic => Assert.Equal("COPE-JS-0002", diagnostic.Id));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Message.Contains(expectedMessage, StringComparison.Ordinal));
    }

    [Fact]
    public void Emits_Private_Nominal_Frozen_Record_Representation_Deterministically()
    {
        MirProgram program = Lower("record Point { x: number; } function main(): Point { return { x: 1 }; }");

        JavaScriptCompilation first = JavaScriptBackend.Emit(program);
        JavaScriptCompilation second = JavaScriptBackend.Emit(program);

        Assert.True(first.Success, string.Join(Environment.NewLine, first.Diagnostics));
        Assert.Equal(first.SourceText, second.SourceText);
        Assert.Contains("Symbol(\"r1\")", first.SourceText, StringComparison.Ordinal);
        Assert.Contains("Symbol(\"r1.f0\")", first.SourceText, StringComparison.Ordinal);
        Assert.Contains("Object.create(null)", first.SourceText, StringComparison.Ordinal);
        Assert.Contains("Object.defineProperties", first.SourceText, StringComparison.Ordinal);
        Assert.Contains("writable: false", first.SourceText, StringComparison.Ordinal);
        Assert.Contains("configurable: false", first.SourceText, StringComparison.Ordinal);
        Assert.Contains("return Object.freeze(value);", first.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("class ", first.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("COPE-JS-REC-0001", first.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_Helpers_Are_Demand_Driven_And_Do_Not_Pull_In_Unrelated_Value_Families()
    {
        JavaScriptCompilation withoutRecords = Emit("function main(): number { return 42; }");
        JavaScriptCompilation recordsOnly = Emit(
            "record Point { x: number; } function main(): Point { return { x: 42 }; }");

        Assert.DoesNotContain("_record_", withoutRecords.SourceText, StringComparison.Ordinal);
        Assert.Contains("_record_type_", recordsOnly.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain(".$tag", recordsOnly.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain(".$payload", recordsOnly.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("_flow_", recordsOnly.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void Logical_Short_Circuit_Keeps_A_Statementful_Right_Operand_Inside_The_Selected_Branch()
    {
        JavaScriptCompilation result = Emit("""
            function bad(): boolean ! string { return err("bad"); }
            function main(): boolean { return false && bad()!; }
            """);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        int branchStart = result.SourceText!.IndexOf("if (false) {", StringComparison.Ordinal);
        int badCall = result.SourceText.IndexOf("= bad();", StringComparison.Ordinal);
        Assert.True(branchStart >= 0, result.SourceText);
        Assert.True(badCall > branchStart, result.SourceText);
        Assert.Single(Regex.Matches(result.SourceText, @"= bad\(\);").Cast<Match>());
    }

    [Fact]
    public void Emits_Private_Unwrap_Panic_Only_For_Unwrap()
    {
        JavaScriptCompilation unwrap = Emit("function parse(): number ! string { return err(\"bad\"); } function main(): number { return parse()!; }");
        JavaScriptCompilation ordinaryResult = Emit("function parse(): number ! string { return err(\"bad\"); }");

        Assert.Contains("COPE-PANIC-UNWRAP: Result unwrap encountered err", unwrap.SourceText, StringComparison.Ordinal);
        Assert.Contains("$tag === \"err\"", unwrap.SourceText, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(unwrap.SourceText!, @"= parse\(\);").Cast<Match>());
        Assert.DoesNotContain("COPE-PANIC-UNWRAP", ordinaryResult.SourceText, StringComparison.Ordinal);
    }

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
                new MirFunction("main", [], new MirType("number"), [], [
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
            new MirFunction("main", [], new MirType("string"), [], [
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
                new MirFunction("fallible", [], new MirResultType(number, new MirType("ParseError")), [], []),
                new MirFunction("array", [], new MirArrayType(number), [], [new MirReturnStatement(new MirArrayExpression([], new MirArrayType(number)))]),
                new MirFunction("equality", [], boolean, [], [new MirReturnStatement(new MirBinaryExpression("==", new MirLiteralExpression(1, number), new MirLiteralExpression(1, number), boolean))]),
                new MirFunction("loop", [], number, [], [new MirWhileStatement(new MirLiteralExpression(true, boolean), [])]),
                new MirFunction("assignment", [], number, [], [new MirExpressionStatement(new MirAssignmentExpression("value", new MirLiteralExpression(1, number), number))]),
                new MirFunction("match", [], number, [], [new MirReturnStatement(new MirMatchExpression(new MirVariableExpression("choice", new MirType("Choice")), [], number))]),
                new MirFunction("unknown", [], number, [], [new MirReturnStatement(new MirBinaryExpression("**", new MirLiteralExpression(2, number), new MirLiteralExpression(3, number), number))])
            ]);

        JavaScriptCompilation result = JavaScriptBackend.Emit(program);

        Assert.False(result.Success);
        Assert.Null(result.SourceText);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-JS-0002"
            && diagnostic.Message.Contains("non-exhaustive match", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("ParseError", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("array expression", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("while loop", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Message.Contains("assignment to 'value'", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_Malformed_Record_Mir_Through_Shared_Validation_Without_Partial_Artifact()
    {
        var missingRecord = new MirRecordType(new MirRecordTypeId("missing"), "Missing");
        var program = new MirProgram([], [], [
            new MirFunction(
                "main",
                [],
                missingRecord,
                [],
                [new MirReturnStatement(new MirVariableExpression("value", missingRecord))]),
        ]);

        JavaScriptCompilation result = JavaScriptBackend.Emit(program);

        Assert.False(result.Success);
        Assert.Null(result.SourceText);
        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("COPE-JS-0002", diagnostic.Id));
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Invalid MIR", StringComparison.Ordinal) &&
            diagnostic.Message.Contains("has no definition", StringComparison.Ordinal));
    }

    [Fact]
    public void Stages_Earlier_Arguments_Before_Later_Record_Preludes()
    {
        JavaScriptCompilation result = Emit("""
            record Point { x: number; y: number; }
            function first(): number { return 1; }
            function second(): number { return 2; }
            function consume(value: number, point: Point): number { return value + point.x + point.y; }
            function main(): number { return consume(first(), { y: second(), x: 39 }); }
            """);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        int firstCall = result.SourceText!.IndexOf("= first();", StringComparison.Ordinal);
        int secondCall = result.SourceText.IndexOf("= second();", StringComparison.Ordinal);
        Assert.True(firstCall >= 0, result.SourceText);
        Assert.True(secondCall > firstCall, result.SourceText);
        Assert.Single(Regex.Matches(result.SourceText, @"= first\(\);").Cast<Match>());
        Assert.Single(Regex.Matches(result.SourceText, @"= second\(\);").Cast<Match>());
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
                new MirFunction("main", [], number, [], [
                    new MirReturnStatement(new MirMatchExpression(
                        new MirVariableExpression("choice", choice),
                        [new MirMatchArm("Some", [], new MirLiteralExpression(1, number))],
                        number))]),
                new MirFunction("bad", [], choice, [], [
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
    public void Emits_Result_Operations_With_Deduplicated_Structural_Tokens()
    {
        MirType number = new("number");
        MirType error = new("string");
        MirResultType resultType = new(number, error);
        var program = new MirProgram([], [
            new MirFunction("parse", [], resultType, [], [
                new MirReturnStatement(new MirOkExpression(new MirLiteralExpression(1, number), resultType))]),
            new MirFunction("forward", [new MirParameter("value", new MirResultType(number, error))], resultType, [], [
                new MirReturnStatement(new MirVariableExpression("value", resultType))]),
            new MirFunction("main", [], resultType, [], [
                new MirReturnStatement(new MirOkExpression(
                    new MirPropagateExpression(new MirCallExpression("parse", [], resultType), new MirPropagationTarget.FunctionReturn(), number),
                    resultType))])
        ]);

        JavaScriptCompilation result = JavaScriptBackend.Emit(program);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Single(Regex.Matches(result.SourceText!, @"const __cope_m3_result_type_\d+ =").Cast<Match>());
        Assert.Contains("$tag === \"err\"", result.SourceText, StringComparison.Ordinal);
        Assert.Contains("return __cope_m3_make_", result.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_Structural_Result_Equality_Without_Partial_Artifact()
    {
        MirType number = new("number");
        MirType stringType = new("string");
        MirType boolean = new("boolean");
        MirResultType result = new(number, stringType);
        var program = new MirProgram([], [
            new MirFunction("main", [], boolean, [], [
                new MirReturnStatement(new MirBinaryExpression(
                    "==",
                    new MirVariableExpression("left", result),
                    new MirVariableExpression("right", result),
                    boolean))])
        ]);

        JavaScriptCompilation compilation = JavaScriptBackend.Emit(program);

        Assert.False(compilation.Success);
        Assert.Null(compilation.SourceText);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Message.Contains("equality for type 'number ! string'", StringComparison.Ordinal));
    }

    [Fact]
    public void Emits_Try_Except_Mir_With_Private_Branded_Flow()
    {
        JavaScriptCompilation compilation = Emit("function read(): number ! string { return err(\"bad\"); } function main(): number { return try { read()? } except (error) { 0 }; }");

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains("flow_token", compilation.SourceText, StringComparison.Ordinal);
        Assert.Contains("flow_handler", compilation.SourceText, StringComparison.Ordinal);
        Assert.Matches(@"\$handler === \d+", compilation.SourceText!);
        Assert.DoesNotContain("catch", compilation.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_Invalid_Function_Return_Propagation_At_The_Mir_Boundary()
    {
        MirType number = new("number");
        MirType stringType = new("string");
        MirResultType result = new(number, stringType);
        var propagation = new MirPropagateExpression(
            new MirCallExpression("read", [], result),
            new MirPropagationTarget.FunctionReturn(),
            number);
        var program = new MirProgram([], [
            new MirFunction("main", [], number, [], [new MirReturnStatement(propagation)]),
        ]);

        JavaScriptCompilation compilation = JavaScriptBackend.Emit(program);

        Assert.False(compilation.Success);
        Assert.Null(compilation.SourceText);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Message.Contains(
            "Function-return propagation requires a Result function return type.",
            StringComparison.Ordinal));
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
            new MirFunction("main", [], boolean, [], [
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

    private static MirProgram CreateMalformedTableProgram()
    {
        var number = new MirNamedType("number");
        var table = new MirTableDefinition(
            new MirTableId("t1"),
            "Values",
            "t1.row",
            [new MirTableColumnDefinition(
                new MirTableColumnId("t1.c0"),
                "value",
                number,
                [new MirTableLiteralConstant("wrong", number)])],
            1);
        var boundsError = new MirEnum("TableBoundsError", [
            new MirEnumCase("InvalidIndex", [new MirEnumPayloadField("index", number)]),
            new MirEnumCase("OutOfBounds", [
                new MirEnumPayloadField("index", number),
                new MirEnumPayloadField("rowCount", number),
            ]),
        ]);
        return new MirProgram([boundsError], [], [table], []);
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
