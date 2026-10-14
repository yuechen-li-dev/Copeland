using System.Text.Json;
using Xunit;

namespace Copeland.Cli.Tests;

public sealed partial class CliIntegrationTests
{
    [Fact]
    public async Task Table_key_edits_follow_identity_after_reordering_and_preserve_repeated_neighbors()
    {
        using var temp = new TempDir();
        foreach (string ids in new[] { "[30, 10, 20]", "[20, 30, 10]" })
        {
            string original = $$"""
                type Identity = int;
                record table Stock {
                    key sku: Identity = {{ids}};
                    onHand: int = [4 ... 3];
                    valid: boolean = [true ...];
                }
                // Keep surrounding source unchanged.
                function untouched(): int { return 7; }
                """;
            string source = temp.WriteFile("Stock.ts", original);
            int target = ids == "[30, 10, 20]" ? 1 : 2;
            CliResult preview = await RunCliAsync(temp.Path, "table", "set", source, "Stock", "--key", "10", "--column", "onHand", "--value", "12", "--dry-run", "--format", "json");
            Assert.Equal(0, preview.ExitCode);
            Assert.Equal(original, File.ReadAllText(source));
            using (JsonDocument json = JsonDocument.Parse(preview.StdOut))
            {
                JsonElement selection = json.RootElement.GetProperty("selection");
                Assert.Equal("key", selection.GetProperty("kind").GetString());
                Assert.Equal("sku", selection.GetProperty("keyColumn").GetString());
                Assert.Equal(10, selection.GetProperty("value").GetInt32());
                Assert.Equal(target, selection.GetProperty("row").GetInt32());
            }
            CliResult edit = await RunCliAsync(temp.Path, "table", "set", source, "Stock", "--key", "10", "--column", "onHand", "--value", "12", "--format", "json");
            Assert.Equal(0, edit.ExitCode);
            CliResult rows = await RunCliAsync(temp.Path, "table", "rows", source, "Stock", "--format", "json");
            Assert.Equal(0, rows.ExitCode);
            using (JsonDocument json = JsonDocument.Parse(rows.StdOut))
            {
                foreach (JsonElement row in json.RootElement.GetProperty("rows").EnumerateArray())
                {
                    JsonElement values = row.GetProperty("values");
                    Assert.Equal(values.GetProperty("sku").GetInt32() == 10 ? 12 : 4, values.GetProperty("onHand").GetInt32());
                    Assert.True(values.GetProperty("valid").GetBoolean());
                }
            }
            Assert.Contains("key sku: Identity = " + ids + ";", File.ReadAllText(source), StringComparison.Ordinal);
            Assert.Contains("function untouched(): int { return 7; }", File.ReadAllText(source), StringComparison.Ordinal);
            string beforeDelete = File.ReadAllText(source);
            CliResult deletePreview = await RunCliAsync(temp.Path, "table", "delete-row", source, "Stock", "--key", "10", "--dry-run", "--format", "json");
            Assert.Equal(0, deletePreview.ExitCode);
            Assert.Equal(beforeDelete, File.ReadAllText(source));
            CliResult deleted = await RunCliAsync(temp.Path, "table", "delete-row", source, "Stock", "--key", "10", "--format", "json");
            Assert.Equal(0, deleted.ExitCode);
            using (JsonDocument json = JsonDocument.Parse(deleted.StdOut))
            {
                Assert.Equal(10, json.RootElement.GetProperty("deleted").GetProperty("values").GetProperty("sku").GetInt32());
            }
            CliResult remaining = await RunCliAsync(temp.Path, "table", "rows", source, "Stock", "--format", "json");
            Assert.Equal(0, remaining.ExitCode);
            using JsonDocument remainingJson = JsonDocument.Parse(remaining.StdOut);
            JsonElement[] remainingRows = remainingJson.RootElement.GetProperty("rows").EnumerateArray().ToArray();
            Assert.Equal(2, remainingRows.Length);
            Assert.All(remainingRows, row =>
            {
                JsonElement values = row.GetProperty("values");
                Assert.NotEqual(10, values.GetProperty("sku").GetInt32());
                Assert.Equal(4, values.GetProperty("onHand").GetInt32());
                Assert.True(values.GetProperty("valid").GetBoolean());
            });
        }
    }

    [Theory]
    [InlineData("int", "[-3, 2]", "-3", "-3", 0)]
    [InlineData("int", "[0, 2]", "+0", "0", 0)]
    [InlineData("boolean", "[true, false]", "false", "false", 1)]
    [InlineData("string", "[\"001\", \"1\"]", "001", "\"001\"", 0)]
    [InlineData("string", "[\"A\", \"a\"]", "a", "\"a\"", 1)]
    [InlineData("string", "[\"\", \"other\"]", "", "\"\"", 0)]
    [InlineData("string", "['caf\"é\\leaf, x', 'other']", "caf\"é\\leaf, x", "\"caf\\\"é\\\\leaf, x\"", 0)]
    public async Task Table_key_edits_use_exact_declared_scalar_values(string type, string cells, string key, string expectedJson, int target)
    {
        using var temp = new TempDir();
        string source = temp.WriteFile("Values.ts", $"record table Values {{ key code: {type} = {cells}; value: int = [0, 1]; }}");
        CliResult edited = await RunCliAsync(temp.Path, "table", "set", source, "Values", "--key", key, "--column", "value", "--value", "9", "--format", "json");
        Assert.True(edited.ExitCode == 0, edited.StdOut + edited.StdErr);
        using (JsonDocument json = JsonDocument.Parse(edited.StdOut))
        using (JsonDocument expected = JsonDocument.Parse(expectedJson))
        {
            JsonElement selection = json.RootElement.GetProperty("selection");
            Assert.Equal(expected.RootElement.ValueKind, selection.GetProperty("value").ValueKind);
            Assert.Equal(expected.RootElement.ToString(), selection.GetProperty("value").ToString());
            Assert.Equal(target, selection.GetProperty("row").GetInt32());
        }
        CliResult rows = await RunCliAsync(temp.Path, "table", "rows", source, "Values", "--format", "json");
        Assert.Equal(0, rows.ExitCode);
        using (JsonDocument json = JsonDocument.Parse(rows.StdOut))
        {
            int[] values = json.RootElement.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("values").GetProperty("value").GetInt32()).ToArray();
            Assert.Equal(target == 0 ? new[] { 9, 1 } : new[] { 0, 9 }, values);
        }
        CliResult deleted = await RunCliAsync(temp.Path, "table", "delete-row", source, "Values", "--key", key, "--format", "json");
        Assert.Equal(0, deleted.ExitCode);
        CliResult remaining = await RunCliAsync(temp.Path, "table", "rows", source, "Values", "--format", "json");
        Assert.Equal(0, remaining.ExitCode);
        using JsonDocument remainingJson = JsonDocument.Parse(remaining.StdOut);
        JsonElement survivor = Assert.Single(remainingJson.RootElement.GetProperty("rows").EnumerateArray());
        Assert.Equal(target == 0 ? 1 : 0, survivor.GetProperty("values").GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task Table_key_selection_preserves_compiler_projected_escaped_string_identity()
    {
        using var temp = new TempDir();
        const string sourceText = """
            record table Values { key code: string = ["caf\"é\\leaf, x"]; value: int = [4]; }
            """;
        const string projectedKey = """caf\"é\\leaf, x""";
        string source = temp.WriteFile("Escaped.ts", sourceText);
        CliResult rows = await RunCliAsync(temp.Path, "table", "rows", source, "Values", "--format", "json");
        Assert.Equal(0, rows.ExitCode);
        using (JsonDocument json = JsonDocument.Parse(rows.StdOut))
        {
            Assert.Equal(projectedKey, json.RootElement.GetProperty("rows")[0].GetProperty("values").GetProperty("code").GetString());
        }
        CliResult edited = await RunCliAsync(temp.Path, "table", "set", source, "Values", "--key", projectedKey, "--column", "value", "--value", "9", "--format", "json");
        Assert.True(edited.ExitCode == 0, edited.StdOut + edited.StdErr);
        Assert.Contains("key code: string = [\"caf\\\"é\\\\leaf, x\"]", File.ReadAllText(source), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("key code: int = [1, 2];", new string[] { }, "COPE-TABLE-TOOL-0021")]
    [InlineData("key code: int = [1, 2];", new[] { "--key", "1", "--row", "0" }, "COPE-TABLE-TOOL-0021")]
    [InlineData("key code: int = [1, 2];", new[] { "--key", "1", "--key", "2" }, "COPE-TABLE-TOOL-0003")]
    [InlineData("key code: int = [1, 2];", new[] { "--row", "-1" }, "COPE-TABLE-TOOL-0005")]
    [InlineData("key code: int = [1, 2];", new[] { "--key", "3" }, "COPE-TABLE-TOOL-0023")]
    [InlineData("key code: int = [1, 2];", new[] { "--key", "1.0" }, "COPE-TABLE-TOOL-0010")]
    [InlineData("key code: int = [1, 2];", new[] { "--key", "2147483648" }, "COPE-TABLE-TOOL-0010")]
    [InlineData("code: int = [1, 2];", new[] { "--key", "1" }, "COPE-TABLE-TOOL-0022")]
    [InlineData("key code: boolean = [true, false];", new[] { "--key", "False" }, "COPE-TABLE-TOOL-0010")]
    [InlineData("key code: string = [\"A\", \"B\"];", new[] { "--key", "a" }, "COPE-TABLE-TOOL-0023")]
    [InlineData("key code: int = [];", new[] { "--key", "1" }, "COPE-TABLE-TOOL-0023")]
    public async Task Table_key_selector_failures_preserve_source(string column, string[] selector, string diagnostic)
    {
        using var temp = new TempDir();
        string source = temp.WriteFile("Values.ts", $"record table Values {{ {column} }}");
        byte[] original = File.ReadAllBytes(source);
        foreach (string command in new[] { "set", "delete-row" })
        {
            var arguments = new List<string> { "table", command, source, "Values" };
            arguments.AddRange(selector);
            if (command == "set")
            {
                arguments.AddRange(["--column", "code", "--value", "0"]);
            }
            arguments.AddRange(["--format", "json"]);
            CliResult rejected = await RunCliAsync(temp.Path, arguments.ToArray());
            Assert.Equal(1, rejected.ExitCode);
            using JsonDocument json = JsonDocument.Parse(rejected.StdOut);
            Assert.Equal(diagnostic, json.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.Equal(original, File.ReadAllBytes(source));
        }
    }

    [Fact]
    public async Task Table_key_edits_keep_compiler_key_reference_and_derived_write_guards()
    {
        using var temp = new TempDir();
        string source = temp.WriteFile("Relations.ts", """
            record table Parents { key code: int = [1, 2]; value: int = [4 ... 2]; }
            record table Children { reference parent: int -> Parents.code = [1]; }
            record table Derived = derive Parents as parent { code: int = parent.code; value: int = parent.value; }
            """);
        byte[] original = File.ReadAllBytes(source);
        (string[] Arguments, string Diagnostic)[] rejectedEdits =
        [
            (["set", source, "Parents", "--key", "2", "--column", "code", "--value", "1"], "COPE-TABLE-0037"),
            (["set", source, "Parents", "--key", "1", "--column", "code", "--value", "3"], "COPE-TABLE-0038"),
            (["delete-row", source, "Parents", "--key", "1"], "COPE-TABLE-0038"),
            (["set", source, "Derived", "--key", "1", "--column", "value", "--value", "0"], "COPE-TABLE-TOOL-0020"),
            (["delete-row", source, "Derived", "--key", "1"], "COPE-TABLE-TOOL-0020"),
        ];
        foreach (var edit in rejectedEdits)
        {
            CliResult rejected = await RunCliAsync(temp.Path, ["table", .. edit.Arguments, "--format", "json"]);
            Assert.Equal(1, rejected.ExitCode);
            Assert.Contains(edit.Diagnostic, rejected.StdOut, StringComparison.Ordinal);
            Assert.Equal(original, File.ReadAllBytes(source));
        }
        CliResult valid = await RunCliAsync(temp.Path, "table", "set", source, "Parents", "--key", "2", "--column", "code", "--value", "3", "--format", "json");
        Assert.Equal(0, valid.ExitCode);
        using JsonDocument json = JsonDocument.Parse(valid.StdOut);
        Assert.Equal(2, json.RootElement.GetProperty("selection").GetProperty("value").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("changed").GetProperty("newValue").GetInt32());
    }
}
