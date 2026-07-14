using System.Text.RegularExpressions;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Lowering;
using Copeland.TS.Mir;
using Copeland.TS.Syntax;
using Copeland.TS.Backend.CSharp.Tests.Runtime;
using Copeland.TS.TestSupport;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class CSharpBackendTests
{
    [Fact]
    public void Valid_table_mir_is_rejected_without_a_partial_artifact()
    {
        var program = Lower("record table Samples { x: [1]; }");

        CSharpCompilation compilation = CSharpBackend.Emit(program);

        Assert.Empty(compilation.SourceText);
        Assert.Collection(compilation.Diagnostics, diagnostic => Assert.Equal("COPE-CS-TABLE-0001", diagnostic.Id));
    }

    [Fact]
    public void Malformed_table_mir_is_rejected_by_shared_validation_before_backend_table_rejection()
    {
        MirProgram program = CreateMalformedTableProgram();

        CSharpCompilation compilation = CSharpBackend.Emit(program);

        Assert.Empty(compilation.SourceText);
        Assert.NotEmpty(compilation.Diagnostics);
        Assert.All(compilation.Diagnostics, diagnostic => Assert.Equal("COPE-CS-0002", diagnostic.Id));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Message.Contains("not a supported closed constant", StringComparison.Ordinal));
        Assert.DoesNotContain(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-CS-TABLE-0001");
    }

    [Theory]
    [MemberData(nameof(TableMirValidationCases.Cases), MemberType = typeof(TableMirValidationCases))]
    public void Every_malformed_table_constant_is_rejected_before_csharp_table_backend_dispatch(
        string _,
        MirProgram program,
        string expectedMessage)
    {
        CSharpCompilation compilation = CSharpBackend.Emit(program);

        Assert.Empty(compilation.SourceText);
        Assert.NotEmpty(compilation.Diagnostics);
        Assert.All(compilation.Diagnostics, diagnostic => Assert.Equal("COPE-CS-0002", diagnostic.Id));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Message.Contains(expectedMessage, StringComparison.Ordinal));
        Assert.DoesNotContain(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-CS-TABLE-0001");
    }

    [Fact]
    public void Emits_record_mir_deterministically()
    {
        var program = Lower("record Point { x: number; } function main(): Point { return { x: 1 }; }");

        var first = CSharpBackend.Emit(program);
        var second = CSharpBackend.Emit(program);

        Assert.Empty(first.Diagnostics);
        Assert.Equal(first.SourceText, second.SourceText);
        Assert.Contains("public sealed class __CopeRecord_r1", first.SourceText, StringComparison.Ordinal);
        Assert.Contains("internal double __field_r1_002Ef0 { get; }", first.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("record __CopeRecord", first.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain(" with ", first.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("Equals(", first.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("GetHashCode(", first.SourceText, StringComparison.Ordinal);

        var generated = RoslynCompileHelper.CompileGeneratedSource(first.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
    }

    [Fact]
    public void Record_Support_Is_Demand_Driven_And_Does_Not_Pull_In_Result_Helpers()
    {
        string withoutRecords = Emit("function main(): number { return 42; }");
        string recordsOnly = Emit(
            "record Point { x: number; } function main(): Point { return { x: 42 }; }");

        Assert.DoesNotContain("__CopeRecord_", withoutRecords, StringComparison.Ordinal);
        Assert.Contains("__CopeRecord_r1", recordsOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("CopeResult<", recordsOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("CopeUnit", recordsOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("COPE-PANIC-UNWRAP", recordsOnly, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_record_mir_is_rejected_before_emission()
    {
        var unknownRecord = new MirRecordType(new MirRecordTypeId("missing"), "Missing");
        var program = new MirProgram(
            [],
            [],
            [new MirFunction("main", [], unknownRecord, [], [new MirReturnStatement(new MirVariableExpression("value", unknownRecord))])]);

        CSharpCompilation compilation = CSharpBackend.Emit(program);

        Assert.Empty(compilation.SourceText);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Message.Contains("has no definition", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_record_field_identity_is_rejected_before_emission()
    {
        var recordId = new MirRecordTypeId("r1");
        var knownFieldId = new MirRecordFieldId("r1.f0");
        var unknownFieldId = new MirRecordFieldId("r1.f9");
        var number = new MirNamedType("number");
        var recordType = new MirRecordType(recordId, "Point");
        var definition = new MirRecordDefinition(
            recordId,
            "Point",
            [new MirRecordFieldDefinition(knownFieldId, "x", number)]);
        var receiver = new MirRecordConstructionExpression(
            recordId,
            [new MirRecordFieldValue(knownFieldId, new MirLiteralExpression(1, number))],
            recordType);
        var access = new MirRecordFieldAccessExpression(receiver, recordId, unknownFieldId, number);
        var program = new MirProgram(
            [],
            [definition],
            [new MirFunction("main", [], number, [], [new MirReturnStatement(access)])]);

        CSharpCompilation compilation = CSharpBackend.Emit(program);

        Assert.Empty(compilation.SourceText);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Message.Contains("unknown field identity", StringComparison.Ordinal));
    }

    [Fact]
    public void Shared_Record_Mir_Closeout_Matrix_Rejects_Before_Either_Backend_Emits()
    {
        var number = new MirNamedType("number");
        var text = new MirNamedType("string");
        var firstId = new MirRecordTypeId("r1");
        var secondId = new MirRecordTypeId("r2");
        var missingId = new MirRecordTypeId("missing");
        var xId = new MirRecordFieldId("r1.f0");
        var yId = new MirRecordFieldId("r1.f1");
        var otherXId = new MirRecordFieldId("r2.f0");
        var unknownFieldId = new MirRecordFieldId("r1.f9");
        var firstType = new MirRecordType(firstId, "First");
        var secondType = new MirRecordType(secondId, "Second");
        var missingType = new MirRecordType(missingId, "Missing");
        var first = new MirRecordDefinition(firstId, "First", [
            new MirRecordFieldDefinition(xId, "x", number),
            new MirRecordFieldDefinition(yId, "y", number),
        ]);
        var second = new MirRecordDefinition(secondId, "Second", [
            new MirRecordFieldDefinition(otherXId, "x", number),
        ]);

        MirRecordConstructionExpression CompleteFirst() => new(
            firstId,
            [
                new MirRecordFieldValue(xId, new MirLiteralExpression(1, number)),
                new MirRecordFieldValue(yId, new MirLiteralExpression(2, number)),
            ],
            firstType);

        MirRecordConstructionExpression CompleteSecond() => new(
            secondId,
            [new MirRecordFieldValue(otherXId, new MirLiteralExpression(1, number))],
            secondType);

        MirProgram Returning(
            MirExpression expression,
            IReadOnlyList<MirRecordDefinition>? records = null,
            IReadOnlyList<MirEnum>? enums = null)
            => new(
                enums ?? [],
                records ?? [first, second],
                [new MirFunction("main", [], expression.Type, [], [new MirReturnStatement(expression)])]);

        var cases = new (string Expected, MirProgram Program)[]
        {
            (
                "has no definition",
                new MirProgram([], [], [
                    new MirFunction("main", [], new MirResultType(missingType, text), [], []),
                ])),
            (
                "enum 'Envelope' payload",
                new MirProgram(
                    [new MirEnum("Envelope", [new MirEnumCase("Value", [new MirEnumPayloadField("value", missingType)])])],
                    [],
                    [])),
            (
                "Duplicate record identity",
                new MirProgram([], [first, new MirRecordDefinition(firstId, "Other", [])], [])),
            (
                "duplicate field name",
                new MirProgram([], [new MirRecordDefinition(firstId, "First", [
                    new MirRecordFieldDefinition(xId, "x", number),
                    new MirRecordFieldDefinition(yId, "x", number),
                ])], [])),
            (
                "missing field identity",
                Returning(new MirRecordConstructionExpression(
                    firstId,
                    [new MirRecordFieldValue(xId, new MirLiteralExpression(1, number))],
                    firstType))),
            (
                "unknown field identity",
                Returning(new MirRecordConstructionExpression(
                    firstId,
                    [
                        new MirRecordFieldValue(xId, new MirLiteralExpression(1, number)),
                        new MirRecordFieldValue(yId, new MirLiteralExpression(2, number)),
                        new MirRecordFieldValue(unknownFieldId, new MirLiteralExpression(3, number)),
                    ],
                    firstType))),
            (
                "duplicates field identity",
                Returning(new MirRecordConstructionExpression(
                    firstId,
                    [
                        new MirRecordFieldValue(xId, new MirLiteralExpression(1, number)),
                        new MirRecordFieldValue(xId, new MirLiteralExpression(2, number)),
                    ],
                    firstType))),
            (
                "value type does not match",
                Returning(new MirRecordConstructionExpression(
                    firstId,
                    [
                        new MirRecordFieldValue(xId, new MirLiteralExpression("wrong", text)),
                        new MirRecordFieldValue(yId, new MirLiteralExpression(2, number)),
                    ],
                    firstType))),
            (
                "access receiver type does not match",
                Returning(new MirRecordFieldAccessExpression(CompleteSecond(), firstId, xId, number))),
            (
                "source or result type does not match",
                Returning(new MirRecordWithExpression(
                    CompleteSecond(),
                    firstId,
                    [new MirRecordFieldValue(xId, new MirLiteralExpression(2, number))],
                    firstType))),
            (
                "replacements must not be empty",
                Returning(new MirRecordWithExpression(CompleteFirst(), firstId, [], firstType))),
            (
                "Recursive record definition",
                new MirProgram([], [new MirRecordDefinition(firstId, "First", [
                    new MirRecordFieldDefinition(xId, "self", firstType),
                ])], [])),
        };

        foreach ((string expected, MirProgram program) in cases)
        {
            CSharpCompilation csharp = CSharpBackend.Emit(program);
            JavaScriptCompilation javaScript = JavaScriptBackend.Emit(program);

            Assert.Empty(csharp.SourceText);
            Assert.Null(javaScript.SourceText);
            Assert.Contains(csharp.Diagnostics, diagnostic =>
                diagnostic.Message.Contains(expected, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(javaScript.Diagnostics, diagnostic =>
                diagnostic.Message.Contains(expected, StringComparison.OrdinalIgnoreCase));
            Assert.All(csharp.Diagnostics, diagnostic => Assert.Equal("COPE-CS-0002", diagnostic.Id));
            Assert.All(javaScript.Diagnostics, diagnostic => Assert.Equal("COPE-JS-0002", diagnostic.Id));
        }
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

    private static string Emit(string source) => CSharpBackend.Emit(Lower(source)).SourceText;

    private static MirProgram Lower(string source)
    {
        var mir = MirLowerer.Lower(SyntaxTree.Parse(source));
        Assert.NotNull(mir.Program);
        return mir.Program!;
    }
}
