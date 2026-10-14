using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.InstinctCorpus;
using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TableRecalculationTests
{
    private const string Relations = """
        record table Products { key id: int = [100, 200]; cents: int = [3, 5]; }
        record table Stock {
            key reference productId: int -> Products.id = [200, 100];
            units: int = [4, 2];
        }
        record table Base = derive Stock as stock {
            productId: int = stock.productId;
            units: int = stock.units;
        }
        record table Left = derive Base as base {
            productId: int = base.productId;
            units: int = base.units;
        }
        record table Right = derive Base as base {
            productId: int = base.productId;
            units: int = base.units * 2;
        }
        record table Plan = derive Products as product
            join Left as left through left.productId
            join Right as right through right.productId {
            productId: int = product.id;
            cents: int = left.units * product.cents + right.units;
        }
        """;

    [Fact]
    public void Recalculation_rebuilds_a_shared_derived_dependency_once_and_preserves_global_tables()
    {
        string source = Relations + """
            function preview(stock: Stock, products: Products): Plan {
                return Plan.recalculate({ Stock: stock, Products: products });
            }
            function run(): int[] {
                const revised: Stock = Stock with { units: [6, 7] };
                const prices: Products = Products with { cents: [4, 8] };
                const stockOnly: Plan = Plan.recalculate({ Stock: revised });
                const both: Plan = preview(revised, prices);
                return [Plan.cents.sum(), stockOnly.cents.sum(), both.cents.sum(),
                    both[0]!.productId, both[0]!.cents, both[1]!.productId, both[1]!.cents,
                    Stock.units.sum(), Products.cents.sum(), Base.units.sum()];
            }
            """;
        CopelandCompilation compilation = Compile(source);
        Assert.Equal([38, 77, 102, 100, 42, 200, 60, 6, 8, 6], Run(compilation));
        MirProgram program = compilation.MirCompilation!.Program!;
        MirFunction preview = program.Functions.Single(function => function.Name == "preview");
        var recalculation = Assert.IsType<MirTableRecalculationExpression>(Assert.IsType<MirReturnStatement>(preview.Body.Single()).Expression);
        Assert.Equal(["Base", "Left", "Right", "Plan"], recalculation.Steps.Select(step => program.Tables.Single(table => table.Id == step.TableId).Name));
        Assert.Contains("table-recalculate", compilation.MirText);
        CopelandCompilation inline = Compile(Relations + """
            function cents(row: Plan.Row): int { return row.cents; }
            function run(): int[] {
                return [
                    Plan.recalculate({ Stock: Stock with { units: [6, 7] } }).cents.sum(),
                    Plan.recalculate({ Stock: Stock with { units: [6, 7] } }).rows().select(cents).length,
                    Plan.recalculate({ Stock: Stock with { units: [6, 7] } }).rows().select(cents)[0],
                    Plan.cents.sum()
                ];
            }
            """);
        Assert.Equal([77, 2, 35, 38], Run(inline));
    }

    [Fact]
    public void Written_inputs_evaluate_once_in_order_and_untaken_branches_do_not_recalculate()
    {
        string source = Relations + """
            function stock(calls: MutableArray<int>): Stock {
                calls[0] = calls[0] * 10 + 1;
                return Stock with { units: [6, 7] };
            }
            function products(calls: MutableArray<int>): Products {
                calls[0] = calls[0] * 10 + 2;
                return Products with { cents: [4, 8] };
            }
            function cents(row: Plan.Row): int { return row.cents; }
            function run(): int[] {
                const calls: MutableArray<int> = MutableArray<int>(1);
                const revised: Plan = Plan.recalculate({ Stock: stock(calls), Products: products(calls) });
                const skipped: boolean = false && Plan.recalculate({ Stock: stock(calls) }).cents.sum() > 0;
                const skippedLength: boolean = false && Plan.recalculate({ Stock: stock(calls) }).rows().select(cents).length > 0;
                const skippedElement: boolean = false && Plan.recalculate({ Stock: stock(calls) }).rows().select(cents)[0] > 0;
                const chosen: Plan = if true { revised } else { Plan.recalculate({ Stock: stock(calls) }) };
                return [revised.cents.sum(), calls[0], chosen.cents.sum()];
            }
            """;
        Assert.Equal([102, 12, 102], Run(Compile(source)));
    }

    [Fact]
    public void Recalculated_rows_support_captured_filters_typed_summaries_and_generic_helpers()
    {
        string source = Relations + """
            record Total { cents: int; lines: int; }
            function preview<T>(stock: Stock, witness: T): Plan {
                return Plan.recalculate({ Stock: stock });
            }
            function run(): int[] {
                const revised: Stock = Stock with { units: [6, 7] };
                const plan: Plan = preview<int>(revised, 1);
                const minimum: int = 40;
                const total: Total = plan.rows().where(capture { minimum } row => row.cents >= minimum)
                    .summarize<Total>(row => ({ cents: sum(row.cents), lines: count() }));
                return [total.cents, total.lines, Plan.cents.sum()];
            }
            """;
        Assert.Equal([42, 1, 38], Run(Compile(source)));
    }

    [Theory]
    [InlineData("Stock.recalculate({ Stock: Stock })", "COPE-RECALC-0001")]
    [InlineData("Plan.recalculate()", "COPE-RECALC-0002")]
    [InlineData("Plan.recalculate({})", "COPE-RECALC-0002")]
    [InlineData("Plan.recalculate({ Stock: Stock }, { Products: Products })", "COPE-RECALC-0002")]
    [InlineData("Plan.recalculate(Stock)", "COPE-RECALC-0002")]
    [InlineData("Plan.recalculate({ missing: Stock })", "COPE-RECALC-0003")]
    [InlineData("Plan.recalculate({ Stock: Stock, Stock: Stock })", "COPE-RECALC-0003")]
    [InlineData("Plan.recalculate({ Left: Left })", "COPE-RECALC-0003")]
    [InlineData("Plan.recalculate({ Stock: Products })", "COPE-RECALC-0004")]
    [InlineData("Plan.recalculate({ Stock: 1 })", "COPE-RECALC-0004")]
    public void Invalid_recalculation_inputs_have_directed_diagnostics(string expression, string expected)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(Relations + "function run(): Plan { return " + expression + "; }");
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == expected);
    }

    [Fact]
    public void Recalculation_on_a_snapshot_is_rejected_in_favor_of_the_declared_plan()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(Relations + """
            function run(): Plan { const plan: Plan = Plan; return plan.recalculate({ Stock: Stock }); }
            """);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-RECALC-0001");
    }

    [Fact]
    public void Direct_async_materialization_and_all_javascript_profiles_have_named_boundaries()
    {
        CopelandCompilation asynchronous = CopelandCompiler.CompileToMir(Relations + """
            async function run(): Plan { return Plan.recalculate({ Stock: Stock }); }
            """);
        Assert.Contains(asynchronous.Diagnostics, diagnostic => diagnostic.Id == "COPE-RECALC-0005");
        CopelandCompilation compilation = Compile(Relations + """
            function run(): Plan { return Plan.recalculate({ Stock: Stock }); }
            """);
        foreach (JavaScriptEmissionProfile profile in Enum.GetValues<JavaScriptEmissionProfile>())
        {
            var result = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!, new JavaScriptEmissionOptions { Profile = profile });
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-JS-DERIVE-0001");
        }
    }

    [Fact]
    public void Mir_rejects_missing_inputs_wrong_nominal_snapshots_and_forged_dependency_order()
    {
        CopelandCompilation compilation = Compile(Relations + """
            function preview(): Plan { return Plan.recalculate({ Stock: Stock }); }
            """);
        MirProgram program = compilation.MirCompilation!.Program!;
        MirFunction function = program.Functions.Single(function => function.Name == "preview");
        var original = Assert.IsType<MirTableRecalculationExpression>(Assert.IsType<MirReturnStatement>(function.Body.Single()).Expression);
        MirTableRecalculationExpression[] corrupted =
        [
            original with { Inputs = original.Inputs.Skip(1).ToArray() },
            original with { Inputs = [original.Inputs[0] with { Value = original.Inputs[1].Value }, original.Inputs[1]] },
            original with { Steps = original.Steps.Reverse().ToArray() },
            original with { Steps = [.. original.Steps, original.Steps[0]] },
            original with { Steps = original.Steps.Select(step => step with { Inputs = [] }).ToArray() }
        ];
        foreach (MirTableRecalculationExpression expression in corrupted)
        {
            var invalidFunction = new MirFunction(function.Name, function.Parameters, function.ReturnType,
                function.Locals, [new MirReturnStatement(expression)]);
            var invalid = new MirProgram(program.Enums, program.Records, program.Tables, [invalidFunction]);
            Assert.Contains(MirValidator.Validate(invalid), diagnostic => diagnostic.Message.Contains("Recalculation"));
        }
    }

    [Fact]
    public void Incompatible_key_snapshots_fail_the_existing_join_guard_without_changing_globals()
    {
        CopelandCompilation compilation = Compile(Relations + """
            function incompatible(): Plan {
                return Plan.recalculate({ Products: Products with { id: [100, 300] } });
            }
            function run(): int[] { return [Plan.cents.sum(), Products[1]!.id]; }
            """);
        var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(emitted.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        var failure = Assert.Throws<System.Reflection.TargetInvocationException>(() => module.GetMethod("incompatible")!.Invoke(null, null));
        var invariant = Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Contains("could not resolve its declared reference", invariant.Message);
        Assert.Equal([38, 200], (int[])module.GetMethod("run")!.Invoke(null, null)!);
    }

    private static CopelandCompilation Compile(string source)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        return compilation;
    }

    private static int[] Run(CopelandCompilation compilation)
    {
        var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(emitted.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        return (int[])module.GetMethod("run")!.Invoke(null, null)!;
    }
}
