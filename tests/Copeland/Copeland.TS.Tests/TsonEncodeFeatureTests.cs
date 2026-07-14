using Copeland.TS.Compiler;
using Copeland.TS.Semantics.Bound;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TsonEncodeFeatureTests
{
    private const string Schema = """
        const $schema: string = "copeland://tests/encode-feature";
        record Inner { text: string; }
        enum Choice { None, Value(inner: Inner), }
        record Root { enabled: boolean; count: number; inner: Inner; choice: Choice; }
        """;

    [Fact]
    public void Intrinsic_builds_one_deduplicated_reachable_plan_and_result_expression()
    {
        CopelandCompilation compilation = Compile(Schema + """
            function first(value: Root): string ! TsonEncodeError { return tsonEncode(value); }
            function second(value: Root): string ! TsonEncodeError {
                const encoded: string ! TsonEncodeError = tsonEncode(value);
                return encoded;
            }
            """);

        Assert.True(compilation.Success, Describe(compilation));
        BoundTsonEncodingPlan plan = Assert.Single(compilation.BoundCompilation!.Program.TsonEncodingPlans);
        Assert.Equal(new[] { "Choice", "Inner", "Root" }, plan.Definitions.Select(type => type.Name));
        Assert.Single(compilation.MirCompilation!.Program!.TsonEncodingPlans);
        Assert.Equal(2, Count(compilation.MirText!, "tson-encode [tson0]"));
        Assert.Contains("TsonEncodeError", compilation.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Intrinsic_composes_in_ordinary_result_expression_positions()
    {
        CopelandCompilation compilation = Compile(Schema + """
            function pass(value: string ! TsonEncodeError): string ! TsonEncodeError { return value; }
            function returned(value: Root): string ! TsonEncodeError { return tsonEncode(value); }
            function argument(value: Root): string ! TsonEncodeError { return pass(tsonEncode(value)); }
            function conditional(value: Root): string ! TsonEncodeError {
                return if true { tsonEncode(value) } else { tsonEncode(value) };
            }
            function matched(value: Root): string {
                return match tsonEncode(value) { ok(text) => text, err(error) => "failed", };
            }
            function propagated(value: Root): string ! TsonEncodeError {
                const text: string = tsonEncode(value)?;
                return text;
            }
            function unwrapped(value: Root): string { return tsonEncode(value)!; }
            function handled(value: Root): string {
                return try { tsonEncode(value)? } except (error) { "failed" };
            }
            """);

        Assert.True(compilation.Success, Describe(compilation));
        Assert.Single(compilation.MirCompilation!.Program!.TsonEncodingPlans);
    }

    [Theory]
    [InlineData("function bad(value: number): string ! TsonEncodeError { return tsonEncode(value); }", "COPE-TSON-ENCODE-0001")]
    [InlineData("function bad(value: Root): string ! TsonEncodeError { return tsonEncode(); }", "COPE-TSON-ENCODE-0001")]
    [InlineData("function bad(value: Root): string ! TsonEncodeError { return tsonEncode(value, value); }", "COPE-TSON-ENCODE-0001")]
    [InlineData("function bad(value: Root): void { tsonEncode; }", "COPE-TSON-ENCODE-0001")]
    [InlineData("function tsonEncode(value: Root): Root { return value; }", "COPE-TSON-ENCODE-0001")]
    [InlineData("function bad(tsonEncode: Root): Root { return tsonEncode; }", "COPE-TSON-ENCODE-0001")]
    [InlineData("record TsonEncodeError { value: string; }", "COPE-TSON-ENCODE-0001")]
    [InlineData("function TsonEncodeError(): void { return; }", "COPE-TSON-ENCODE-0001")]
    public void Malformed_intrinsic_use_has_stable_diagnostic(string source, string diagnosticId)
    {
        CopelandCompilation compilation = Compile(Schema + source);

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, diagnostic =>
            diagnostic.Id == diagnosticId && diagnostic.Length > 0);
    }

    [Theory]
    [InlineData("record Bad { value: number ! string; } function bad(value: Bad): string ! TsonEncodeError { return tsonEncode(value); }")]
    [InlineData("record table Values { value: [1]; } record Bad { value: Values.Row; } function bad(value: Bad): string ! TsonEncodeError { return tsonEncode(value); }")]
    public void Unsupported_reachable_types_are_compile_time_errors(string source)
    {
        CopelandCompilation compilation = Compile(
            "const $schema: string = \"copeland://tests/unsupported\"; " + source);

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSON-ENCODE-0003");
    }

    [Fact]
    public void Supported_nested_arrays_build_one_structural_plan()
    {
        CopelandCompilation compilation = Compile("""
            const $schema: string = "copeland://tests/arrays";
            record Item { name: string; }
            enum Choice { None, Some(item: Item), }
            record Batch { names: string[]; items: Item[]; choices: Choice[]; matrix: number[][]; }
            function encode(value: Batch): string ! TsonEncodeError { return tsonEncode(value); }
            """);

        Assert.True(compilation.Success, Describe(compilation));
        string mir = compilation.MirText!;
        Assert.Contains("string[]", mir, StringComparison.Ordinal);
        Assert.Contains("number[][]", mir, StringComparison.Ordinal);
        Assert.Single(compilation.MirCompilation!.Program!.TsonEncodingPlans);
    }

    [Fact]
    public void Table_intrinsic_requires_the_declaration_owned_singleton_and_builds_one_table_plan()
    {
        CopelandCompilation accepted = Compile("""
            const $schema: string = "copeland://tests/table-encode";
            record table Samples { active: boolean = [true, false]; score: number = [1, 2]; }
            function encode(): string ! TsonEncodeError { return tsonEncode(Samples); }
            """);

        Assert.True(accepted.Success, Describe(accepted));
        BoundTsonEncodingPlan plan = Assert.Single(accepted.BoundCompilation!.Program.TsonEncodingPlans);
        BoundTsonTablePlan table = Assert.IsType<BoundTsonTablePlan>(plan.TablePlan);
        Assert.Equal("Samples", table.TableType.Name);
        Assert.Equal(2, table.ExpectedRowCount);
        Assert.Equal(2, table.Columns.Count);

        CopelandCompilation rejected = Compile("""
            const $schema: string = "copeland://tests/table-encode";
            record table Samples { active: boolean = [true]; }
            function bad(): string ! TsonEncodeError {
                const copied: Samples = Samples;
                return tsonEncode(copied);
            }
            """);
        Assert.False(rejected.Success);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSON-ENCODE-0001");
    }

    [Fact]
    public void Asset_backed_table_singleton_builds_one_table_plan()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(
            """
            const $schema: string = "copeland://tests/table-encode-asset";
            record table Samples from tsonAsset("./samples.tson") {
                active: boolean;
                score: number;
            }
            function encode(): string ! TsonEncodeError { return tsonEncode(Samples); }
            """,
            new CopelandCompilationOptions
            {
                SourcePath = "C:/project/main.ts",
                ProjectRoot = "C:/project",
                AssetSource = new FileAssetSource(("C:/project/samples.tson", string.Join("\n",
                [
                    "const $schema: string = \"copeland://tests/table-encode-asset\";",
                    string.Empty,
                    "record table Samples {",
                    "    active: boolean = [",
                    "        true,",
                    "        false,",
                    "    ];",
                    "    score: number = [",
                    "        $number(\"3FF0000000000000\"),",
                    "        $number(\"4000000000000000\"),",
                    "    ];",
                    "}",
                    string.Empty,
                    "const $value = Samples;",
                    string.Empty,
                ]))),
            });

        Assert.True(compilation.Success, Describe(compilation));
        BoundTsonEncodingPlan plan = Assert.Single(compilation.BoundCompilation!.Program.TsonEncodingPlans);
        BoundTsonTablePlan table = Assert.IsType<BoundTsonTablePlan>(plan.TablePlan);
        Assert.Equal(2, table.ExpectedRowCount);
        Assert.Equal(["active", "score"], table.Columns.Select(column => column.Column.Name));
    }

    [Theory]
    [InlineData("""
        const $schema: string = "copeland://tests/table-encode";
        record table Samples { active: boolean = [true, false]; }
        function bad(): string ! TsonEncodeError { return tsonEncode(Samples[0]!); }
        """)]
    [InlineData("""
        const $schema: string = "copeland://tests/table-encode";
        record table Samples { active: boolean = [true, false]; }
        function bad(): string ! TsonEncodeError { return tsonEncode(Samples.active); }
        """)]
    [InlineData("""
        const $schema: string = "copeland://tests/table-encode";
        record table Samples { active: boolean = [true, false]; }
        function bad(): string ! TsonEncodeError { return tsonEncode(Samples.active[0]!); }
        """)]
    public void Table_intrinsic_rejects_row_column_and_cell_views(string source)
    {
        CopelandCompilation compilation = Compile(source);

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSON-ENCODE-0001");
    }

    [Fact]
    public void Missing_schema_is_a_compile_time_error()
    {
        CopelandCompilation compilation = Compile(
            "record Root { value: string; } function bad(value: Root): string ! TsonEncodeError { return tsonEncode(value); }");

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSON-ENCODE-0002");
    }

    [Fact]
    public void Program_without_intrinsic_has_no_plan_or_error_enum()
    {
        CopelandCompilation compilation = Compile("record Root { value: string; } function value(root: Root): string { return root.value; }");

        Assert.True(compilation.Success, Describe(compilation));
        Assert.Empty(compilation.MirCompilation!.Program!.TsonEncodingPlans);
        Assert.DoesNotContain(compilation.MirCompilation.Program.Enums, @enum => @enum.Name == "TsonEncodeError");
        Assert.DoesNotContain("tson", compilation.MirText!, StringComparison.OrdinalIgnoreCase);
    }

    private static CopelandCompilation Compile(string source)
        => CopelandCompiler.CompileToMir(source);

    private static string Describe(CopelandCompilation compilation)
        => string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}"));

    private sealed class FileAssetSource(params (string Path, string Text)[] files) : ICopelandAssetSource
    {
        private readonly Dictionary<string, string> _files = files.ToDictionary(
            file => Path.GetFullPath(file.Path),
            file => file.Text,
            StringComparer.OrdinalIgnoreCase);

        public bool TryRead(string normalizedPath, out string? sourceText)
            => _files.TryGetValue(Path.GetFullPath(normalizedPath), out sourceText);
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
