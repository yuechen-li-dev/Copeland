using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests.Runtime;

public sealed class GenericBackendParityTests
{
    [Fact]
    public async Task Closed_generic_matrix_has_csharp_node_parity_and_reuses_specializations()
    {
        const string source = """
            interface Positioned { x: number; y: number; }
            interface Named { name: string; }

            record Point { x: number; y: number; }
            record PersonPoint { x: number; y: number; name: string; }
            type AliasPoint = PersonPoint;

            record table Samples {
                x: [9];
                y: [10];
                name: string = ["row"];
            }

            function identity<T>(value: T): T { return value; }
            function chooseLeft<T, U>(left: T, right: U): T { return left; }
            function sum<T extends Positioned>(value: T): number { return value.x + value.y; }
            function describe<T extends Positioned & Named>(value: T): string {
                let seen: number = 0;
                for (let index: number = 0; index < value.x; index = index + 1) {
                    if (index == 1) { continue; }
                    seen = seen + 1;
                }

                if (seen > 1) {
                    return value.name;
                }

                return "small";
            }

            function relayArray<T>(value: T[]): T[] { return value; }
            function relayResult<T, E>(value: T ! E): T ! E { return value; }

            function mainNumber(): number { return identity<number>(42); }
            function mainString(): string { return identity<string>("value"); }
            function mainChoose(): number { return chooseLeft<number, string>(7, "x"); }
            function mainRecord(): number {
                const point: Point = { x: 20, y: 22 };
                return sum<Point>(point);
            }
            function mainExtra(): number {
                const point: PersonPoint = { x: 5, y: 6, name: "extra" };
                return sum<PersonPoint>(point);
            }
            function mainNamed(): string {
                const point: PersonPoint = { x: 3, y: 4, name: "named" };
                return describe<PersonPoint>(point);
            }
            function mainAlias(): string {
                const point: AliasPoint = { x: 3, y: 4, name: "alias" };
                return describe<AliasPoint>(point);
            }
            function mainRow(): string {
                const row: Samples.Row = Samples[0]!;
                return describe<Samples.Row>(row);
            }
            function mainArray(): number {
                const values: number[] = relayArray<number>([5]);
                return if true { 100 + 5 } else { 0 };
            }
            function mainResult(): number {
                return match relayResult<number, string>(ok(42)) {
                    ok(value) => value,
                    err(error) => 0,
                };
            }
            function mainReuse(): number {
                return identity<number>(1) + identity<number>(2);
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        JavaScriptCompilation javascript = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation symbolic = JavaScriptBackend.Emit(
            compilation.MirCompilation.Program!,
            new JavaScriptEmissionOptions { Profile = JavaScriptEmissionProfile.Symbolic });
        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation.Program!);
        Assert.True(javascript.Success, string.Join(Environment.NewLine, javascript.Diagnostics));
        Assert.True(symbolic.Success, string.Join(Environment.NewLine, symbolic.Diagnostics));
        Assert.Empty(csharp.Diagnostics);

        Assert.Single(Regex.Matches(compilation.MirText!, @"func identity__primitive_number__[0-9A-F]{16}\(").Cast<Match>());
        Assert.DoesNotContain("interface ", javascript.SourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Positioned", symbolic.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("TypeParameter", symbolic.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("RequirementFieldAccess", compilation.MirText!, StringComparison.Ordinal);

        const string expectedTrace = "42|value|7|42|11|named|alias|row|105|42|3\n";
        string nodeScript = javascript.SourceText + """
            console.log([
              mainNumber(),
              mainString(),
              mainChoose(),
              mainRecord(),
              mainExtra(),
              mainNamed(),
              mainAlias(),
              mainRow(),
              mainArray(),
              mainResult(),
              mainReuse()
            ].join("|"));
            """;
        ProcessResult firstNode = await RunNodeAsync(nodeScript);
        ProcessResult secondNode = await RunNodeAsync(nodeScript);
        ProcessResult symbolicNode = await RunNodeAsync(symbolic.SourceText + """
            console.log([
              mainNumber(),
              mainString(),
              mainChoose(),
              mainRecord(),
              mainExtra(),
              mainNamed(),
              mainAlias(),
              mainRow(),
              mainArray(),
              mainResult(),
              mainReuse()
            ].join("|"));
            """);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        string csharpTrace = string.Join(
            "|",
            [
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "mainNumber")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "mainString")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "mainChoose")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "mainRecord")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "mainExtra")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "mainNamed")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "mainAlias")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "mainRow")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "mainArray")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "mainResult")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "mainReuse")),
            ]) + "\n";

        Assert.Equal(expectedTrace, firstNode.StdOut);
        Assert.Equal(firstNode, secondNode);
        Assert.Equal(expectedTrace, symbolicNode.StdOut);
        Assert.Equal(expectedTrace, csharpTrace);
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            double number when number == Math.Truncate(number) => number.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
            double number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string text => text,
            _ => throw new InvalidOperationException("Unexpected runtime value.")
        };
    }

    private static async Task<ProcessResult> RunNodeAsync(string source)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".js");
        await File.WriteAllTextAsync(path, source, new UTF8Encoding(false));

        try
        {
            using var process = Process.Start(new ProcessStartInfo("node", "\"" + path + "\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("Failed to start Node.");
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
            Assert.Equal(string.Empty, stderr);
            return new ProcessResult(stdout.Replace("\r\n", "\n", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed record ProcessResult(string StdOut);
}
