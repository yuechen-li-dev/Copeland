using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.InstinctCorpus;
using Copeland.TS.Mir;
using Copeland.TS.Semantics.Bound;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TableSummaryTests
{
    [Fact]
    public void Typed_totals_and_grouped_summaries_execute_with_filters_empty_inputs_and_declared_field_order()
    {
        const string source = """
            record Totals { lines: int; units: int; cents: int; }
            record Supplier { id: int; label: string; units: int; cents: int; lines: int; }
            record table Orders {
                supplier: int = [2, 1, 2, 3];
                label: string = ["B", "A", "B", "C"];
                units: int = [4, 0, 3, 5];
                cents: int = [12, 0, 9, 20];
            }
            function totals(): Totals {
                return Orders.rows().summarize<Totals>(row => ({
                    cents: sum(row.cents), units: sum(row.units), lines: count()
                }));
            }
            function suppliers(): Supplier[] {
                return Orders.rows().where(row => row.units > 0)
                    .groupBy(row => row.supplier, row => row.label)
                    .summarize<Supplier>(row => ({
                        id: row.supplier, label: row.label,
                        units: sum(row.units), cents: sum(row.cents), lines: count()
                    }));
            }
            function run(): int[] {
                const all: Totals = totals();
                const grouped: Supplier[] = suppliers();
                const empty: Totals = Orders.rows().where(row => row.units < 0)
                    .summarize(row => ({ lines: count(), units: sum(row.units), cents: sum(row.cents) }));
                const noGroups: Supplier[] = Orders.rows().where(row => row.units < 0)
                    .groupBy(row => row.supplier, row => row.label)
                    .summarize<Supplier>(row => ({ id: row.supplier, label: row.label, units: sum(row.units), cents: sum(row.cents), lines: count() }));
                return [all.lines, all.units, all.cents, grouped.length,
                    grouped[0].id, grouped[0].units, grouped[0].cents, grouped[0].lines,
                    grouped[1].id, grouped[1].units, grouped[1].cents, grouped[1].lines,
                    empty.lines, empty.units, empty.cents, noGroups.length];
            }
            """;
        CopelandCompilation compilation = Compile(source);
        var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(emitted.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        Assert.Equal([4, 12, 41, 2, 2, 7, 21, 2, 3, 5, 20, 1, 0, 0, 0, 0], (int[])module.GetMethod("run")!.Invoke(null, null)!);
        Assert.Contains("table-summary", compilation.MirText);
    }

    [Fact]
    public void Summary_of_a_snapshot_and_captured_filter_uses_the_passed_value_without_mutating_the_singleton()
    {
        const string source = """
            record Total { units: int; lines: int; }
            record table Stock { units: int = [2, 5]; }
            function summarize(stock: Stock, minimum: int): Total {
                return stock.rows().where(capture { minimum } row => row.units >= minimum)
                    .summarize<Total>(row => ({ units: sum(row.units), lines: count() }));
            }
            function run(): int[] {
                const revised: Stock = Stock with { units: [4, 7] };
                const total: Total = summarize(revised, 4);
                return [total.units, total.lines, Stock.units.sum()];
            }
            """;
        CopelandCompilation compilation = Compile(source);
        var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(emitted.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        Assert.Equal([11, 2, 7], (int[])module.GetMethod("run")!.Invoke(null, null)!);
    }

    [Theory]
    [InlineData("T.rows().summarize<S>((row): S => ({ total: count() }))", "COPE-SUMMARY-0003")]
    [InlineData("T.rows().summarize<Missing>(row => ({ total: count() }))", "COPE-SUMMARY-0002")]
    [InlineData("T.rows().summarize<int>(row => ({ total: count() }))", "COPE-SUMMARY-0002")]
    [InlineData("T.rows().summarize<S>(row => ({ missing: count() }))", "COPE-SUMMARY-0004")]
    [InlineData("T.rows().summarize<S>(row => ({ total: count(), total: count() }))", "COPE-SUMMARY-0004")]
    [InlineData("T.rows().summarize<S>(row => ({ total: sum(row.text) }))", "COPE-SUMMARY-0005")]
    [InlineData("T.rows().summarize<S>(row => ({ total: row.units }))", "COPE-SUMMARY-0008")]
    [InlineData("T.rows().summarize<S>(row => ({ total: sum(row.units + 1) }))", "COPE-SUMMARY-0008")]
    [InlineData("T.rows().summarize<S>(row => ({ total: average(row.units) }))", "COPE-SUMMARY-0007")]
    [InlineData("T.rows().summarize<S>(row => ({ total: count(row.units) }))", "COPE-SUMMARY-0007")]
    [InlineData("T.rows().summarize<S>(row => ({ total: sum() }))", "COPE-SUMMARY-0007")]
    [InlineData("T.rows().summarize<S>(row => ({ total: called() }))", "COPE-SUMMARY-0007")]
    [InlineData("T.rows().summarize<S>(row => { return { total: count() }; })", "COPE-SUMMARY-0003")]
    [InlineData("T.rows().groupBy().summarize<S>(row => ({ total: count() }))", "COPE-SUMMARY-0006")]
    [InlineData("T.rows().groupBy(row => row.units, row => row.units).summarize<S>(row => ({ total: count() }))", "COPE-SUMMARY-0006")]
    [InlineData("T.rows().groupBy(row => row.units + 1).summarize<S>(row => ({ total: count() }))", "COPE-SUMMARY-0008")]
    public void Invalid_summary_descriptors_have_directed_diagnostics(string expression, string expected)
    {
        string source = "record S { total: int; } record table T { units: int = [1]; text: string = [\"x\"]; } function called(): int { return 1; } function run(): S { return " + expression + "; }";
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == expected);
    }

    [Fact]
    public void Filter_captures_are_created_once_and_untaken_branches_do_not_evaluate_them()
    {
        const string source = """
            record Total { units: int; }
            record table Stock { units: int = [2, 5, 7]; }
            function next(calls: MutableArray<int>): int {
                calls[0] = calls[0] + 1;
                return calls[0];
            }
            function run(): int[] {
                const calls: MutableArray<int> = MutableArray<int>(1);
                const total: Total = Stock.rows()
                    .where(capture { minimum: next(calls) } row => row.units >= minimum)
                    .where(capture { maximum: next(calls) } row => row.units > maximum)
                    .summarize<Total>(row => ({ units: sum(row.units) }));
                const skipped: boolean = false && Stock.rows()
                    .where(capture { minimum: next(calls) } row => row.units >= minimum)
                    .summarize<Total>(row => ({ units: sum(row.units) })).units > 0;
                const alternative: Total = if true { total } else {
                    Stock.rows().where(capture { minimum: next(calls) } row => row.units >= minimum)
                        .summarize<Total>(row => ({ units: sum(row.units) }))
                };
                return [total.units, calls[0], alternative.units];
            }
            """;
        Assert.Equal([12, 2, 12], Run(source));
    }

    [Fact]
    public void Composite_scalar_keys_float_sums_and_contextual_grouped_results_execute()
    {
        const string source = """
            enum Kind { Food, Drink }
            record Group { amount: number; lines: int; }
            record table Items {
                label: string = ["b", "a", "b", "B"];
                active: boolean = [true, false, true, true];
                kind: Kind = [Kind.Food, Kind.Drink, Kind.Food, Kind.Food];
                amount: number = [1.5, 2.0, 3.5, 4.0];
            }
            function run(): int[] {
                const groups: Group[] = Items.rows()
                    .groupBy(row => row.label, row => row.active, row => row.kind)
                    .summarize(row => ({ amount: sum(row.amount), lines: count() }));
                const expected: number = 5.0;
                return [groups.length, groups[0].lines, groups[1].lines, groups[2].lines,
                    if groups[0].amount == expected { 1 } else { 0 }];
            }
            """;
        Assert.Equal([3, 2, 1, 1, 1], Run(source));
    }

    [Fact]
    public void Non_table_callable_member_named_summarize_keeps_ordinary_semantics()
    {
        const string source = """
            record Api { summarize: (value: int) => int; }
            function increment(value: int): int { return value + 1; }
            function run(): int[] {
                const api: Api = { summarize: increment };
                return [api.summarize(4)];
            }
            """;
        Assert.Equal([5], Run(source));
    }

    [Fact]
    public void Summary_fields_preserve_derived_column_relationship_provenance()
    {
        const string source = """
            record table Prices { key id: int = [1]; cents: int = [3]; }
            record table Orders { reference priceId: int -> Prices.id = [1]; units: int = [2]; }
            record table Cost = derive Orders as order join Prices as price through order.priceId {
                const cents: int = order.units * price.cents;
                total: int = cents;
            }
            record Total { cents: int; }
            function totals(): Total {
                return Cost.rows().summarize<Total>(row => ({ cents: sum(row.total) }));
            }
            """;
        CopelandCompilation compilation = Compile(source);
        MirFunction function = compilation.MirCompilation!.Program!.Functions.Single(function => function.Name == "totals");
        var summary = Assert.IsType<MirTableSummaryExpression>(Assert.IsType<MirReturnStatement>(function.Body.Single()).Expression);
        Assert.Equal(["price.cents", "units"], summary.Fields[0].Inputs);
        Assert.Equal(["Orders.priceId -> Prices.id"], summary.Fields[0].Relationships);
        var corrupt = summary with { Fields = [summary.Fields[0] with { Type = new MirNamedType("string") }] };
        var invalidFunction = new MirFunction(function.Name, function.Parameters, function.ReturnType,
            function.Locals, [new MirReturnStatement(corrupt)]);
        MirProgram program = compilation.MirCompilation.Program;
        var invalid = new MirProgram(program.Enums, program.Records, program.Tables, [invalidFunction]);
        Assert.Contains(MirValidator.Validate(invalid), diagnostic => diagnostic.Message.Contains("Summary"));
    }

    [Fact]
    public void JavaScript_profiles_report_the_named_summary_execution_boundary()
    {
        CopelandCompilation compilation = Compile("""
            record Total { units: int; }
            record table Stock { units: int = [1]; }
            function total(): Total {
                return Stock.rows().summarize<Total>(row => ({ units: sum(row.units) }));
            }
            """);
        foreach (JavaScriptEmissionProfile profile in Enum.GetValues<JavaScriptEmissionProfile>())
        {
            var result = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!, new JavaScriptEmissionOptions { Profile = profile });
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-JS-SUMMARY-0001");
        }
    }

    [Fact]
    public void Stored_row_views_and_standalone_grouping_have_a_directed_stop()
    {
        CopelandCompilation stored = CopelandCompiler.CompileToMir("""
            record Total { units: int; }
            record table Stock { units: int = [1]; }
            function total(): Total {
                const rows = Stock.rows();
                return rows.summarize<Total>(row => ({ units: sum(row.units) }));
            }
            """);
        Assert.Contains(stored.Diagnostics, diagnostic => diagnostic.Id == "COPE-SUMMARY-0010");
        CopelandCompilation grouping = CopelandCompiler.CompileToMir("""
            record table Stock { units: int = [1]; }
            function total(): int { const groups = Stock.rows().groupBy(row => row.units); return 0; }
            """);
        Assert.Contains(grouping.Diagnostics, diagnostic => diagnostic.Id == "COPE-SUMMARY-0010");
    }

    private static int[] Run(string source)
    {
        CopelandCompilation compilation = Compile(source);
        var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(emitted.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        return (int[])module.GetMethod("run")!.Invoke(null, null)!;
    }

    [Fact]
    public void Direct_summary_in_async_code_reports_a_repair_instead_of_backend_failure()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            record Total { units: int; }
            record table Stock { units: int = [1]; }
            async function total(): Total {
                return Stock.rows().summarize<Total>(row => ({ units: sum(row.units) }));
            }
            """);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-SUMMARY-0011");
    }

    private static CopelandCompilation Compile(string source)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        return compilation;
    }
}
