using System.Security.Cryptography;
using System.Text;
using Copeland.TS.Tson;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TsonTableFeatureTests
{
    [Fact]
    public void Authoring_table_projects_to_nominal_immutable_table_and_exact_canonical_text()
    {
        const string authoring = """
            const $schema: string = "copeland://example/telemetry";

            // Authoring comments do not survive canonical projection.
            record table Samples {
                active: [true, false];
                score: [1, -0];
                label: ["first", "second"];
            }

            const $value = Samples;
            """;

        const string expected = """
            const $schema: string = "copeland://example/telemetry";

            record table Samples {
                active: boolean = [
                    true,
                    false,
                ];
                score: number = [
                    $number("3FF0000000000000"),
                    $number("8000000000000000"),
                ];
                label: string = [
                    "first",
                    "second",
                ];
            }

            const $value = Samples;
            """ + "\n";

        TsonReadResult result = TsonDocumentReader.ReadSelfDescribed(
            authoring,
            TsonDocumentProfile.ObjectTypeScript);

        Assert.True(result.Success, Describe(result));
        TsonTable table = Assert.IsType<TsonTable>(result.Document!.Root);
        Assert.Equal("copeland://example/telemetry#Samples", table.Schema.IdentityValue.Value);
        Assert.Equal("copeland://example/telemetry#Samples.active", table.Columns[0].Schema.Identity.Value);
        Assert.Equal(2, table.RowCount);
        Assert.Equal(expected, TsonCanonicalPrinter.Print(result.Document));
        Assert.True(TsonDocumentReader.ReadSelfDescribed(expected, TsonDocumentProfile.CanonicalTson).Success);
    }

    [Fact]
    public void Table_storage_is_defensive_and_columns_retain_declaration_order()
    {
        TsonTableIdentity tableIdentity = TsonTableIdentity.Create("copeland://example/immutable", "Values");
        var schemaStorage = new[]
        {
            new TsonTableColumnSchema(
                "first",
                TsonTableColumnIdentity.Create(tableIdentity, "first"),
                TsonTypeReference.Number),
            new TsonTableColumnSchema(
                "second",
                TsonTableColumnIdentity.Create(tableIdentity, "second"),
                TsonTypeReference.Number),
        };
        var schema = new TsonTableSchema("Values", tableIdentity, schemaStorage);
        var firstCells = new TsonValue[] { TsonNumber.FromDouble(1) };
        var columnStorage = new[]
        {
            new TsonTableColumn(schema.Columns[0], firstCells),
            new TsonTableColumn(schema.Columns[1], [TsonNumber.FromDouble(2)]),
        };
        var table = new TsonTable(schema, columnStorage);

        schemaStorage[0] = schemaStorage[1];
        firstCells[0] = TsonNumber.FromDouble(99);
        columnStorage[0] = columnStorage[1];

        Assert.Equal(["first", "second"], table.Columns.Select(column => column.Schema.Name));
        Assert.Equal(1, table.RowCount);
        Assert.Equal(1, Assert.IsType<TsonNumber>(table.Columns[0].Cells[0]).Value);
    }

    [Fact]
    public void Canonical_table_data_is_columnar_and_has_no_row_representation()
    {
        const string source = """
            const $schema: string = "copeland://example/columnar";
            record table Samples {
                second: number = [4, 5];
                first: number = [1, 2];
            }
            const $value = Samples;
            """;

        TsonReadResult result = TsonDocumentReader.ReadSelfDescribed(
            source,
            TsonDocumentProfile.ObjectTypeScript);

        Assert.True(result.Success, Describe(result));
        TsonTable table = Assert.IsType<TsonTable>(result.Document!.Root);
        Assert.Equal(["second", "first"], table.Columns.Select(column => column.Schema.Name));
        Assert.Equal([4d, 5d], table.Columns[0].Cells
            .Select(cell => Assert.IsType<TsonNumber>(cell).Value));
        Assert.Equal([1d, 2d], table.Columns[1].Cells
            .Select(cell => Assert.IsType<TsonNumber>(cell).Value));
        Assert.Equal(2, table.RowCount);
        Assert.Null(typeof(TsonTable).GetProperty("Rows"));
        Assert.DoesNotContain(
            typeof(TsonTable).Assembly.GetTypes(),
            type => type.Name.Contains("TsonTableRow", StringComparison.Ordinal));

        string canonical = TsonCanonicalPrinter.Print(result.Document);
        int secondColumn = canonical.IndexOf("second: number", StringComparison.Ordinal);
        int firstColumn = canonical.IndexOf("first: number", StringComparison.Ordinal);
        Assert.True(secondColumn >= 0 && firstColumn > secondColumn);
        Assert.Contains("second: number = [\n        $number(\"4010000000000000\"),", canonical, StringComparison.Ordinal);
        Assert.Contains("first: number = [\n        $number(\"3FF0000000000000\"),", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("[{", canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_columns_retain_types_and_same_shape_tables_are_nominally_distinct()
    {
        TsonTable first = ReadTable("copeland://example/one", "record table Empty { value: number = []; }");
        TsonTable second = ReadTable("copeland://example/two", "record table Empty { value: number = []; }");

        Assert.Equal(TsonTypeKind.Number, first.Columns[0].Schema.ElementType.Kind);
        Assert.Equal(0, first.RowCount);
        Assert.NotEqual(first.Schema.IdentityValue, second.Schema.IdentityValue);
    }

    [Fact]
    public void Record_enum_nested_array_binary64_and_unicode_cells_round_trip()
    {
        const string source = """
            const $schema: string = "copeland://example/cells";
            record Point { x: number; }
            enum State { Named(label: string), }
            record table Values {
                point: Point = [{ x: 1 }];
                state: State = [State.Named("ready")];
                matrix: number[][] = [[[0, -0], [1]]];
                edge: number = [$number("7FF0000000000001")];
                text: string = ["quote: \"; snow: 雪; pair: 😀"];
            }
            const $value = Values;
            """;

        TsonReadResult result = TsonDocumentReader.ReadSelfDescribed(source, TsonDocumentProfile.ObjectTypeScript);

        Assert.True(result.Success, Describe(result));
        TsonTable table = Assert.IsType<TsonTable>(result.Document!.Root);
        Assert.IsType<TsonRecord>(table.Columns[0].Cells[0]);
        Assert.IsType<TsonEnum>(table.Columns[1].Cells[0]);
        Assert.IsType<TsonArray>(table.Columns[2].Cells[0]);
        Assert.Equal(0x7FF8000000000000UL, Assert.IsType<TsonNumber>(table.Columns[3].Cells[0]).Bits);
        string canonical = TsonCanonicalPrinter.Print(result.Document);
        Assert.Equal(canonical, TsonCanonicalPrinter.Print(result.Document));
        Assert.True(TsonDocumentReader.ReadSelfDescribed(canonical, TsonDocumentProfile.CanonicalTson).Success);
    }

    [Theory]
    [InlineData("record table Empty { }", "COPE-TSON-TABLE-0003")]
    [InlineData("record table Values { x: [1]; x: [2]; }", "COPE-TSON-TABLE-0003")]
    [InlineData("record table Values { x: [1, 2]; y: [3]; }", "COPE-TSON-TABLE-0003")]
    [InlineData("record table Values { x: number = [\"wrong\"]; }", "COPE-TSON-TABLE-0004")]
    [InlineData("record table Values { x: Result = []; }", "COPE-TSON-TABLE-0002")]
    public void Invalid_table_shapes_and_cells_use_bounded_table_diagnostics(string declaration, string code)
    {
        string source = $"const $schema: string = \"copeland://example/invalid\"; {declaration} const $value = Values;";

        TsonReadResult result = TsonDocumentReader.ReadSelfDescribed(source, TsonDocumentProfile.ObjectTypeScript);

        TsonDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(code, diagnostic.Code);
        Assert.True(diagnostic.Length > 0);
    }

    [Fact]
    public void Table_root_is_exact_and_nested_or_multiple_tables_are_rejected()
    {
        AssertCode(
            "record table Values { x: [1]; } const $value = [Values];",
            "COPE-TSON-TABLE-0001");
        AssertCode(
            "record table First { x: [1]; } record table Second { x: [1]; } const $value = First;",
            "COPE-TSON-TABLE-0001");
        AssertCode(
            "record table Values { x: [1]; } const $value = Unknown;",
            "COPE-TSON-TABLE-0001");
    }

    [Fact]
    public void Table_limits_are_cumulative_and_output_limit_is_incremental()
    {
        const string twoByTwo = "const $schema: string = \"copeland://example/limits\"; record table Values { x: [1, 2]; y: [3, 4]; } const $value = Values;";
        var cellLimits = new TsonLimits(maximumTableCellCount: 3);
        TsonReadResult cells = TsonDocumentReader.ReadSelfDescribed(
            twoByTwo,
            TsonDocumentProfile.ObjectTypeScript,
            limits: cellLimits);
        Assert.Equal("COPE-TSON-TABLE-0005", Assert.Single(cells.Diagnostics).Code);

        TsonDocument document = TsonDocumentReader.ReadSelfDescribed(
            twoByTwo,
            TsonDocumentProfile.ObjectTypeScript).Document!;
        byte[] canonical = TsonCanonicalPrinter.PrintUtf8(document);
        Assert.Equal(canonical.Length, TsonCanonicalPrinter.PrintUtf8(
            document,
            new TsonLimits(maximumCanonicalUtf8ByteCount: canonical.Length)).Length);
        Assert.Throws<TsonCanonicalLimitException>(() => TsonCanonicalPrinter.Print(
            document,
            new TsonLimits(maximumCanonicalUtf8ByteCount: canonical.Length - 1)));
    }

    [Theory]
    [InlineData(255, true)]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void Column_count_boundaries_are_exact(int columnCount, bool success)
    {
        string columns = string.Join(
            " ",
            Enumerable.Range(0, columnCount).Select(index => $"c{index}: number = [];"));
        string source = $"const $schema: string = \"copeland://example/columns\"; record table Values {{ {columns} }} const $value = Values;";

        TsonReadResult result = TsonDocumentReader.ReadSelfDescribed(source, TsonDocumentProfile.ObjectTypeScript);

        Assert.Equal(success, result.Success);
        if (!success)
        {
            Assert.Equal("COPE-TSON-TABLE-0005", Assert.Single(result.Diagnostics).Code);
        }
    }

    [Theory]
    [InlineData(99_999, true)]
    [InlineData(100_000, true)]
    [InlineData(100_001, false)]
    public void Row_and_cell_count_boundaries_are_exact(int rowCount, bool success)
    {
        string cells = string.Join(",", Enumerable.Repeat("0", rowCount));
        string source = $"const $schema: string = \"copeland://example/rows\"; record table Values {{ x: number = [{cells}]; }} const $value = Values;";
        var limits = new TsonLimits(
            maximumSourceLength: 2_000_000,
            maximumValueNodeCount: 100_002);

        TsonReadResult result = TsonDocumentReader.ReadSelfDescribed(
            source,
            TsonDocumentProfile.ObjectTypeScript,
            limits: limits);

        Assert.Equal(success, result.Success);
        if (!success)
        {
            Assert.Equal("COPE-TSON-TABLE-0005", Assert.Single(result.Diagnostics).Code);
        }
    }

    [Fact]
    public void Value_node_count_includes_table_columns_cells_and_nested_values_once()
    {
        const string source = "const $schema: string = \"copeland://example/nodes\"; record table Values { x: number = [1]; } const $value = Values;";

        Assert.True(TsonDocumentReader.ReadSelfDescribed(
            source,
            TsonDocumentProfile.ObjectTypeScript,
            limits: new TsonLimits(maximumValueNodeCount: 3)).Success);
        TsonReadResult overflow = TsonDocumentReader.ReadSelfDescribed(
            source,
            TsonDocumentProfile.ObjectTypeScript,
            limits: new TsonLimits(maximumValueNodeCount: 2));
        Assert.Equal("COPE-TSON-TABLE-0005", Assert.Single(overflow.Diagnostics).Code);
    }

    [Fact]
    public void Semantic_construction_rejects_missing_reordered_and_aliased_columns_or_cells()
    {
        TsonTableIdentity identity = TsonTableIdentity.Create("copeland://example/construct", "Values");
        var first = new TsonTableColumnSchema(
            "first",
            TsonTableColumnIdentity.Create(identity, "first"),
            TsonTypeReference.Array(TsonTypeReference.Number));
        var second = new TsonTableColumnSchema(
            "second",
            TsonTableColumnIdentity.Create(identity, "second"),
            TsonTypeReference.Array(TsonTypeReference.Number));
        var schema = new TsonTableSchema("Values", identity, [first, second]);
        var array = new TsonArray(
            new TsonArraySchema(TsonTypeReference.Number),
            [TsonNumber.FromDouble(1)]);

        Assert.Throws<ArgumentException>(() => new TsonTable(
            schema,
            [new TsonTableColumn(first, [array])]));
        Assert.Throws<ArgumentException>(() => new TsonTable(
            schema,
            [new TsonTableColumn(second, [array]), new TsonTableColumn(first, [array])]));
        Assert.Throws<ArgumentException>(() => new TsonTable(
            schema,
            [new TsonTableColumn(first, [array]), new TsonTableColumn(second, [array])]));
    }

    [Fact]
    public void Representative_corpus_has_pinned_exact_bytes_and_sha256()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Tson", "Tables", "Corpus", "representative.tson");
        byte[] bytes = File.ReadAllBytes(path);
        string text = new UTF8Encoding(false, true).GetString(bytes);
        TsonReadResult read = TsonDocumentReader.ReadSelfDescribed(text, TsonDocumentProfile.CanonicalTson);

        Assert.True(read.Success, Describe(read));
        Assert.Equal(bytes, TsonCanonicalPrinter.PrintUtf8(read.Document!));
        Assert.Equal(1_145, bytes.Length);
        Assert.Equal(
            "450DF822E63C4A1F681D98796D707EA6AAB35D1B4D533CDD479B49BB2394256A",
            Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static TsonTable ReadTable(string schema, string declaration)
    {
        string source = $"const $schema: string = \"{schema}\"; {declaration} const $value = Empty;";
        TsonReadResult result = TsonDocumentReader.ReadSelfDescribed(source, TsonDocumentProfile.ObjectTypeScript);
        Assert.True(result.Success, Describe(result));
        return Assert.IsType<TsonTable>(result.Document!.Root);
    }

    private static void AssertCode(string body, string code)
    {
        string source = $"const $schema: string = \"copeland://example/root\"; {body}";
        TsonReadResult result = TsonDocumentReader.ReadSelfDescribed(source, TsonDocumentProfile.ObjectTypeScript);
        Assert.Equal(code, Assert.Single(result.Diagnostics).Code);
    }

    private static string Describe(TsonReadResult result)
    {
        return string.Join(
            Environment.NewLine,
            result.SyntaxDiagnostics.Select(diagnostic => diagnostic.ToString())
                .Concat(result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
    }

}
