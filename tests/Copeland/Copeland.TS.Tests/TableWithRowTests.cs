using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.InstinctCorpus;
using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TableWithRowTests
{
    private const string Tables = """
        record table Products { key id: int = [100, 200]; }
        record table Stock {
            key reference productId: int -> Products.id = [200, 100];
            units: int = [4, 2];
            label: string = ["second", "first"];
        }
        """;

    [Fact]
    public async Task Keyed_edits_preserve_identity_order_and_original_and_report_missing_keys()
    {
        string source = Tables + """
            function edit(stock: Stock, productId: int, units: int): Stock ! TableKeyError {
                return stock.withRow({ productId }, { units, label: "edited" });
            }
            function run(): int[] {
                const revised: Stock = edit(Stock, 100, 12)!;
                const negative: Stock = edit(revised, 200, -1)!;
                const missing: int = match edit(revised, 999, 3) {
                    ok(value) => 0,
                    err(error) => match error { TableKeyError.NotFound => 1, },
                };
                return [Stock.units.sum(), revised.units.sum(), revised[0]!.productId,
                    revised[0]!.units, revised[1]!.productId, revised[1]!.units,
                    negative.units.sum(), missing];
            }
            """;
        CopelandCompilation compilation = Compile(source);
        Assert.Equal([6, 16, 200, 4, 100, 12, 11, 1], RunCSharp(compilation));
        foreach (JavaScriptEmissionProfile profile in Enum.GetValues<JavaScriptEmissionProfile>())
        {
            var emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!, new JavaScriptEmissionOptions { Profile = profile });
            Assert.Empty(emitted.Diagnostics);
            Assert.Equal("[6,16,200,4,100,12,11,1]", (await CorpusRunner.RunNode(emitted.SourceText + "\nconsole.log(JSON.stringify(run()));")).Trim());
        }
    }

    [Fact]
    public void Operands_evaluate_once_in_written_order_even_for_missing_keys_and_only_in_taken_branches()
    {
        CopelandCompilation compilation = Compile(Tables + """
            function source(calls: MutableArray<int>): Stock { calls[0] = calls[0] * 10 + 1; return Stock; }
            function key(calls: MutableArray<int>): int { calls[0] = calls[0] * 10 + 2; return 999; }
            function units(calls: MutableArray<int>): int { calls[0] = calls[0] * 10 + 3; return 12; }
            function label(calls: MutableArray<int>): string { calls[0] = calls[0] * 10 + 4; return "changed"; }
            function run(): int[] {
                const calls: MutableArray<int> = MutableArray<int>(1);
                const result: Stock ! TableKeyError = source(calls).withRow({ productId: key(calls) }, { units: units(calls), label: label(calls) });
                const skipped: boolean = false && source(calls).withRow({ productId: key(calls) }, { units: units(calls) })!.units.sum() > 0;
                return [calls[0], Stock.units.sum()];
            }
            """);
        Assert.Equal([1234, 6], RunCSharp(compilation));
        TableJavaScriptParity.AssertRun(compilation, [1234, 6]);
    }

    [Theory]
    [InlineData("Stock.withRow({ productId: 100 }, { units: 1, units: 2 })", "COPE-ROW-0004")]
    [InlineData("Stock.withRow({ productId: 100 }, { missing: 1 })", "COPE-ROW-0004")]
    [InlineData("Stock.withRow({ productId: 100 }, { units: true })", "COPE-ROW-0003")]
    [InlineData("Stock.withRow({ productId: 100.0 }, { units: 1 })", "COPE-ROW-0003")]
    [InlineData("Stock.withRow({ units: 100 }, { units: 1 })", "COPE-ROW-0002")]
    [InlineData("Stock.withRow({ productId: 100 }, {})", "COPE-ROW-0002")]
    [InlineData("Stock.withRow({ productId: 100 }, { productId: 200 })", "COPE-ROW-0005")]
    public void Invalid_patches_have_directed_compile_diagnostics(string expression, string diagnostic)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(Tables + $"function edit(): Stock ! TableKeyError {{ return {expression}; }}");
        Assert.Contains(compilation.Diagnostics, item => item.Id == diagnostic);
    }

    [Fact]
    public void Generic_patches_support_dynamic_immutable_cells_and_string_keys()
    {
        CopelandCompilation compilation = Compile("""
            enum Status { Ready, Paused, }
            record Detail { label: string; }
            record table Items {
                key code: string = ["b", "a"];
                detail: Detail = [{ label: "before" }, { label: "before" }];
                status: Status = [Status.Ready, Status.Ready];
                amount: int = [1, 2];
            }
            function change<T>(items: Items, code: string, witness: T, value: int): Items ! TableKeyError {
                return items.withRow({ code }, { detail: { label: "after" }, status: Status.Paused, amount: value });
            }
            function run(): int[] {
                const revised: Items = change<int>(Items, "a", 1, 7)!;
                return [revised[1]!.amount, Items[1]!.amount,
                    match revised[1]!.status { Ready => 0, Paused => 1, },
                    if revised[1]!.detail.label == "after" { 1 } else { 0 }];
            }
            """);
        Assert.Equal([7, 2, 1, 1], RunCSharp(compilation));
        TableJavaScriptParity.AssertRun(compilation, [7, 2, 1, 1]);
    }

    [Fact]
    public void Mir_cannot_forge_key_identity_protected_fields_or_the_error_contract()
    {
        CopelandCompilation compilation = Compile(Tables + """
            function edit(stock: Stock): Stock ! TableKeyError {
                return stock.withRow({ productId: 100 }, { units: 12 });
            }
            """);
        MirProgram program = compilation.MirCompilation!.Program!;
        MirFunction function = program.Functions.Single(item => item.Name == "edit");
        var original = Assert.IsType<MirTableWithRowExpression>(Assert.IsType<MirReturnStatement>(function.Body.Single()).Expression);
        MirTableWithRowExpression[] corruptions =
        [
            original with { Type = new MirNamedType("int") },
            original with { KeyColumnId = original.Replacements[0].ColumnId },
            original with { Key = new MirLiteralExpression("100", new MirNamedType("string")) },
            original with { Replacements = [] },
            original with { Replacements = [original.Replacements[0], original.Replacements[0]] },
            original with { Replacements = [new MirTableCellReplacement(original.KeyColumnId, original.Key)] },
        ];
        foreach (MirTableWithRowExpression expression in corruptions)
        {
            var invalidFunction = new MirFunction(function.Name, function.Parameters, function.ReturnType, function.Locals,
                [new MirReturnStatement(expression)]);
            var invalid = new MirProgram(program.Enums, program.Records, program.Tables, [invalidFunction]);
            Assert.Contains(MirValidator.Validate(invalid), diagnostic => diagnostic.Message.Contains("withRow"));
        }
        var missingError = new MirProgram(program.Enums.Where(item => item.Name != "TableKeyError").ToArray(),
            program.Records, program.Tables, [function]);
        Assert.Contains(MirValidator.Validate(missingError), diagnostic => diagnostic.Message.Contains("TableKeyError"));
    }

    [Theory]
    [InlineData("record table T { value: int = [1]; }", "COPE-ROW-0001")]
    [InlineData("record table S { key id: int = [1]; } record table T = derive S as row { id: int = row.id; value: int = 1; }", "COPE-ROW-0001")]
    [InlineData("record table S { key id: int = [1]; } record table T { key id: int = [1]; reference value: int -> S.id = [1]; }", "COPE-ROW-0005")]
    public void Derived_unkeyed_and_reference_updates_have_a_named_stop(string tables, string expected)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(tables + "function edit(): T ! TableKeyError { return T.withRow({ id: 1 }, { value: 1 }); }");
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == expected);
    }

    private static CopelandCompilation Compile(string source)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        return compilation;
    }

    private static int[] RunCSharp(CopelandCompilation compilation)
    {
        var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(emitted.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        return (int[])module.GetMethod("run")!.Invoke(null, null)!;
    }
}
