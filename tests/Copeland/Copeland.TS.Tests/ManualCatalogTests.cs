using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Copeland.TS.InstinctCorpus;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ManualCatalogTests
{
    [Fact]
    public void Unkeyed_catalog_preserves_queries_fallbacks_and_immutable_prices_on_all_backends()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData/Tables/ManualCatalog.ts"));
        var compilation = CopelandCompiler.CompileToMir(source + """

            function equalNumber(value: number, expected: number): int {
                return if value == expected { 1 } else { 0 };
            }
            function run(): int[] {
                const products: Products = workbookProducts();
                const original: Prices = workbookPrices();
                const revised: Prices = revisedPrices();
                const active: ProductView[] = activeProducts(products);
                return [
                    products.id.count(), active.length, retailCount(),
                    equalNumber(retailSum(), 115.0),
                    equalNumber(retailAverage(), 23.0),
                    equalNumber(retailMinimum(), 8.75),
                    equalNumber(retailMaximum(), 42.0),
                    equalNumber(inventoryValue(), 969.25),
                    if categoryNameFor(101) == "Coffee" { 1 } else { 0 },
                    if categoryNameFor(999) == "Unknown" { 1 } else { 0 },
                    equalNumber(retailFor(999), 0.0),
                    if stateLabel(100) == "active" { 1 } else { 0 },
                    if stateLabel(101) == "low-stock" { 1 } else { 0 },
                    if stateLabel(104) == "discontinued" { 1 } else { 0 },
                    if stateLabel(999) == "unknown" { 1 } else { 0 },
                    equalNumber(original.retail[1]!, 16.25),
                    equalNumber(revised.retail[1]!, 16.50),
                ];
            }
            """);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        var csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(csharp.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(csharp.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        int[] expected = [5, 3, 5, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1];
        Assert.Equal(expected, (int[])module.GetMethod("run")!.Invoke(null, null)!);
        TableJavaScriptParity.AssertRun(compilation, expected);
    }
}
