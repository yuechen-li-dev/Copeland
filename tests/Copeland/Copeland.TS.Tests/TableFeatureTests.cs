using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TableFeatureTests
{
    private const string Program = """
        record table Samples {
            x: [1, 2];
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
        Assert.Null(table.Columns[0].ExplicitType);
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
    public void Table_cells_lower_to_closed_mir_constants_not_executable_expressions()
    {
        var compilation = CopelandCompiler.CompileToMir("record table Values { enabled: [true]; label: [\"ok\"]; count: [-0]; }");

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        MirTableDefinition table = Assert.Single(compilation.MirCompilation!.Program!.Tables);
        Assert.All(table.Columns.SelectMany(column => column.Constants), constant => Assert.IsAssignableFrom<MirTableConstant>(constant));
        Assert.All(table.Columns.SelectMany(column => column.Constants), constant => Assert.IsNotAssignableFrom<MirExpression>(constant));
        Assert.IsType<MirTableLiteralConstant>(table.Columns[0].Constants[0]);
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
