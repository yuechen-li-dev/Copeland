using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Copeland.TS.InstinctCorpus;
using Copeland.TS.Mir;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TableCardinalityTests
{
    [Fact]
    public void Reverse_join_nonuniqueness_is_a_typed_failure_after_an_accepted_insert()
    {
        var compilation = CopelandCompiler.CompileToMir("""
            record table P { key id: int = [1]; }
            record table S { key id: int = [11]; reference parentId: int -> P.id = [1]; }
            record table Plan = derive P as p join S as s through s.parentId { id: int = s.id; }
            record Inputs { p: P; s: S; }
            function run(): int[] {
                const original: Inputs = { p: P, s: S };
                const added: Inputs = Tables.apply(original, { s: [S.insertRow({ id: 12, parentId: 1 })] })!;
                const code: int = match Plan.tryRecalculate({ P: added.p, S: added.s }) {
                    ok(plan) => 0,
                    err(error) => match error { NonUnique(relation) => 42, Unresolved(relation) => 41, },
                };
                return [added.s.id.count(), code, original.s.id.count()];
            }
            """);
        AssertParity(compilation, [2, 42, 1]);
    }

    [Fact]
    public void Empty_authored_tables_can_be_populated_and_string_keys_remain_case_sensitive()
    {
        var compilation = CopelandCompiler.CompileToMir("""
            record table T { key id: string = []; value: int = []; }
            record Inputs { rows: T; }
            function run(): int[] {
                const original: Inputs = { rows: T };
                const added: Inputs = Tables.apply(original, { rows: [T.insertRow({ id: "A", value: 2 }), T.insertRow({ id: "a", value: 3 })] })!;
                const duplicate: int = match Tables.apply(added, { rows: [T.insertRow({ id: "A", value: 9 })] }) {
                    ok(value) => 0, err(error) => 1,
                };
                return [T.value.sum(), added.rows.id.count(), added.rows.value.sum(), duplicate];
            }
            """);
        AssertParity(compilation, [0, 2, 5, 1]);
    }

    [Fact]
    public void Generic_inputs_and_nominal_rows_work_after_emptying_the_snapshot()
    {
        var compilation = CopelandCompiler.CompileToMir("""
            record table T { key id: int = [1]; value: int = [7]; }
            record Inputs<TValue> { metadata: TValue; rows: T; }
            function remove<TValue>(input: Inputs<TValue>): Inputs<TValue> ! TableEditError {
                return Tables.apply(input, { rows: [T.deleteRow({ id: 1 })] });
            }
            function restore<TValue>(input: Inputs<TValue>, row: T.Row): Inputs<TValue> ! TableEditError {
                return Tables.apply(input, { rows: [T.insertRow(row)] });
            }
            function run(): int[] {
                const initial: Inputs<int> = { metadata: 42, rows: T };
                const empty: Inputs<int> = remove<int>(initial)!;
                const restored: Inputs<int> = restore<int>(empty, T[0]!)!;
                let total: int = 0;
                for (const row of restored.rows.rows()) { total = total + row.value; }
                const missing: int = match empty.rows[0] { ok(row) => 0, err(error) => 1, };
                return [empty.rows.id.count(), restored.rows.id.count(), restored.metadata, total, missing];
            }
            """);
        AssertParity(compilation, [0, 1, 42, 7, 1]);
    }

    [Fact]
    public void Malformed_change_mir_cannot_omit_tables_or_forge_fields_types_or_error_cases()
    {
        var compilation = CopelandCompiler.CompileToMir("""
            record table P { key id: int = [1]; value: int = [3]; }
            record Inputs { p: P; }
            function edit(input: Inputs): Inputs ! TableEditError {
                return Tables.apply(input, { p: [P.insertRow({ id: 2, value: 4 })] });
            }
            """);
        Assert.True(compilation.Success);
        var program = compilation.MirCompilation!.Program!;
        var function = program.Functions.Single(item => item.Name == "edit");
        var original = Assert.IsType<MirTableChangesExpression>(Assert.IsType<MirReturnStatement>(function.Body.Single()).Expression);
        var operation = original.Operations.Single();
        MirTableChangesExpression[] corruptions = [
            original with { Type = new MirNamedType("int") },
            original with { Fields = [] },
            original with { Fields = [original.Fields[0], original.Fields[0]] },
            original with { Fields = [original.Fields[0] with { FieldId = new("missing") }] },
            original with { Operations = [operation with { Values = [] }] },
            original with { Operations = [operation with { Row = new MirLiteralExpression(1, new MirNamedType("int")) }] },
            original with { Operations = [operation with { TableId = new("missing") }] },
        ];
        foreach (var expression in corruptions)
        {
            var invalidFunction = new MirFunction(function.Name, function.Parameters, function.ReturnType, function.Locals, [new MirReturnStatement(expression)]);
            var invalid = new MirProgram(program.Enums, program.Records, program.Tables, [invalidFunction]);
            Assert.NotEmpty(MirValidator.Validate(invalid));
            Assert.NotEmpty(CSharpBackend.Emit(invalid).Diagnostics);
        }
        var missingError = new MirProgram(program.Enums.Where(item => item.Name != "TableEditError").ToArray(), program.Records, program.Tables, [function]);
        Assert.NotEmpty(MirValidator.Validate(missingError));
    }

    [Fact]
    public void Generic_constructor_without_arguments_has_diagnostics_instead_of_a_parser_exception()
    {
        var syntax = SyntaxTree.Parse("function run(): int { const invalid: int = new Box<int>; return 1; }");
        Assert.NotEmpty(syntax.Diagnostics);
    }

    [Theory]
    [InlineData("Tables.apply(input, { p: [P.insertRow({ id: 2 })] })", "COPE-CHANGE-0004")]
    [InlineData("Tables.apply(input, { p: [P.insertRow({ id: 2, value: 1, extra: 2 })] })", "COPE-CHANGE-0004")]
    [InlineData("Tables.apply(input, { p: [P.insertRow({ id: \"two\", value: 1 })] })", "COPE-CHANGE-0005")]
    [InlineData("Tables.apply(input, { p: [P.deleteRow({ value: 1 })] })", "COPE-CHANGE-0004")]
    [InlineData("Tables.apply(input, { p: [Other.deleteRow({ id: 1 })] })", "COPE-CHANGE-0003")]
    [InlineData("Tables.apply(input, { missing: [] })", "COPE-CHANGE-0003")]
    [InlineData("Tables.apply(input, { p: [], p: [] })", "COPE-CHANGE-0003")]
    [InlineData("P.insertRow({ id: 2, value: 1 })", "COPE-CHANGE-0003")]
    public void Invalid_change_descriptions_receive_a_source_diagnostic(string expression, string diagnostic)
    {
        var compilation = CopelandCompiler.CompileToMir("""
            record table P { key id: int = [1]; value: int = [3]; }
            record table Other { key id: int = [1]; }
            record Inputs { p: P; }
            function test(input: Inputs): Inputs ! TableEditError {
            """ + "return " + expression + "; }");
        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, item => item.Id == diagnostic);
    }

    [Fact]
    public void Missing_reference_targets_and_ambiguous_snapshot_ownership_are_rejected()
    {
        foreach (string fields in new[] { "s: S;", "p: P; s: S; another: P;" })
        {
            var compilation = CopelandCompiler.CompileToMir("""
                record table P { key id: int = [1]; }
                record table S { key reference id: int -> P.id = [1]; }
                """ + "record Inputs { " + fields + " }" + """
                function test(input: Inputs): Inputs ! TableEditError { return Tables.apply(input, {}); }
                """);
            Assert.False(compilation.Success);
            Assert.Contains(compilation.Diagnostics, item => item.Id == "COPE-CHANGE-0002");
        }
    }

    [Fact]
    public void Operands_evaluate_once_in_written_order_even_for_a_rejected_batch()
    {
        var compilation = CopelandCompiler.CompileToMir("""
            record table P { key id: int = [3]; value: int = [10]; }
            record Inputs { p: P; }
            function stamp(trace: MutableArray<int>, value: int): int {
                trace[0] = trace[0] * 10 + value;
                return value;
            }
            function input(trace: MutableArray<int>): Inputs {
                const ignored: int = stamp(trace, 1);
                return { p: P };
            }
            function run(): int[] {
                const trace: MutableArray<int> = MutableArray<int>(1, 0);
                const result: Inputs ! TableEditError = Tables.apply(input(trace), {
                    p: [P.insertRow({ value: stamp(trace, 2), id: stamp(trace, 3) }),
                        P.deleteRow({ id: stamp(trace, 4) })],
                });
                const code: int = match result {
                    ok(accepted) => 0,
                    err(error) => match error {
                        DuplicateKey(tableName, keyValue) => 1, NotFound(tableName, keyValue) => 2,
                        ConflictingEdit(tableName, keyValue) => 3, MissingReference(tableName, columnName, keyValue) => 4, RowLimit(tableName) => 5,
                    },
                };
                return [trace[0], code, P.value.sum()];
            }
            """);
        AssertParity(compilation, [1234, 1, 10]);
    }

    [Fact]
    public void Checked_recalculation_reports_reverse_join_coverage_without_swallowing_other_failures()
    {
        var compilation = CopelandCompiler.CompileToMir("""
            record table P { key id: int = [1]; }
            record table S { key reference id: int -> P.id = [1]; units: int = [3]; }
            record table Plan = derive P as p join S as s through s.id { units: int = s.units; }
            record Inputs { p: P; s: S; }
            function code(value: Plan ! TableRelationError): int {
                return match value {
                    ok(plan) => plan.units.sum(),
                    err(error) => match error { Unresolved(relation) => 41, NonUnique(relation) => 42, },
                };
            }
            function run(): int[] {
                const inputs: Inputs = { p: P, s: S };
                const incomplete: Inputs = Tables.apply(inputs, { s: [S.deleteRow({ id: 1 })] })!;
                const empty: Inputs = Tables.apply(incomplete, { p: [P.deleteRow({ id: 1 })] })!;
                return [code(Plan.tryRecalculate({ P: inputs.p, S: inputs.s })),
                    code(Plan.tryRecalculate({ P: incomplete.p, S: incomplete.s })),
                    code(Plan.tryRecalculate({ P: empty.p, S: empty.s }))];
            }
            """);
        AssertParity(compilation, [3, 41, 0]);
    }

    [Fact]
    public void Atomic_row_changes_preserve_keys_references_order_and_empty_execution()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            record table Products { key id: int = [1, 2]; price: int = [10, 20]; }
            record table Stock { key reference id: int -> Products.id = [2, 1]; units: int = [3, 4]; }
            record table Plan = derive Products as product join Stock as stock through stock.id {
                id: int = product.id;
                total: int = product.price * stock.units;
            }
            record Inputs { products: Products; stock: Stock; label: string; }
            function initial(): Inputs { return { products: Products, stock: Stock, label: "preserved" }; }
            function refused(value: Inputs ! TableEditError): int {
                return match value { ok(accepted) => 0, err(error) => 1, };
            }
            function add(inputs: Inputs, id: int): Inputs ! TableEditError {
                return Tables.apply(inputs, {
                    stock: [Stock.insertRow({ id, units: 5 })],
                    products: [Products.insertRow({ id, price: 30 })],
                });
            }
            function remove(inputs: Inputs, id: int): Inputs ! TableEditError {
                return Tables.apply(inputs, {
                    products: [Products.deleteRow({ id })],
                    stock: [Stock.deleteRow({ id })],
                });
            }
            function total(inputs: Inputs): int {
                return Plan.recalculate({ Products: inputs.products, Stock: inputs.stock }).total.sum();
            }
            function run(): int[] {
                const original: Inputs = initial();
                const added: Inputs = add(original, 3)!;
                const patched: Products = added.products.withRow({ id: 3 }, { price: 40 })!;
                const duplicate: int = refused(add(added, 3));
                const missing: int = refused(remove(added, 9));
                const referenced: int = refused(Tables.apply(added, { products: [Products.deleteRow({ id: 1 })] }));
                const dangling: int = refused(Tables.apply(added, { stock: [Stock.insertRow({ id: 9, units: 2 })] }));
                const conflicting: int = refused(Tables.apply(added, { products: [Products.deleteRow({ id: 3 }), Products.insertRow({ id: 3, price: 99 })] }));
                const reduced: Inputs = remove(added, 2)!;
                const empty: Inputs = remove(remove(reduced, 1)!, 3)!;
                const restored: Inputs = add(empty, 7)!;
                const labels: int = if restored.label == "preserved" { 1 } else { 0 };
                const projected: int[] = reduced.products.rows().select(row => row.id);
                return [original.products.id.count(), added.products.id.count(), total(original), total(added),
                    duplicate, missing, referenced, dangling, conflicting, patched.price.sum(),
                    projected[0], projected[1], empty.products.id.count(), empty.products.price.sum(),
                    total(empty), restored.products.id.count(), total(restored), labels, total(original)];
            }
            """);
        AssertParity(compilation, [2, 3, 100, 250, 1, 1, 1, 1, 1, 70, 1, 3, 0, 0, 0, 1, 150, 1, 100]);
    }

    private static void AssertParity(CopelandCompilation compilation, int[] expected)
    {
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        var csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(csharp.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(csharp.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        Assert.Equal(expected, (int[])module.GetMethod("run")!.Invoke(null, null)!);
        TableJavaScriptParity.AssertRun(compilation, expected);
    }
}
