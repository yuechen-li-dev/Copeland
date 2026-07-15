using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests.Runtime;

public sealed class NominalUnionBackendParityTests
{
    [Fact]
    public async Task Nominal_union_exact_once_and_contextual_injection_have_csharp_node_and_symbolic_parity()
    {
        const string source = """
            record Circle {
                radius: number;
                observed: number;
            }

            record Rectangle {
                width: number;
                height: number;
                observed: number;
            }

            record Coin {
                value: number;
            }

            record Badge {
                value: number;
            }

            type Shape = Circle | Rectangle;
            type Token = Coin | Badge;

            record Holder {
                shape: Shape;
            }

            enum Envelope {
                Value(shape: Shape),
            }

            function identity<T>(value: T): T {
                return value;
            }

            function selectLeft<T, U>(left: T, right: U): T {
                return left;
            }

            function variableInitTrace(): number {
                let trace: number = 0;
                const circle: Circle = { radius: trace = trace * 10 + 1, observed: trace };
                const shape: Shape = circle;
                return match shape {
                    Circle(value) => trace * 10 + value.observed,
                    Rectangle(value) => 0,
                };
            }

            function assignmentTrace(): number {
                let trace: number = 0;
                const circle: Circle = { radius: trace = trace * 10 + 1, observed: trace };
                let shape: Shape = circle;
                const rectangle: Rectangle = {
                    width: trace = trace * 10 + 2,
                    height: trace = trace * 10 + 3,
                    observed: trace,
                };
                shape = rectangle;
                return match shape {
                    Circle(value) => 0,
                    Rectangle(value) => trace * 10 + value.observed,
                };
            }

            function argumentTrace(): number {
                let trace: number = 0;
                const circle: Circle = { radius: trace = trace * 10 + 1, observed: trace };
                const shape: Shape = identity<Shape>(circle);
                return match shape {
                    Circle(value) => trace * 10 + value.observed,
                    Rectangle(value) => 0,
                };
            }

            function recordFieldTrace(): number {
                let trace: number = 0;
                const circle: Circle = { radius: trace = trace * 10 + 1, observed: trace };
                const holder: Holder = { shape: circle };
                return match holder.shape {
                    Circle(value) => trace * 10 + value.observed,
                    Rectangle(value) => 0,
                };
            }

            function enumPayloadTrace(): number {
                let trace: number = 0;
                const circle: Circle = { radius: trace = trace * 10 + 1, observed: trace };
                const envelope: Envelope = Envelope.Value(circle);
                return match envelope {
                    Value(shape) => match shape {
                        Circle(value) => trace * 10 + value.observed,
                        Rectangle(value) => 0,
                    },
                };
            }

            function arrayTrace(): number {
                let trace: number = 0;
                const circle: Circle = { radius: trace = trace * 10 + 1, observed: trace };
                const rectangle: Rectangle = {
                    width: trace = trace * 10 + 2,
                    height: trace = trace * 10 + 3,
                    observed: trace,
                };
                const shapes: Shape[] = [circle, rectangle];
                return trace;
            }

            function explicitGenericTrace(): number {
                let trace: number = 0;
                const circle: Circle = { radius: trace = trace * 10 + 1, observed: trace };
                const shape: Shape = identity<Shape>(circle);
                return match shape {
                    Circle(value) => trace * 10 + value.observed,
                    Rectangle(value) => 0,
                };
            }

            function explicitGenericOrderTrace(): number {
                let trace: number = 0;
                const circle: Circle = { radius: trace = trace * 10 + 1, observed: trace };
                const rectangle: Rectangle = {
                    width: trace = trace * 10 + 2,
                    height: trace = trace * 10 + 3,
                    observed: trace,
                };
                const shape: Shape = selectLeft<Shape, Shape>(circle, rectangle);
                return match shape {
                    Circle(value) => trace * 10 + value.observed,
                    Rectangle(value) => 0,
                };
            }

            function resultPropagationTrace(): number ! string {
                let trace: number = 0;
                const circle: Circle = { radius: trace = trace * 10 + 1, observed: trace };
                const value: Shape ! string = ok(circle);
                const shape: Shape = value?;
                return match shape {
                    Circle(payload) => trace * 10 + payload.observed,
                    Rectangle(payload) => 0,
                };
            }

            function resultPropagationValue(): number {
                return resultPropagationTrace()!;
            }

            function tryExceptTrace(): number {
                let trace: number = 0;
                return try {
                    const circle: Circle = { radius: trace = trace * 10 + 1, observed: trace };
                    const value: Shape ! string = ok(circle);
                    const shape: Shape = value?;
                    match shape {
                        Circle(payload) => trace * 10 + payload.observed,
                        Rectangle(payload) => 0,
                    }
                } except (error) {
                    0
                };
            }

            function directAlternativeValue(): number {
                const firstCircle: Circle = { radius: 7, observed: 7 };
                const secondRectangle: Rectangle = { width: 5, height: 6, observed: 6 };
                const first: Shape = firstCircle;
                const second: Shape = secondRectangle;
                return match first {
                    Circle(value) => value.radius,
                    Rectangle(value) => 0,
                } + match second {
                    Circle(value) => 0,
                    Rectangle(value) => value.width + value.height,
                };
            }

            function distinctSameShapeValue(): number {
                const badge: Badge = { value: 9 };
                const token: Token = badge;
                return match token {
                    Coin(value) => 0,
                    Badge(value) => value.value,
                };
            }

            function specializationReuseValue(): number {
                const circleValue: Circle = { radius: 4, observed: 4 };
                const rectangleValue: Rectangle = { width: 2, height: 3, observed: 3 };
                const first: Shape = identity<Shape>(circleValue);
                const second: Shape = identity<Shape>(rectangleValue);
                return match first {
                    Circle(value) => value.radius,
                    Rectangle(value) => 0,
                } + match second {
                    Circle(value) => 0,
                    Rectangle(value) => value.width + value.height,
                };
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

        Assert.DoesNotContain("MirUnion", compilation.MirText, StringComparison.Ordinal);
        Assert.DoesNotContain("UnionProvenance", compilation.MirText, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(csharp.SourceText, @"public static [^\r\n]+ identity__.*Shape__[0-9A-F]{16}\(").Cast<Match>());
        Assert.DoesNotContain("union", javascript.SourceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("union", symbolic.SourceText, StringComparison.OrdinalIgnoreCase);

        const string expected = "11|1353|11|11|11|123|11|1231|11|11|18|9|9\n";
        string runtimeScript = """
            console.log([
              variableInitTrace(),
              assignmentTrace(),
              argumentTrace(),
              recordFieldTrace(),
              enumPayloadTrace(),
              arrayTrace(),
              explicitGenericTrace(),
              explicitGenericOrderTrace(),
              resultPropagationValue(),
              tryExceptTrace(),
              directAlternativeValue(),
              distinctSameShapeValue(),
              specializationReuseValue()
            ].join("|"));
            """;

        ProcessResult firstNode = await RunNodeAsync(javascript.SourceText + runtimeScript);
        ProcessResult secondNode = await RunNodeAsync(javascript.SourceText + runtimeScript);
        ProcessResult symbolicNode = await RunNodeAsync(symbolic.SourceText + runtimeScript);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        string csharpTrace = string.Join(
            "|",
            [
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "variableInitTrace")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "assignmentTrace")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "argumentTrace")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "recordFieldTrace")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "enumPayloadTrace")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "arrayTrace")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "explicitGenericTrace")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "explicitGenericOrderTrace")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "resultPropagationValue")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "tryExceptTrace")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "directAlternativeValue")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "distinctSameShapeValue")),
                FormatValue(GeneratedModuleInvoker.Invoke(generated.Assembly!, "specializationReuseValue")),
            ]) + "\n";

        Assert.Equal(expected, firstNode.StdOut);
        Assert.Equal(firstNode, secondNode);
        Assert.Equal(expected, symbolicNode.StdOut);
        Assert.Equal(expected, csharpTrace);
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
            Assert.True(process.ExitCode == 0, stderr);
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
