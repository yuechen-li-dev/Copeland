using Copeland.TS.Compiler;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Mir;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TableFeatureTests
{
    private const string Program = """
        record table Samples {
            x: number = [1, 2];
            label: string = ["a", "b"];
        }

        record table Empty {
            x: number = [];
        }

        function row(index: number): Samples.Row ! TableBoundsError {
            return Samples[index];
        }

        function readColumn(index: number): number ! TableBoundsError {
            return Samples.x[index];
        }

        function field(index: number): number ! TableBoundsError {
            const value: Samples.Row = Samples[index]?;
            return ok(value.x);
        }
        """;

    [Fact]
    public void Parser_preserves_table_declarations_types_and_postfix_access()
    {
        SyntaxTree tree = SyntaxTree.Parse(Program);

        Assert.Empty(tree.Diagnostics);
        var table = Assert.IsType<TableDeclarationSyntax>(tree.Root.Members[0]);
        Assert.Equal(["x", "label"], table.Columns.Select(column => column.Identifier.Text));
        Assert.IsType<PredefinedTypeSyntax>(table.Columns[0].ExplicitType);
        Assert.IsType<PredefinedTypeSyntax>(table.Columns[1].ExplicitType);
        var row = Assert.IsType<FunctionDeclarationSyntax>(tree.Root.Members[2]);
        Assert.IsType<QualifiedRowTypeSyntax>(((ResultTypeSyntax)row.ReturnType!).SuccessType);
        var column = Assert.IsType<FunctionDeclarationSyntax>(tree.Root.Members[3]);
        var returnStatement = Assert.IsType<ReturnStatementSyntax>(column.Body.Statements[0]);
        Assert.IsType<IndexExpressionSyntax>(returnStatement.Expression);
    }

    [Fact]
    public void Binder_preserves_stable_nominal_table_row_and_column_identities()
    {
        var compilation = CopelandCompiler.CompileToMir(Program);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        BoundProgram program = compilation.BoundCompilation!.Program;
        Assert.Equal(["t1", "t2"], program.Tables.Select(table => table.TableType.Id.ToString()));
        BoundTableDefinition samples = program.Tables[0];
        Assert.Equal("t1", samples.TableType.Id.ToString());
        Assert.Equal("t1.row", samples.TableType.RowType.TableId + ".row");
        Assert.Equal(["t1.c0", "t1.c1"], samples.Columns.Select(column => column.Column.Id.ToString()));
        Assert.Equal(2, samples.RowCount);
        Assert.Empty(program.Tables[1].Columns[0].Cells);

        var rowReturn = Assert.IsType<BoundReturnStatement>(program.Functions[0].Body.Statements[0]);
        Assert.IsType<BoundTableRowAccessExpression>(rowReturn.Expression);
        var columnReturn = Assert.IsType<BoundReturnStatement>(program.Functions[1].Body.Statements[0]);
        Assert.IsType<BoundColumnElementAccessExpression>(columnReturn.Expression);
        var fieldDeclaration = Assert.IsType<BoundVariableDeclaration>(program.Functions[2].Body.Statements[0]);
        Assert.IsType<BoundPropagateExpression>(fieldDeclaration.Initializer);
    }

    [Fact]
    public void Mir_text_is_deterministic_and_displays_table_contract()
    {
        var first = CopelandCompiler.CompileToMir(Program);
        var second = CopelandCompiler.CompileToMir(Program);

        Assert.True(first.Success, string.Join(Environment.NewLine, first.Diagnostics));
        Assert.Equal(first.MirText, second.MirText);
        Assert.Contains("table Samples [t1] row [t1.row] count 2", first.MirText, StringComparison.Ordinal);
        Assert.Contains("column x [t1.c0]: number = [1, 2]", first.MirText, StringComparison.Ordinal);
        Assert.Contains("table-row [t1] table-ref [t1][index]", first.MirText, StringComparison.Ordinal);
        Assert.Contains("column-element table-column [t1] table-ref [t1].[t1.c0][index]", first.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Representative_table_mir_has_a_stable_byte_length_and_sha256()
    {
        string source = LanguageFixtures.ReadSourceText("Valid/tables/constants-and-access.cl-valid.ts");
        CopelandCompilation first = CopelandCompiler.CompileToMir(source);
        CopelandCompilation second = CopelandCompiler.CompileToMir(source);

        Assert.True(first.Success, string.Join(Environment.NewLine, first.Diagnostics));
        Assert.Equal(first.MirText, second.MirText);
        byte[] bytes = Encoding.UTF8.GetBytes(first.MirText!);
        Assert.Equal(1661, bytes.Length);
        Assert.Equal(
            "62897D4142128179A9036545CBA4A0BDB4E3EB74ACF9D722E71E90A0EF93234F",
            Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [Fact]
    public void Table_cells_lower_to_closed_mir_constants_not_executable_expressions()
    {
        var compilation = CopelandCompiler.CompileToMir("record table Values { enabled: [true]; label: [\"ok\"]; count: [-0]; }");

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        MirTableDefinition table = Assert.Single(compilation.MirCompilation!.Program!.Tables);
        Assert.All(table.Columns.SelectMany(column => column.Constants), constant => Assert.IsAssignableFrom<MirTableConstant>(constant));
        Assert.All(table.Columns.SelectMany(column => column.Constants), constant => Assert.IsNotAssignableFrom<MirExpression>(constant));
        Assert.IsType<MirTableLiteralConstant>(table.Columns[0].Constants[0]);
    }

    [Fact]
    public void Derived_tables_bind_exact_projection_schema_and_relation_plan()
    {
        const string source = """
            record table Prices {
                id: int = [1, 2];
                retail: number = [10, 20];
                cost: number = [4, 7];
            }
            record table Margins = derive Prices as price {
                productId: int = price.id;
                margin: number = price.retail - price.cost;
            }
            function total(): number { return Margins.margin.sum(); }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        BoundDerivedTableDefinition derived = Assert.IsType<BoundDerivedTableDefinition>(compilation.BoundCompilation!.Program.Tables[1]);
        Assert.Equal("Prices", derived.SourceTable.Name);
        Assert.Equal(["productId", "margin"], derived.Projections.Select(projection => projection.Column.Name));
        Assert.Equal(["cost", "retail"], derived.Projections[1].SourceColumns);
        MirTableDefinition mir = compilation.MirCompilation!.Program!.Tables[1];
        Assert.NotNull(mir.DerivedPlan);
        Assert.Equal("t1", mir.DerivedPlan!.SourceTableId.Value);
        Assert.All(mir.Columns, column => Assert.Empty(column.Constants));
        Assert.Contains("derived source [t1] alias price", compilation.MirText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("record table T = derive Missing as row { x: int = row.x; }", "COPE-DERIVE-0001")]
    [InlineData("record table A = derive B as row { x: int = row.x; } record table B = derive A as row { x: int = row.x; }", "COPE-DERIVE-0008")]
    [InlineData("record table T { x: int = [1]; } record table U = derive T as row { x: int = row.x; x: int = row.x; }", "COPE-DERIVE-0003")]
    [InlineData("record table T { x: int = [1]; } record table U = derive T as row { x: int = row.x; } function revise(): U { return U with { x: [2] }; }", "COPE-DERIVE-0009")]
    public void Derived_tables_report_source_cycle_and_schema_diagnostics(string source, string diagnosticId)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Fact]
    public void JavaScript_reports_the_explicit_derived_table_materializer_boundary()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            record table Source { value: int = [1]; }
            record table Projection = derive Source as row { value: int = row.value; }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation javascript = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Contains(javascript.Diagnostics, diagnostic => diagnostic.Id == "COPE-JS-DERIVE-0001");
    }

    [Fact]
    public void Tables_bind_single_column_keys_and_typed_references()
    {
        const string source = """
            record table Categories {
                key id: int = [10, 20];
                name: string = ["Coffee", "Tea"];
            }

            record table Products {
                key id: int = [100, 101];
                reference categoryId: int -> Categories.id = [10, 20];
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        BoundTableDefinition categories = compilation.BoundCompilation!.Program.Tables[0];
        BoundTableDefinition products = compilation.BoundCompilation.Program.Tables[1];
        Assert.Equal("id", categories.TableType.KeyColumn!.Name);
        TableReferenceSymbol reference = Assert.IsType<TableReferenceSymbol>(products.Columns[1].Column.Reference);
        Assert.Equal("Categories", reference.TargetTable.Name);
        Assert.Equal("id", reference.TargetKey.Name);

        MirTableDefinition productsMir = compilation.MirCompilation!.Program!.Tables[1];
        Assert.Equal("t2.c0", productsMir.KeyColumnId!.Value.Value);
        Assert.Equal("t1", productsMir.Columns[1].Reference!.TargetTableId.Value);
        Assert.Equal("t1.c0", productsMir.Columns[1].Reference!.TargetKeyColumnId.Value);
    }

    [Theory]
    [InlineData("record table T { key id: int = [1, 1]; }", "COPE-TABLE-0037")]
    [InlineData("record table T { key id: int = [1, 2]; } function revised(): T { return T with { id: [1, 1] }; }", "COPE-TABLE-0037")]
    [InlineData("record table T { key id: number = [1]; }", "COPE-TABLE-0031")]
    [InlineData("record table T { reference id: int -> Missing.id = [1]; }", "COPE-TABLE-0033")]
    [InlineData("record table T { key id: int = [1]; } record table U { reference id: int -> T.missing = [1]; }", "COPE-TABLE-0034")]
    [InlineData("record table T { id: int = [1]; } record table U { reference id: int -> T.id = [1]; }", "COPE-TABLE-0035")]
    [InlineData("record table T { key id: string = [\"a\"]; } record table U { reference id: int -> T.id = [1]; }", "COPE-TABLE-0036")]
    [InlineData("record table T { key id: int = [1]; } record table U { reference id: int -> T.id = [2]; }", "COPE-TABLE-0038")]
    public void Tables_report_key_and_reference_constraint_diagnostics(string source, string diagnosticId)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Fact]
    public void Closed_table_constants_accept_primitives_aggregates_and_results()
    {
        var compilation = CopelandCompiler.CompileToMir("""
            record Nested { value: number; }
            record Envelope { nested: Nested; }
            enum Choice { Empty, Value(value: number), }
            record table Constants {
                enabled: [true];
                label: ["text"];
                positive: [1];
                negative: [-1];
                negativeZero: number = [-0.0];
                empty: [Choice.Empty];
                payload: [Choice.Value(2)];
                contextual: Envelope = [{ nested: { value: 3 } }];
                nested: Nested = [{ value: 4 }];
                succeeded: number ! string = [ok(5)];
                failed: number ! string = [err("bad")];
            }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        MirTableDefinition table = Assert.Single(compilation.MirCompilation!.Program!.Tables);
        Assert.Equal(11, table.Columns.Count);
        Assert.IsType<MirTableEnumConstant>(table.Columns[5].Constants[0]);
        Assert.IsType<MirTableRecordConstant>(table.Columns[7].Constants[0]);
        Assert.All(table.Columns[9].Constants, constant => Assert.IsType<MirTableResultConstant>(constant));
        Assert.All(table.Columns[10].Constants, constant => Assert.IsType<MirTableResultConstant>(constant));
        Assert.Contains("-0", compilation.MirText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("function value(): number { return 1; } record table T { x: [value()]; }", "COPE-TABLE-0009")]
    [InlineData("const value: number = 1; record table T { x: [value]; }", "COPE-TABLE-0009")]
    [InlineData("record table Other { x: [1]; } record table T { x: [Other]; }", "COPE-TABLE-0009")]
    [InlineData("record table Other { x: [1]; } record table T { x: [Other.x]; }", "COPE-TABLE-0009")]
    [InlineData("record table Other { x: [1]; } record table T { x: [Other[0]]; }", "COPE-TABLE-0009")]
    [InlineData("record table T { x: [1 + 1]; }", "COPE-TABLE-0009")]
    [InlineData("record table T { x: [true && false]; }", "COPE-TABLE-0009")]
    [InlineData("record table T { x: [if true { 1 } else { 2 }]; }", "COPE-TABLE-0009")]
    [InlineData("enum E { A, } record table T { x: [match E.A { A => 1, }]; }", "COPE-TABLE-0009")]
    [InlineData("function read(): number ! string { return ok(1); } record table T { x: [try { const value: number = read()?; value } except (error) { 0 }]; }", "COPE-TABLE-0009")]
    [InlineData("function read(): number ! string { return ok(1); } record table T { x: [read()!]; }", "COPE-TABLE-0009")]
    [InlineData("let value: number = 1; record table T { x: [value = 2]; }", "COPE-TABLE-0009")]
    [InlineData("record Point { x: number; } function point(): Point { return { x: 1 }; } record table T { x: Point = [point() with { x: 2 }]; }", "COPE-TABLE-0009")]
    [InlineData("record table T { x: number[] = [[1]]; }", "COPE-TABLE-0009")]
    [InlineData("record R { values: number[]; } record table T { x: R = [{ values: [1] }]; }", "COPE-TABLE-0009")]
    [InlineData("enum E { Values(values: number[]), } record table T { x: E = [E.Values([1])]; }", "COPE-TABLE-0009")]
    [InlineData("record table T { x: (number[] ! string) = [ok([1])]; }", "COPE-TABLE-0009")]
    [InlineData("record table T { x: void ! string = []; }", "COPE-TABLE-0009")]
    [InlineData("record R { x: number; } enum E { Empty, } record table T { value: R = [E.Empty]; }", "COPE-TABLE-0007")]
    public void Table_cells_reject_executable_mutable_and_wrong_contextual_values(string source, string diagnosticId)
    {
        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Theory]
    [InlineData("record table Empty { }", "COPE-TABLE-0003")]
    [InlineData("record table Empty { x: []; }", "COPE-TABLE-0005")]
    [InlineData("record table Ragged { x: [1, 2]; y: [1]; }", "COPE-TABLE-0008")]
    [InlineData("function f(): void { record table Nested { x: [1]; } }", "COPE-TABLE-0001")]
    [InlineData("enum TableBoundsError { Other, }", "COPE-TABLE-0002")]
    [InlineData("record table T { x: [1]; } function f(): boolean { return T.x == T.x; }", "COPE-TABLE-0017")]
    public void Binder_reports_table_specific_diagnostics(string source, string diagnosticId)
    {
        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Fact]
    public void Table_with_replaces_authored_columns_and_preserves_nominal_identity()
    {
        const string source = """
            record table Scores {
                name: string = ["Alice", "Bob", "Carol"];
                score: number = [95.0, 81.5, 91.0];
            }

            function revised(): Scores {
                return Scores with {
                    score: [95.0, 84.0, 91.0]
                };
            }
            """;

        CopelandCompilation first = CopelandCompiler.CompileToMir(source);
        CopelandCompilation second = CopelandCompiler.CompileToMir(source);

        Assert.True(first.Success, string.Join(Environment.NewLine, first.Diagnostics));
        Assert.Equal(first.MirText, second.MirText);
        BoundReturnStatement returned = Assert.IsType<BoundReturnStatement>(
            Assert.Single(first.BoundCompilation!.Program.Functions).Body.Statements[0]);
        BoundTableWithExpression update = Assert.IsType<BoundTableWithExpression>(returned.Expression);
        Assert.Equal("Scores", update.TableType.Name);
        Assert.Equal("score", Assert.Single(update.Replacements).Column.Name);
        Assert.Equal(3, Assert.Single(update.Replacements).Value.Elements.Count);
        Assert.Contains(
            "table-with [t1] table-ref [t1] { t1.c1: [95, 84, 91] }",
            first.MirText,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "record table Scores { score: number = [1, 2, 3]; } function revised(): Scores { return Scores with { score: [1, 2] }; }",
        "COPE-TABLE-0008")]
    [InlineData(
        "record table Scores { score: number = [1, 2, 3]; } function revised(values: number[]): Scores { return Scores with { score: values }; }",
        "COPE-TABLE-0022")]
    [InlineData(
        "record table Scores { score: number = [1, 2, 3]; } function revised(): Scores { return Scores with { score: [1, \"bad\", 3] }; }",
        "COPE-TYPE-0009")]
    public void Table_with_rejects_ragged_nonliteral_and_wrong_element_replacements(
        string source,
        string diagnosticId)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        var diagnostic = Assert.Single(
            compilation.Diagnostics,
            candidate => candidate.Id == diagnosticId);
        Assert.True(diagnostic.Position > 0);
    }
}
