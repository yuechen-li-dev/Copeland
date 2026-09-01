using Copeland.TS.Tson;
using Oblivion.Model;
using Oblivion.Persistence;
using Oblivion.Product;
using Xunit;

namespace Oblivion.App.Tests;

public sealed class TableCardTests
{
    [Fact]
    public void Structured_vault_loads_first_class_table_sources()
    {
        OblivionWorkspaceLoadResult load = OblivionApplication.LoadVault(FixtureRoot);

        Assert.True(load.Succeeded, string.Join(Environment.NewLine, load.Diagnostics));
        OblivionCard[] tables = load.Workspace!.Pages.Single().Cards
            .Where(card => card.Kind == OblivionCardKind.Table)
            .ToArray();
        Assert.Equal(2, tables.Length);
        Assert.Equal("content/validation-evidence.obj.ts", tables[0].Table!.Reference);
        Assert.Equal("content/validation-evidence.tson", tables[1].Table!.Reference);
        Assert.All(tables, card => Assert.Empty(card.Body.RawText));
    }

    [Fact]
    public void Authoring_and_canonical_sources_project_equivalent_columnar_tables()
    {
        OblivionCard[] cards = LoadTableCards();
        OblivionTableCardRealizer realizer = new();

        OblivionTableCardRealization authored = realizer.Realize(cards[0], FixtureRoot);
        OblivionTableCardRealization canonical = realizer.Realize(cards[1], FixtureRoot);

        Assert.True(authored.Succeeded, string.Join(Environment.NewLine, authored.Diagnostics));
        Assert.True(canonical.Succeeded, string.Join(Environment.NewLine, canonical.Diagnostics));
        Assert.Equal("obj.ts", authored.Profile);
        Assert.Equal("tson", canonical.Profile);
        AssertEquivalent(authored.Table!, canonical.Table!);
        Assert.Equal(16, authored.Table!.RowCount);
        Assert.Equal(7, authored.Table.Columns.Count);
    }

    [Theory]
    [InlineData("missing.obj.ts", "OBLIVION-TABLE-SOURCE-NOT-FOUND")]
    [InlineData("table.csv", "OBLIVION-TABLE-SOURCE-EXTENSION-UNSUPPORTED")]
    public void Missing_and_unsupported_sources_are_typed_failures(string reference, string code)
    {
        OblivionCard card = LoadTableCards()[0] with
        {
            Table = new OblivionTableSource(OblivionTableSourceKind.TsonTable, reference),
        };

        OblivionTableCardRealization result = new OblivionTableCardRealizer().Realize(card, FixtureRoot);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData("invalid.obj.ts", "this is not TSON", null)]
    [InlineData("record.obj.ts", "const $schema: string = \"copeland://oblivion/m20e/record\"; record Value { x: number; } const $value: Value = { x: 1 };", "OBLIVION-TABLE-ROOT-NOT-TABLE")]
    public void Invalid_and_non_table_roots_fail_without_guessing(
        string fileName,
        string source,
        string? expectedCode)
    {
        using TemporaryDirectory directory = new();
        File.WriteAllText(Path.Combine(directory.Path, fileName), source);
        OblivionCard card = LoadTableCards()[0] with
        {
            Table = new OblivionTableSource(OblivionTableSourceKind.TsonTable, fileName),
        };

        OblivionTableCardRealization result = new OblivionTableCardRealizer().Realize(card, directory.Path);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Diagnostics);
        if (expectedCode is not null)
        {
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
        }
    }

    [Fact]
    public void Noncanonical_tson_is_rejected_by_the_existing_canonical_profile()
    {
        using TemporaryDirectory directory = new();
        string source = File.ReadAllText(Path.Combine(FixtureRoot, "content", "validation-evidence.tson"));
        File.WriteAllText(Path.Combine(directory.Path, "noncanonical.tson"), source + " ");
        OblivionCard card = LoadTableCards()[1] with
        {
            Table = new OblivionTableSource(OblivionTableSourceKind.TsonTable, "noncanonical.tson"),
        };

        OblivionTableCardRealization result = new OblivionTableCardRealizer().Realize(card, directory.Path);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "COPE-TSON-TABLE-0005");
    }

    [Fact]
    public void Row_projection_indexes_authoritative_columns_without_allocating_rows()
    {
        TsonTable table = RealizeAuthoredTable();

        Assert.Equal("obj-ts-load", Assert.IsType<TsonString>(
            OblivionTableProjection.Cell(table, 0, 1)).Value);
        Assert.Equal("diff-check", Assert.IsType<TsonString>(
            OblivionTableProjection.Cell(table, 15, 1)).Value);
        Assert.Equal(
            new[] { "order", "lane", "subsystem", "required", "risk", "proofs", "evidence" },
            table.Columns.Select(column => column.Schema.Name));
        Assert.Equal(
            table.Schema.Columns.Select(column => column.Identity.Value),
            table.Columns.Select(column => column.Schema.Identity.Value));
        Assert.DoesNotContain(
            typeof(OblivionCard).Assembly.GetTypes().Select(type => type.Name),
            name => name is "TableRow" or "TableCell" or "TableSchema");
    }

    [Fact]
    public void Formatter_is_deterministic_invariant_and_bounded_for_every_cell_family()
    {
        TsonTable table = RealizeAuthoredTable();

        Assert.Equal("1", OblivionTableCellDisplayFormatter.Format(table.Columns[0].Cells[0]));
        Assert.Equal("true", OblivionTableCellDisplayFormatter.Format(table.Columns[3].Cells[0]));
        Assert.Equal("Semantic", OblivionTableCellDisplayFormatter.Format(table.Columns[4].Cells[0]));
        Assert.Equal("[unit, equivalence]", OblivionTableCellDisplayFormatter.Format(table.Columns[5].Cells[1]));
        Assert.Equal(
            "{owner: Oblivion.App, expected: authoring profile loads}",
            OblivionTableCellDisplayFormatter.Format(table.Columns[6].Cells[0]));
        Assert.Equal("-0", OblivionTableCellDisplayFormatter.Format(TsonNumber.FromBits(0x8000000000000000)));
        Assert.Equal("NaN", OblivionTableCellDisplayFormatter.Format(TsonNumber.FromDouble(double.NaN)));
        Assert.Equal("Infinity", OblivionTableCellDisplayFormatter.Format(TsonNumber.FromDouble(double.PositiveInfinity)));
        Assert.Equal("line\\nnext", OblivionTableCellDisplayFormatter.Format(new TsonString("line\nnext")));
    }

    [Theory]
    [InlineData("record table Empty { value: string = []; }", 0, 1)]
    [InlineData("record table One { value: string = [\"only\"]; }", 1, 1)]
    public void Zero_row_one_row_and_one_column_shapes_remain_valid(
        string declaration,
        int expectedRows,
        int expectedColumns)
    {
        string source = "const $schema: string = \"copeland://oblivion/m20e/shape\"; " +
            declaration + " const $value = " + (expectedRows == 0 ? "Empty;" : "One;");
        TsonReadResult read = TsonDocumentReader.ReadSelfDescribed(
            source,
            TsonDocumentProfile.ObjectTypeScript);

        Assert.True(read.Success, string.Join(Environment.NewLine, read.Diagnostics.Select(item => item.Message)));
        TsonTable table = Assert.IsType<TsonTable>(read.Document!.Root);
        Assert.Equal(expectedRows, table.RowCount);
        Assert.Equal(expectedColumns, table.Columns.Count);
    }

    [Fact]
    public void Collapsed_has_no_preview_and_expanded_selects_read_only_table_presenter()
    {
        OblivionCard card = LoadTableCards()[0];
        OblivionTableCardRealization realization = new OblivionTableCardRealizer().Realize(card, FixtureRoot);
        OblivionTablePresentationSource source = new(
            realization.Table!,
            realization.Source.Reference,
            realization.Profile!,
            realization.SourceHash!,
            realization.LoadMilliseconds,
            realization.Diagnostics);

        OblivionContentPresentationPlan collapsed = OblivionContentPresenterSelector.Select(
            card,
            OblivionCardViewState.Collapsed,
            table: source);
        OblivionContentPresentationPlan expanded = OblivionContentPresenterSelector.Select(
            card,
            new OblivionCardViewState(true, 0),
            table: source);

        Assert.Empty(collapsed.Items);
        Assert.Equal("Table", collapsed.ContentTypeLabel);
        Assert.Equal(OblivionContentPresenterKind.AvaloniaReadOnlyTable, Assert.Single(expanded.Items).PresenterKind);
        Assert.Same(realization.Table, expanded.Table!.Table);
        Assert.True(expanded.AllowsInternalScroll);
    }

    [Fact]
    public void Card_show_and_content_expose_shape_but_never_fake_text()
    {
        OblivionWorkspaceControl control = new();
        OblivionControlResult<OblivionCardDetail> shown = control.ShowCard(
            FixtureRoot,
            "validation-evidence");
        OblivionControlResult<OblivionCardContentResult> content = control.GetCardContent(
            FixtureRoot,
            "validation-evidence");

        Assert.True(shown.Succeeded, string.Join(Environment.NewLine, shown.Diagnostics));
        Assert.Equal("table", shown.Value!.Kind);
        Assert.Equal("TsonTable", shown.Value.TableSourceKind);
        Assert.Equal("obj.ts", shown.Value.TableProfile);
        Assert.Equal(16, shown.Value.TableRowCount);
        Assert.Equal(7, shown.Value.TableColumnCount);
        Assert.Equal("order", shown.Value.TableColumnNames![0]);
        Assert.Equal("number", shown.Value.TableColumnTypes![0]);
        Assert.False(content.Succeeded);
        Assert.Contains(content.Diagnostics, diagnostic => diagnostic.Code == "OBLIVION-CARD-CONTENT-NOT-TEXT");
    }

    private static string FixtureRoot => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "M20eTsonTables.oblivion");

    private static OblivionCard[] LoadTableCards()
    {
        OblivionWorkspaceLoadResult load = OblivionApplication.LoadVault(FixtureRoot);
        Assert.True(load.Succeeded, string.Join(Environment.NewLine, load.Diagnostics));
        return load.Workspace!.Pages.Single().Cards
            .Where(card => card.Kind == OblivionCardKind.Table)
            .ToArray();
    }

    private static TsonTable RealizeAuthoredTable()
    {
        OblivionTableCardRealization realization = new OblivionTableCardRealizer().Realize(
            LoadTableCards()[0],
            FixtureRoot);
        Assert.True(realization.Succeeded, string.Join(Environment.NewLine, realization.Diagnostics));
        return realization.Table!;
    }

    private static void AssertEquivalent(TsonTable expected, TsonTable actual)
    {
        Assert.Equal(expected.Schema.IdentityValue.Value, actual.Schema.IdentityValue.Value);
        Assert.Equal(expected.RowCount, actual.RowCount);
        Assert.Equal(expected.Columns.Count, actual.Columns.Count);
        for (int columnIndex = 0; columnIndex < expected.Columns.Count; columnIndex++)
        {
            TsonTableColumn left = expected.Columns[columnIndex];
            TsonTableColumn right = actual.Columns[columnIndex];
            Assert.Equal(left.Schema.Identity.Value, right.Schema.Identity.Value);
            Assert.Equal(
                OblivionTableCellDisplayFormatter.FormatType(left.Schema.ElementType),
                OblivionTableCellDisplayFormatter.FormatType(right.Schema.ElementType));
            Assert.Equal(
                left.Cells.Select(OblivionTableCellDisplayFormatter.Format),
                right.Cells.Select(OblivionTableCellDisplayFormatter.Format));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "oblivion-m20e-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
