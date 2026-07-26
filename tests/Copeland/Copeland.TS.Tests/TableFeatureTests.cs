using Copeland.TS.Compiler;
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
}
