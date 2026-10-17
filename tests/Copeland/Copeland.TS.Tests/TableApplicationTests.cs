using System.Text.Json;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.InstinctCorpus;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TableApplicationTests
{
    [Fact]
    public async Task Table_snapshots_results_and_derived_metadata_cross_modules_without_exposing_private_names()
    {
        CopelandProjectSource[] sources =
        [
            new("Tables.ts", "Tables.ts", """
                export record table Stock { key id: int = [2, 1]; units: int = [3, 5]; }
                export record table Plan = derive Stock as stock { id: int = stock.id; units: int = stock.units * 2; }
                """),
            new("Application.ts", "Application.ts", """
                import { Stock, Plan } from "./Tables";
                export record Inputs { stock: Stock; }
                export function initial(): Inputs { return { stock: Stock }; }
                export function edit(inputs: Inputs, id: int, units: int): Inputs ! TableKeyError {
                    return match inputs.stock.withRow({ id }, { units }) {
                        ok(stock) => ok(inputs with { stock }),
                        err(error) => err(error),
                    };
                }
                export function total(inputs: Inputs): int {
                    return Plan.recalculate({ Stock: inputs.stock }).units.sum();
                }
                """),
            new("Frontend.ts", "Frontend.ts", """
                import { Inputs, initial, edit, total } from "./Application";
                export function run(): int[] {
                    const original: Inputs = initial();
                    const revised: Inputs = edit(original, 1, 12)!;
                    const missing: int = match edit(revised, 9, 0) {
                        ok(value) => 0,
                        err(error) => match error { TableKeyError.NotFound => 1, },
                    };
                    return [total(original), total(revised), missing, total(original)];
                }
                """),
        ];
        CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir(sources);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        var csharp = CSharpBackend.Emit(compilation.MirProjectGraph!.AggregateProgram);
        Assert.Empty(csharp.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(csharp.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        Assert.Equal([16, 30, 1, 16], (int[])module.GetMethod("run")!.Invoke(null, null)!);
        string directory = Path.Combine(Path.GetTempPath(), "copeland-table-modules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "package.json"), "{\"type\":\"module\"}");
            foreach (JavaScriptEmissionProfile profile in Enum.GetValues<JavaScriptEmissionProfile>())
            {
                var emitted = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph, new JavaScriptEmissionOptions { Profile = profile });
                Assert.Empty(emitted.Diagnostics);
                foreach ((string name, string content) in emitted.Files)
                {
                    string path = Path.Combine(directory, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, content);
                }
                string entry = JsonSerializer.Serialize(new Uri(Path.Combine(directory, "Frontend.js")).AbsoluteUri);
                string actual = await CorpusRunner.RunNode($"import {{ run }} from {entry};\nconsole.log(JSON.stringify(run()));");
                Assert.Equal("[16,30,1,16]", actual.Trim());
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Summary_int_overflow_and_payload_enum_keys_have_backend_parity()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            enum Kind { Named(label: string, size: int), Other, }
            record Totals { units: int; lines: int; }
            record table Values {
                kind: Kind = [Kind.Named("same", 2), Kind.Other, Kind.Named("same", 2)];
                units: int = [2147483647, 3, 1];
            }
            function run(): int[] {
                const groups: Totals[] = Values.rows().groupBy(row => row.kind)
                    .summarize<Totals>(row => ({ units: sum(row.units), lines: count() }));
                const total: Totals = Values.rows().summarize<Totals>(row => ({ units: sum(row.units), lines: count() }));
                return [groups.length, groups[0].units, groups[0].lines, groups[1].units, total.units];
            }
            """);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        var csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(csharp.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(csharp.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        int[] expected = [2, int.MinValue, 2, 3, int.MinValue + 3];
        Assert.Equal(expected, (int[])module.GetMethod("run")!.Invoke(null, null)!);
        TableJavaScriptParity.AssertRun(compilation, expected);
    }
    [Theory]
    [InlineData("source")]
    [InlineData("index")]
    [InlineData("values0")]
    [InlineData("lookup0")]
    [InlineData("joined0")]
    public async Task JavaScript_derivation_allocates_temporaries_separately_from_authored_relation_aliases(string alias)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir($$"""
            record table T { value: int = [7]; }
            record table P = derive T as {{alias}} { result: int = {{alias}}.value; }
            function run(): int { return P.result.sum(); }
            """);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        foreach (JavaScriptEmissionProfile profile in Enum.GetValues<JavaScriptEmissionProfile>())
        {
            var emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!, new JavaScriptEmissionOptions { Profile = profile });
            Assert.Empty(emitted.Diagnostics);
            Assert.Equal("7", (await CorpusRunner.RunNode(emitted.SourceText + "\nconsole.log(run());")).Trim());
        }
    }

    [Fact]
    public async Task Npm_integer_callback_contracts_reject_fractional_out_of_range_and_non_numeric_values()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            import { createElement } from "react";
            import { CountField } from "@controls";
            function make(): ReactNode {
                return <CountField value={0} onValueChange={(value: int) => {}} />;
            }
            """, new CopelandCompilationOptions
            {
                SourcePath = "Main.tsx",
                ProjectTypes = CopelandProjectTypeSet.ReactComponents,
                NpmDependencies = new CopelandNpmDependencyGraph([
                    new("react", "19.2.7", [new("createElement", [], "ReactNode")]),
                    new("@controls", "0.1.0", [], Components: [new("CountField", Properties:
                        [new("value", "int", true), new("onValueChange", "(int)=>void", true)])]),
                ]),
            });
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        foreach (JavaScriptEmissionProfile profile in Enum.GetValues<JavaScriptEmissionProfile>())
        {
            var emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!, new JavaScriptEmissionOptions { Profile = profile });
            Assert.Empty(emitted.Diagnostics);
            string source = string.Join("\n", emitted.SourceText!.Split('\n').Where(line => !line.StartsWith("import ", StringComparison.Ordinal)));
            source = "function createElement(type, props) { return props; } const CountField = 0;\n" + source + """

                const callback = make().onValueChange;
                callback(-2147483648);
                callback(2147483647);
                let failures = 0;
                for (const invalid of [1.5, 2147483648, NaN, "1"]) {
                    try { callback(invalid); } catch (error) {
                        if (!String(error).includes("expected int callback argument")) throw error;
                        failures += 1;
                    }
                }
                console.log(failures);
                """;
            Assert.Equal("4", (await CorpusRunner.RunNode(source)).Trim());
        }
    }

}
