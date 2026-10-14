using System.Text.Json;
using Xunit;

namespace Copeland.Cli.Tests;

public sealed partial class CliIntegrationTests
{
    [Fact]
    public async Task Table_row_edits_preserve_authoring_and_use_the_canonical_derived_query_path()
    {
        using var temp = new TempDir();
        const string original = """
            record table Stock { key id: int; onHand: int; price: float; } = [
                { onHand: 4, "id": 20, price: 1.50 },
                { id: 10, onHand: 7, price: 2.00 },
            ];
            record table Cost = derive Stock as row {
                const quantity = row.onHand;
                id: int = row.id;
                amount: int = quantity * 3;
            }
            // Keep surrounding source unchanged.
            function untouched(): int { return 42; }
            """;
        string source = temp.WriteFile("Stock.ts", original);
        CliResult preview = await RunCliAsync(temp.Path, "table", "set", source, "Stock", "--key", "10", "--column", "onHand", "--value", "9", "--dry-run", "--format", "json");
        Assert.True(preview.ExitCode == 0, preview.StdOut + preview.StdErr);
        Assert.Equal(original, File.ReadAllText(source));
        CliResult edit = await RunCliAsync(temp.Path, "table", "set", source, "Stock", "--key", "10", "--column", "onHand", "--value", "9", "--format", "json");
        Assert.True(edit.ExitCode == 0, edit.StdOut + edit.StdErr);
        Assert.Equal(original.Replace("id: 10, onHand: 7", "id: 10, onHand: 9", StringComparison.Ordinal), File.ReadAllText(source));
        CliResult query = await RunCliAsync(temp.Path, "table", "query", source, "Cost", "--order-by", "id asc", "--format", "json");
        Assert.True(query.ExitCode == 0, query.StdOut + query.StdErr);
        using (JsonDocument json = JsonDocument.Parse(query.StdOut))
        {
            Assert.Equal("csharp-relation-plan", json.RootElement.GetProperty("executor").GetString());
            Assert.Equal([27, 12], json.RootElement.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("amount").GetInt32()).ToArray());
        }
        string beforeRejection = File.ReadAllText(source);
        CliResult duplicate = await RunCliAsync(temp.Path, "table", "set", source, "Stock", "--key", "10", "--column", "id", "--value", "20", "--format", "json");
        Assert.NotEqual(0, duplicate.ExitCode);
        Assert.Equal(beforeRejection, File.ReadAllText(source));
        CliResult add = await RunCliAsync(temp.Path, "table", "add-row", source, "Stock", "--json", "{\"id\":30,\"onHand\":2,\"price\":3.25}", "--format", "json");
        Assert.True(add.ExitCode == 0, add.StdOut + add.StdErr);
        Assert.Contains("} = [", File.ReadAllText(source), StringComparison.Ordinal);
        string csv = System.IO.Path.Combine(temp.Path, "Stock.csv");
        CliResult export = await RunCliAsync(temp.Path, "table", "export", source, "Stock", "--format", "csv", "--output", csv);
        Assert.Equal(0, export.ExitCode);
        CliResult delete = await RunCliAsync(temp.Path, "table", "delete-row", source, "Stock", "--key", "20", "--format", "json");
        Assert.True(delete.ExitCode == 0, delete.StdOut + delete.StdErr);
        CliResult import = await RunCliAsync(temp.Path, "table", "import", source, "Stock", "--format", "csv", "--input", csv, "--replace", "--result-format", "json");
        Assert.True(import.ExitCode == 0, import.StdOut + import.StdErr);
        Assert.Contains("price: 1.50", File.ReadAllText(source), StringComparison.Ordinal);
        Assert.Contains("function untouched(): int { return 42; }", File.ReadAllText(source), StringComparison.Ordinal);
        CliResult rows = await RunCliAsync(temp.Path, "table", "rows", source, "Stock", "--format", "json");
        Assert.Equal(0, rows.ExitCode);
        using JsonDocument remaining = JsonDocument.Parse(rows.StdOut);
        Assert.Equal([20, 10, 30], remaining.RootElement.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("values").GetProperty("id").GetInt32()).ToArray());
    }

    [Fact]
    public async Task Repeated_row_edits_split_and_delete_logical_rows_without_changing_neighbors()
    {
        using var temp = new TempDir();
        const string original = """
            record table Values { value: int; flag: boolean; } = [
                { value: 99, flag: false } ... 0,
                { value: 4, flag: true } ... 3,
                { value: 8, flag: false },
            ];
            """;
        string source = temp.WriteFile("Values.ts", original);
        foreach (int target in new[] { 0, 1, 2, 3 })
        {
            File.WriteAllText(source, original);
            CliResult edited = await RunCliAsync(temp.Path, "table", "set", source, "Values", "--row", target.ToString(), "--column", "value", "--value", "9", "--format", "json");
            Assert.True(edited.ExitCode == 0, edited.StdOut + edited.StdErr);
            CliResult rows = await RunCliAsync(temp.Path, "table", "rows", source, "Values", "--format", "json");
            Assert.Equal(0, rows.ExitCode);
            using (JsonDocument json = JsonDocument.Parse(rows.StdOut))
            {
                JsonElement[] values = json.RootElement.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("values")).ToArray();
                Assert.Equal(4, values.Length);
                for (int row = 0; row < values.Length; row += 1)
                {
                    int expectedValue = row == 3 ? 8 : 4;
                    if (row == target)
                    {
                        expectedValue = 9;
                    }
                    Assert.Equal(expectedValue, values[row].GetProperty("value").GetInt32());
                    Assert.Equal(row < 3, values[row].GetProperty("flag").GetBoolean());
                }
            }
            File.WriteAllText(source, original);
            CliResult deleted = await RunCliAsync(temp.Path, "table", "delete-row", source, "Values", "--row", target.ToString(), "--format", "json");
            Assert.True(deleted.ExitCode == 0, deleted.StdOut + deleted.StdErr);
            CliResult remaining = await RunCliAsync(temp.Path, "table", "rows", source, "Values", "--format", "json");
            Assert.Equal(0, remaining.ExitCode);
            using JsonDocument after = JsonDocument.Parse(remaining.StdOut);
            int[] expected = [4, 4, 4, 8];
            Assert.Equal(expected.Where((_, row) => row != target), after.RootElement.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("values").GetProperty("value").GetInt32()));
        }
    }

    [Fact]
    public async Task Row_authoring_can_delete_its_last_trailing_comma_row_and_append_into_empty_data()
    {
        using var temp = new TempDir();
        string source = temp.WriteFile("Values.ts", "record table Values { key id: int; value: int; } = [{ id: 1, value: 7 },];");
        CliResult deleted = await RunCliAsync(temp.Path, "table", "delete-row", source, "Values", "--key", "1", "--format", "json");
        Assert.True(deleted.ExitCode == 0, deleted.StdOut + deleted.StdErr);
        CliResult added = await RunCliAsync(temp.Path, "table", "add-row", source, "Values", "--json", "{\"id\":2,\"value\":9}", "--format", "json");
        Assert.True(added.ExitCode == 0, added.StdOut + added.StdErr);
        CliResult rows = await RunCliAsync(temp.Path, "table", "rows", source, "Values", "--format", "json");
        Assert.Equal(0, rows.ExitCode);
        using JsonDocument json = JsonDocument.Parse(rows.StdOut);
        Assert.Equal(9, Assert.Single(json.RootElement.GetProperty("rows").EnumerateArray()).GetProperty("values").GetProperty("value").GetInt32());
    }
}
