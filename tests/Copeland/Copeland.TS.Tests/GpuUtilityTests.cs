using Copeland.TS.Compiler;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Gpu.Wgsl;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class GpuUtilityTests
{
    private static string Source(string expression) => WgslGraphicsBackendTests.Source.Replace(
        "return Sqrt(x * x + y * y);", "return " + expression + ";", StringComparison.Ordinal);

    [Theory]
    [InlineData("when utility { case 1.0 when true score 3 case 2.0 when true score 4 else 0.0 }", 2.0)]
    [InlineData("when utility { case 1.0 when true score 4 case 2.0 when true score 4 case 3.0 when true score 2 else 0.0 }", 1.0)]
    [InlineData("when utility { case 1.0 when false score 100 case 2.0 when false score 200 else 9.0 }", 9.0)]
    [InlineData("when utility { case 1.0 when true score 0 case 2.0 when true score 0 else 9.0 }", 1.0)]
    [InlineData("when utility { case 1.0 when x > 0.0 score 2 + 3 case 2.0 when true score 4 else 0.0 }", 1.0)]
    [InlineData("when utility { case (when utility { case 7.0 when true score 1 else 0.0 }) when true score 1 else 0.0 }", 7.0)]
    public void SelectionLowersToOrdinaryControlFlow(string expression, double expected)
    {
        var bound = GpuGraphicsBinder.Compile(new GpuCompilationRequest([new("shader.v.ts", Source(expression))]));
        Assert.True(bound.Success, string.Join("; ", bound.Diagnostics.Select(d => d.Message)));
        var compiled = WgslGraphicsBackend.Lower(bound);
        Assert.True(compiled.Success, string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));
        Assert.DoesNotContain("when utility", compiled.Program!.Code);
        Assert.Contains(" > ", compiled.Program.Code);
        Assert.Equal(compiled.Program.Code, WgslGraphicsBackend.Lower(bound).Program!.Code);
        var functions = bound.Functions.ToDictionary(f => f.Name);
        Assert.Equal(expected, Execute(functions["radius"], new Dictionary<string, double> { ["x"] = 1, ["y"] = 2 }, functions));
    }

    [Theory]
    [InlineData("when utility { case 1.0 when true score 1.0 else 0.0 }")]
    [InlineData("when utility { case 1.0 when 1 score 1 else 0.0 }")]
    [InlineData("when utility { case true when true score 1 else 0.0 }")]
    [InlineData("when utility { else 0.0 }")]
    public void RejectsInvalidUtilityTypes(string expression)
    {
        var result = WgslGraphicsBackend.Compile(new GpuCompilationRequest([new("shader.v.ts", Source(expression))]));
        Assert.False(result.Success);
    }

    [Fact]
    public void LosingValuesAndIneligibleScoresAreNotEvaluated()
    {
        SelectionLowersToOrdinaryControlFlow(
            "when utility { case Sqrt(-1.0) when false score 100 case 2.0 when true score 1 else Sqrt(-1.0) }", 2);
    }

    [Fact]
    public void HostProfileRefusesUtilityWithAnExplicitDiagnostic()
    {
        var result = CopelandCompiler.CompileToMir("function Main(): number { return when utility { case 1 when true score 1 else 0 }; }");
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-UTILITY-PROFILE-0001");
    }

    [Fact]
    public void RejectsUnboundedCandidateListsAndExportsCompilerOwnedNames()
    {
        string expression = "when utility { " + string.Join(" ", Enumerable.Range(0, 17).Select(index => $"case 1.0 when true score {index}")) + " else 0.0 }";
        var rejected = WgslGraphicsBackend.Compile(new GpuCompilationRequest([new("shader.v.ts", Source(expression))]));
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-UTILITY-0001");
        var admitted = WgslGraphicsBackend.Compile(new GpuCompilationRequest([new("shader.v.ts", Source("when utility { case 1.0 when true score 1 else 0.0 }"))]));
        Assert.True(admitted.Success);
        Assert.Contains("fn " + admitted.Program!.FunctionNames["radius"] + "(", admitted.Program.Code);
    }

    // Execute the normalized MIR, independently of the utility binder's selection algorithm.
    private static double Execute(VdMirFunction function, Dictionary<string, double> scope, Dictionary<string, VdMirFunction> functions)
    {
        double Eval(VdMirExpression e) => e.Kind switch
        {
            "literal" => e.Value == "true" ? 1 : e.Value == "false" ? 0 : double.Parse(e.Value!, System.Globalization.CultureInfo.InvariantCulture),
            "name" => scope[e.Value!],
            "binary" => Binary(e.Value!, Eval(e.Operands![0]), Eval(e.Operands[1])),
            "call" => Execute(functions[e.Value!], functions[e.Value!].Parameters.Select((p, i) => (p.Name, Value: Eval(e.Operands![i]))).ToDictionary(p => p.Name, p => p.Value), functions),
            _ => throw new InvalidOperationException("Unexpected evaluated expression: " + e.Kind),
        };
        double? Statements(IReadOnlyList<VdMirStatement> statements)
        {
            foreach (var s in statements)
            {
                if (s.Kind is "local" or "assign") scope[s.Name!] = Eval(s.Expression!);
                else if (s.Kind == "return") return Eval(s.Expression!);
                else if (s.Kind == "if")
                {
                    var value = Statements(Eval(s.Expression!) != 0 ? s.Body ?? [] : s.ElseBody ?? []);
                    if (value.HasValue) return value;
                }
                else throw new InvalidOperationException("Unexpected statement: " + s.Kind);
            }
            return null;
        }
        return Statements(function.Statements) ?? throw new InvalidOperationException("No result");
    }

    private static double Binary(string op, double a, double b) => op switch
    {
        "+" => a + b,
        ">" => a > b ? 1 : 0,
        "==" => a == b ? 1 : 0,
        "||" => a != 0 || b != 0 ? 1 : 0,
        _ => throw new InvalidOperationException(op),
    };
}
