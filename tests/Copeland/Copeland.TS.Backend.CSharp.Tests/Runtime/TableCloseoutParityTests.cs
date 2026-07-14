using System.Diagnostics;
using System.Text;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests.Runtime;

public sealed class TableCloseoutParityTests
{
    [Fact]
    public async Task Asset_backed_table_executes_with_csharp_node_parity_and_one_singleton()
    {
        const string source = """
            const $schema: string = "copeland://tests/table-runtime";
            record Point { x: number; }
            enum State { Missing, Named(label: string), }
            record table Samples from tsonAsset("./samples.obj.ts") {
                score: number;
                label: string;
                point: Point;
                state: State;
                values: number[][];
            }
            function score(): number { return Samples.score[0]!; }
            function label(): string { return Samples.label[0]!; }
            function point(): number { return Samples.point[0]!.x; }
            function state(): string { return match Samples.state[0]! { Missing => "missing", Named(label) => label, }; }
            function values(): number[][] { return Samples.values[0]!; }
            function first(): Samples { return Samples; }
            function second(): Samples { return Samples; }
            """;
        const string asset = """
            const $schema: string = "copeland://tests/table-runtime";
            record Point { x: number; }
            enum State { Missing, Named(label: string), }
            record table Samples {
                score: number = [$number("8000000000000000")];
                label: string = ["雪 😀"];
                point: Point = [{ x: 42 }];
                state: State = [State.Named("ready")];
                values: number[][] = [[[1, 2], [], [3]]];
            }
            const $value = Samples;
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(
            source,
            new CopelandCompilationOptions
            {
                SourcePath = "C:/project/main.ts",
                ProjectRoot = "C:/project",
                AssetSource = new RuntimeAssetSource(asset),
            });
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation javascript = JavaScriptBackend.Emit(compilation.MirCompilation.Program!);
        Assert.Empty(csharp.Diagnostics);
        Assert.True(javascript.Success, string.Join(Environment.NewLine, javascript.Diagnostics));
        Assert.DoesNotContain("samples.obj.ts", csharp.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("samples.obj.ts", javascript.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("tsonAsset", csharp.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("tsonAsset", javascript.SourceText, StringComparison.Ordinal);

        string script = javascript.SourceText + """
            console.log(Object.is(score(), -0));
            console.log(label());
            console.log(point());
            console.log(state());
            console.log(JSON.stringify(values()));
            console.log(first() === second());
            """;
        ProcessResult node = await RunNodeAsync(script);
        Assert.Equal("true\n雪 😀\n42\nready\n[[1,2],[],[3]]\ntrue\n", node.StdOut);
        Assert.Equal(string.Empty, node.StdErr);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(
            0x8000000000000000UL,
            BitConverter.DoubleToUInt64Bits(Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "score"))));
        Assert.Equal("雪 😀", Assert.IsType<string>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "label")));
        Assert.Equal(42d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "point")));
        Assert.Equal("ready", Assert.IsType<string>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "state")));
        var values = Assert.IsType<double[][]>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "values"));
        Assert.Equal([1d, 2d], values[0]);
        Assert.Empty(values[1]);
        Assert.Equal([3d], values[2]);
        Assert.Same(
            GeneratedModuleInvoker.Invoke(generated.Assembly!, "first"),
            GeneratedModuleInvoker.Invoke(generated.Assembly!, "second"));
    }

    [Fact]
    public void Csharp_parenthesizes_assignment_when_it_is_an_operand()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            function main(): number {
                let trace: number = 0;
                const value: number = (trace = trace * 10 + 2) - 12;
                return trace * 100 + value;
            }
            """);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        CSharpCompilation emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Assert.Contains("((trace = ((trace * 10.0) + 2.0)) - 12.0)", emitted.SourceText, StringComparison.Ordinal);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(190d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
    }

    [Fact]
    public async Task Csharp_and_javascript_execute_the_adversarial_table_matrix_deterministically()
    {
        const string source = """
            record Point { x: number; y: number; }
            enum Flag { None, Value(value: number), }
            record table Empty { value: number = []; }
            record table Values {
                value: [-0, 10, 20];
                text: string = ["quote \" slash \\ newline\n", "middle", "last"];
                enabled: boolean = [true, false, true];
                point: Point = [{ x: 1, y: 2 }, { x: 3, y: 4 }, { x: 5, y: 6 }];
                flag: Flag = [Flag.Value(7), Flag.None, Flag.Value(9)];
                result: number ! string = [ok(11), err("no"), ok(13)];
                nested: Flag ! string = [ok(Flag.Value(14)), err("nested"), ok(Flag.None)];
            }
            record table SameShape { value: [-0, 10, 20]; }

            function receiver(marker: number): Values { return Values; }
            function combine(earlier: number, row: Values.Row): number { return earlier * 100 + row.point.y; }
            function inspect(value: number ! TableBoundsError): number {
                return match value {
                    ok(item) => item,
                    err(error) => match error {
                        InvalidIndex(index) => 1000,
                        OutOfBounds(index, rowCount) => 2000 + rowCount,
                    },
                };
            }
            function inspectRow(value: Values.Row ! TableBoundsError): number {
                return match value {
                    ok(item) => 0,
                    err(error) => match error {
                        InvalidIndex(index) => 1000,
                        OutOfBounds(index, rowCount) => 2000 + rowCount,
                    },
                };
            }
            function access(index: number): number ! TableBoundsError { return Values.value[index]; }
            function forward(index: number): number ! TableBoundsError { return Values.value[index]?; }
            function recover(index: number): number {
                return try { Values.value[index]? } except (error) {
                    match error { InvalidIndex(value) => 3000, OutOfBounds(value, count) => 4000 + count, }
                };
            }
            function row(index: number): Values.Row ! TableBoundsError { return Values[index]; }
            function empty(index: number): number { return inspect(Empty.value[index]); }
            function selectedOnly(): number { return if true { Values.value[1]! } else { Values.value[-1]! }; }
            function shortCircuitOnly(): boolean { return false && Values.value[-1]! == 0; }
            function receiverOrderOnly(): number {
                let trace: number = 0;
                const row: Values.Row = receiver(trace = trace * 10 + 1)[(trace = trace * 10 + 2) - 12]!;
                return trace + row.value;
            }
            function argumentOrderOnly(): number {
                let trace: number = 12;
                const value: number = combine(trace = trace * 10 + 3, receiver(trace = trace * 10 + 4)[(trace = trace * 10 + 5) - 12345]!);
                return trace + value;
            }
            function matrix(): number {
                let trace: number = 0;
                const stable: Values = Values;
                const values: column number = Values.value;
                const first: Values.Row = receiver(trace = trace * 10 + 1)[(trace = trace * 10 + 2) - 12]!;
                const middle: Values.Row = Values[1]!;
                const last: Values.Row = Values[2]!;
                const ordered: number = combine(trace = trace * 10 + 3, receiver(trace = trace * 10 + 4)[(trace = trace * 10 + 5) - 12345]!);
                const cells: number = values[0]! + values[1]! + values[2]!;
                const projected: number = first.point.x + middle.point.y + last.point.x;
                const booleans: number = if stable.enabled[0]! { 1 } else { 0 };
                const strings: number = if Values.text[0]! == "quote \" slash \\ newline\n" { 1 } else { 0 };
                const enumValue: number = match Values.flag[0]! { Value(value) => value, None => 0, };
                const resultValue: number = match Values.result[0]! { ok(value) => value, err(error) => 0, };
                const nestedValue: number = match Values.nested[0]! {
                    ok(flag) => match flag { Value(value) => value, None => 0, },
                    err(error) => 0,
                };
                const propagated: number = forward(2)!;
                const recovered: number = recover(-1);
                const selected: number = if true { Values.value[1]! } else { Values.value[-1]! };
                const shortCircuit: boolean = false && Values.value[-1]! == 0;
                const shortValue: number = if shortCircuit { 0 } else { 1 };
                return trace + ordered + cells + projected + booleans + strings + enumValue + resultValue + nestedValue + propagated + recovered + selected + shortValue;
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        JavaScriptCompilation firstJavaScript = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation secondJavaScript = JavaScriptBackend.Emit(compilation.MirCompilation.Program!);
        CSharpCompilation firstCSharp = CSharpBackend.Emit(compilation.MirCompilation.Program!);
        CSharpCompilation secondCSharp = CSharpBackend.Emit(compilation.MirCompilation.Program!);

        Assert.True(firstJavaScript.Success, string.Join(Environment.NewLine, firstJavaScript.Diagnostics));
        Assert.Equal(firstJavaScript.SourceText, secondJavaScript.SourceText);
        Assert.Empty(firstCSharp.Diagnostics);
        Assert.Equal(firstCSharp.SourceText, secondCSharp.SourceText);
        Assert.DoesNotContain("COPE-CS-TABLE-0001", firstCSharp.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("COPE-JS-TABLE-0001", firstJavaScript.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("catch", firstJavaScript.SourceText, StringComparison.Ordinal);

        string script = firstJavaScript.SourceText + """
            console.log(matrix());
            console.log(Object.is(inspect(access(0)), -0));
            console.log(inspect(access(1)));
            console.log(inspect(access(2)));
            console.log(Object.is(inspect(access(-0)), -0));
            console.log(inspect(access(0.5)));
            console.log(inspect(access(NaN)));
            console.log(inspect(access(Infinity)));
            console.log(inspect(access(-Infinity)));
            console.log(inspect(access(-1)));
            console.log(inspect(access(3)));
            console.log(inspect(access(9007199254740991)));
            console.log(inspectRow(row(3)));
            console.log(empty(0));
            console.log(recover(NaN));
            """;

        ProcessResult firstNode = await RunNodeAsync(script);
        ProcessResult secondNode = await RunNodeAsync(script);
        Assert.Equal("28755\ntrue\n10\n20\ntrue\n1000\n1000\n1000\n1000\n2003\n2003\n2003\n2003\n2000\n3000\n", firstNode.StdOut);
        Assert.Equal(string.Empty, firstNode.StdErr);
        Assert.Equal(firstNode, secondNode);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(firstCSharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(10d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "selectedOnly")));
        Assert.False(Assert.IsType<bool>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "shortCircuitOnly")));
        Assert.Equal(12d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "receiverOrderOnly")));
        Assert.Equal(24647d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "argumentOrderOnly")));
        Assert.Equal(28755d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "matrix")));
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(Inspect(Invoke(generated, "access", 0d))));
        Assert.Equal(10d, Inspect(Invoke(generated, "access", 1d)));
        Assert.Equal(20d, Inspect(Invoke(generated, "access", 2d)));
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(Inspect(Invoke(generated, "access", -0d))));
        Assert.Equal(1000d, Inspect(Invoke(generated, "access", .5d)));
        Assert.Equal(1000d, Inspect(Invoke(generated, "access", double.NaN)));
        Assert.Equal(1000d, Inspect(Invoke(generated, "access", double.PositiveInfinity)));
        Assert.Equal(1000d, Inspect(Invoke(generated, "access", double.NegativeInfinity)));
        Assert.Equal(2003d, Inspect(Invoke(generated, "access", -1d)));
        Assert.Equal(2003d, Inspect(Invoke(generated, "access", 3d)));
        Assert.Equal(2003d, Inspect(Invoke(generated, "access", 9007199254740991d)));
        Assert.Equal(2003d, InspectRow(generated, Invoke(generated, "row", 3d)));
        Assert.Equal(2000d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "empty", 0d)));
        Assert.Equal(3000d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "recover", double.NaN)));
    }

    private static object? Invoke(RoslynCompileResult generated, string name, params object[] arguments)
    {
        Type module = generated.Assembly!.GetType("Copeland.Generated.CopelandModule")!;
        return module.GetMethod(name)!.Invoke(null, arguments);
    }

    private static double Inspect(object? value)
    {
        Type module = value!.GetType().Assembly.GetType("Copeland.Generated.CopelandModule")!;
        return Assert.IsType<double>(module.GetMethod("inspect")!.Invoke(null, [value]));
    }

    private static double InspectRow(RoslynCompileResult generated, object? value)
    {
        Type module = generated.Assembly!.GetType("Copeland.Generated.CopelandModule")!;
        return Assert.IsType<double>(module.GetMethod("inspectRow")!.Invoke(null, [value]));
    }

    private static async Task<ProcessResult> RunNodeAsync(string source)
    {
        string directory = Path.Combine(Path.GetTempPath(), "copeland-table-closeout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "program.js");
        try
        {
            await File.WriteAllTextAsync(scriptPath, source, new UTF8Encoding(false));
            var startInfo = new ProcessStartInfo("node")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(scriptPath);
            using Process process = Process.Start(startInfo)!;
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, $"Node exit code: {process.ExitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}");
            return new ProcessResult(stdout, stderr);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class RuntimeAssetSource(string sourceText) : ICopelandAssetSource
    {
        public bool TryRead(string normalizedPath, out string? source)
        {
            source = normalizedPath.EndsWith("samples.obj.ts", StringComparison.OrdinalIgnoreCase)
                ? sourceText
                : null;
            return source is not null;
        }
    }

    private sealed record ProcessResult(string StdOut, string StdErr);
}
