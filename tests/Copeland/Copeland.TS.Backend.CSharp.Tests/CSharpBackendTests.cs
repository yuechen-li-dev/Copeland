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
    public void Async_if_control_flow_emits_explicit_state_transition()
    {
        CSharpCompilation compilation = CSharpBackend.Emit(Lower("""
            async function value(flag: boolean): number {
                if (flag) { return 1; }
                return 2;
            }
            """));

        Assert.Empty(compilation.Diagnostics);
        Assert.Contains("frame.State = frame.flag ?", compilation.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void Synchronous_program_does_not_emit_async_runtime()
    {
        CSharpCompilation compilation = CSharpBackend.Emit(Lower("function value(): number { return 1; }"));

        Assert.Empty(compilation.Diagnostics);
        Assert.DoesNotContain("CopeAsync", compilation.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("__CopeAsyncFrame", compilation.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void Pure_class_lowers_to_a_sealed_complete_carrier_and_static_functions()
    {
        var program = Lower("""
            class Person {
                public name: string;
                private normalizedName: string;
                age: number;
                constructor(name: string, age: number): Person {
                    return { name, normalizedName: Person.normalize(name), age };
                }
                private normalize(name: string): string { return name; }
                birthday(person: Person): Person { return person with { age: person.age + 1 }; }
            }
            function main(): number { return Person.birthday(Person("Ada", 41)).age; }
            """);

        CSharpCompilation compilation = CSharpBackend.Emit(program);

        Assert.Contains("public sealed class __CopeRecord_r1", compilation.SourceText, StringComparison.Ordinal);
        Assert.Contains("public string __field_r1_002Ef0 { get; }", compilation.SourceText, StringComparison.Ordinal);
        Assert.Contains("internal string __field_r1_002Ef1 { get; }", compilation.SourceText, StringComparison.Ordinal);
        Assert.Contains("static __CopeRecord_r1 Person__constructor", compilation.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("set;", compilation.SourceText, StringComparison.Ordinal);
        var generated = RoslynCompileHelper.CompileGeneratedSource(compilation.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(42d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
    }

    [Fact]
    public void Transparent_aliases_are_erased_identically_by_both_backends()
    {
        const string aliasSource = """
type Count = number;
type Counts = Count[];
function retain(values: Counts): Counts {
    return values;
}
""";
        const string directSource = """
function retain(values: number[]): number[] {
    return values;
}
""";

        MirProgram aliasProgram = Lower(aliasSource);
        MirProgram directProgram = Lower(directSource);
        var aliasCSharp = CSharpBackend.Emit(aliasProgram).SourceText;
        var directCSharp = CSharpBackend.Emit(directProgram).SourceText;
        var aliasJavaScript = JavaScriptBackend.Emit(aliasProgram).SourceText;
        var directJavaScript = JavaScriptBackend.Emit(directProgram).SourceText;

        Assert.Equal(directCSharp, aliasCSharp);
        Assert.Equal(directJavaScript, aliasJavaScript);
        Assert.DoesNotContain("Count", aliasCSharp, StringComparison.Ordinal);
        Assert.DoesNotContain("Count", aliasJavaScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Control_flow_emits_structured_loops_and_executes_continue_before_for_increment()
    {
        var program = Lower("""
            function main(): number {
                let total: number = 0;
                for (let index: number = 0; index < 5; index = index + 1) {
                    if (index == 2) { continue; }
                    total = total + index;
                }
                while (total < 8) { total = total + 1; }
                return total;
            }
            """);

        CSharpCompilation compilation = CSharpBackend.Emit(program);

        Assert.Empty(compilation.Diagnostics);
        Assert.Contains("for (;", compilation.SourceText, StringComparison.Ordinal);
        Assert.Contains("while (", compilation.SourceText, StringComparison.Ordinal);
        Assert.Contains("continue;", compilation.SourceText, StringComparison.Ordinal);
        var generated = RoslynCompileHelper.CompileGeneratedSource(compilation.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(8d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
    }

    [Fact]
    public void Malformed_loop_transfer_mir_is_rejected_before_csharp_emission()
    {
        var program = new MirProgram([], [
            new MirFunction("main", [], new MirNamedType("void"), [], [new MirContinueStatement()]),
        ]);

        CSharpCompilation compilation = CSharpBackend.Emit(program);

        Assert.Empty(compilation.SourceText);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-CS-0002"
            && diagnostic.Message.Contains("outside a loop", StringComparison.Ordinal));
    }


    [Fact]
    public void Valid_table_mir_emits_a_complete_csharp_artifact()
    {
        var program = Lower("record table Samples { x: [1]; }");

        CSharpCompilation compilation = CSharpBackend.Emit(program);

        Assert.Empty(compilation.Diagnostics);
        Assert.Contains("public sealed class __CopeTable_t1", compilation.SourceText, StringComparison.Ordinal);
        Assert.Contains("private readonly double[] _column_t1_002Ec0;", compilation.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("COPE-CS-TABLE-0001", compilation.SourceText, StringComparison.Ordinal);
        var generated = RoslynCompileHelper.CompileGeneratedSource(compilation.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
    }

    [Fact]
    public void Tables_execute_with_columnar_rows_closed_constants_and_bounds_results()
    {
        var program = Lower("""
            record Point { x: number; }
            enum State { Empty, Value(value: number), }
            record table Values {
                x: [-0, 2];
                name: string = ["zero", "two"];
                enabled: boolean = [true, false];
                point: Point = [{ x: 3 }, { x: 4 }];
                state: State = [State.Value(5), State.Empty];
                result: number ! string = [ok(6), err("bad")];
                nested: State ! string = [ok(State.Value(7)), err("no")];
            }
            function main(): number {
                const row: Values.Row = Values[1]!;
                const value: number ! string = Values.result[0]!;
                return row.x + match value { ok(payload) => payload, err(error) => 0, };
            }
            function invalid(): number {
                return match Values.x[-1] { ok(value) => 0, err(error) => match error { InvalidIndex(index) => 1, OutOfBounds(index, rowCount) => 2, }, };
            }
            function upper(): number {
                return match Values[2] { ok(row) => 0, err(error) => match error { InvalidIndex(index) => 1, OutOfBounds(index, rowCount) => rowCount, }, };
            }
            function negativeZero(): number {
                return Values.x[0]!;
            }
            """);

        CSharpCompilation compilation = CSharpBackend.Emit(program);

        Assert.Empty(compilation.Diagnostics);
        Assert.Contains("private readonly double[] _column_t1_002Ec0;", compilation.SourceText, StringComparison.Ordinal);
        Assert.Contains("CopeResult<double, TableBoundsError>.Err(new TableBoundsError.InvalidIndex(index))", compilation.SourceText, StringComparison.Ordinal);
        Assert.Contains("new __CopeRecord_r1(3.0)", compilation.SourceText, StringComparison.Ordinal);
        Assert.Contains("CopeResult<State, string>.Ok", compilation.SourceText, StringComparison.Ordinal);
        var generated = RoslynCompileHelper.CompileGeneratedSource(compilation.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(8d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
        Assert.Equal(2d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "invalid")));
        Assert.Equal(2d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "upper")));
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "negativeZero"))));
    }

    [Fact]
    public void Empty_and_same_shaped_tables_remain_nominal_and_immutable()
    {
        var program = Lower("""
            record table Empty { value: number = []; }
            record table First { value: [1]; }
            record table Second { value: [1]; }
            function first(): First { return First; }
            function again(): First { return First; }
            function empty(): number {
                return match Empty[0] { ok(row) => 1, err(error) => match error { InvalidIndex(index) => 2, OutOfBounds(index, rowCount) => rowCount, }, };
            }
            """);

        CSharpCompilation compilation = CSharpBackend.Emit(program);

        Assert.Empty(compilation.Diagnostics);
        Assert.Contains("public sealed class __CopeTable_t2", compilation.SourceText, StringComparison.Ordinal);
        Assert.Contains("public sealed class __CopeTable_t3", compilation.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain(" set;", compilation.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("public double[]", compilation.SourceText, StringComparison.Ordinal);
        var generated = RoslynCompileHelper.CompileGeneratedSource(compilation.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Same(
            GeneratedModuleInvoker.Invoke(generated.Assembly!, "first"),
            GeneratedModuleInvoker.Invoke(generated.Assembly!, "again"));
        Assert.Equal(0d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "empty")));
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

    [Fact]
    public void Malformed_table_row_field_identity_is_rejected_by_shared_validation()
    {
        var number = new MirNamedType("number");
        var tableId = new MirTableId("t1");
        var table = new MirTableDefinition(
            tableId,
            "Values",
            "t1.row",
            [new MirTableColumnDefinition(new MirTableColumnId("t1.c0"), "value", number, [new MirTableLiteralConstant(1, number)])],
            1);
        var row = new MirTableRowType("t1.row", "Values.Row");
        var access = new MirTableRowFieldAccessExpression(
            new MirVariableExpression("row", row),
            "t1.row",
            "t1.c9.f",
            number);
        var program = new MirProgram(
            [new MirEnum("TableBoundsError", [
                new MirEnumCase("InvalidIndex", [new MirEnumPayloadField("index", number)]),
                new MirEnumCase("OutOfBounds", [
                    new MirEnumPayloadField("index", number),
                    new MirEnumPayloadField("rowCount", number),
                ]),
            ])],
            [],
            [table],
            [new MirFunction("main", [], number, [], [new MirReturnStatement(access)])]);

        CSharpCompilation compilation = CSharpBackend.Emit(program);

        Assert.Empty(compilation.SourceText);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-CS-0002"
            && diagnostic.Message.Contains("unknown field identity", StringComparison.Ordinal));
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
        Assert.True(mir.Program is not null, string.Join(Environment.NewLine, mir.Diagnostics.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}")));
        return mir.Program!;
    }
}
