using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class GeneratedDefinitionReachabilityTests
{
    [Fact]
    public void Unreachable_generated_record_family_is_omitted_without_renaming_survivors()
    {
        const string source = """
            record Used { value: int; }
            record Unused { label: string; }
            function main(): int {
                const value: Used = { value: 42 };
                return value.value;
            }
            """;
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        JavaScriptCompilation baseline = JavaScriptBackend.Emit(
            compilation.MirCompilation!.Program!,
            new JavaScriptEmissionOptions { EnableGeneratedDefinitionReachability = false });
        JavaScriptCompilation optimized = JavaScriptBackend.Emit(compilation.MirCompilation.Program!);

        Assert.True(baseline.Success, string.Join(Environment.NewLine, baseline.Diagnostics));
        Assert.True(optimized.Success, string.Join(Environment.NewLine, optimized.Diagnostics));
        Assert.NotNull(baseline.Reachability);
        Assert.NotNull(optimized.Reachability);
        Assert.Contains(optimized.Reachability.Definitions, definition =>
            definition.Kind == "record-carrier" && !definition.IsReachable);
        Assert.True(optimized.Reachability.RemovedCount >= 2);
        Assert.True(optimized.Reachability.RemovedBytes > 0);
        Assert.True(optimized.SourceText!.Length < baseline.SourceText!.Length);

        string usedConstructor = Assert.Single(
            baseline.SourceText.Split('\n'),
            line => line.StartsWith("function __cope_m3_record_make_r1_", StringComparison.Ordinal));
        Assert.Contains(usedConstructor, optimized.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("record_make_r2", optimized.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void Boundary_parameter_roots_validator_and_its_carrier_transitively()
    {
        const string source = """
            record PublicValue { value: int; }
            function boundary(value: PublicValue): int { return value.value; }
            """;
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        JavaScriptCompilation emitted = JavaScriptBackend.Emit(
            compilation.MirCompilation!.Program!,
            new JavaScriptEmissionOptions
            {
                Profile = JavaScriptEmissionProfile.Production,
                BoundaryFunctionNames = new HashSet<string>(["boundary"], StringComparer.Ordinal),
            });

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.All(emitted.Reachability!.Definitions, definition => Assert.True(definition.IsReachable));
        JavaScriptReachabilityDefinition validator = Assert.Single(
            emitted.Reachability.Definitions,
            definition => definition.Kind == "record-validator");
        Assert.True(validator.IsRoot);
        Assert.Contains(emitted.Reachability.Definitions, definition =>
            definition.Kind == "record-carrier" && definition.IsReachable);
    }

    [Fact]
    public void Deterministic_marker_handles_dead_and_live_cycles_and_shared_dependencies()
    {
        var graph = new JavaScriptGeneratedDefinitionGraph();
        foreach (string id in new[] { "a", "b", "c", "shared", "root" })
        {
            graph.Register(id, "test");
        }

        graph.Begin("a");
        graph.Reference("b");
        graph.End("a");
        graph.Begin("b");
        graph.Reference("a");
        graph.Reference("shared");
        graph.End("b");
        graph.Begin("c");
        graph.Reference("c");
        graph.End("c");
        graph.Begin("root");
        graph.Reference("a");
        graph.Reference("shared");
        graph.End("root");
        graph.Reference("root");

        IReadOnlySet<string> reachable = graph.MarkReachable();

        Assert.Equal(["a", "b", "root", "shared"], reachable.OrderBy(value => value, StringComparer.Ordinal));
        Assert.DoesNotContain("c", reachable);
    }

    [Fact]
    public void Invalid_mir_is_rejected_before_reachability_runs()
    {
        var invalid = new MirProgram(
            [],
            [new MirFunction("main", [], new MirNamedType("int"), [], [new MirReturnStatement(new MirLiteralExpression("wrong", new MirNamedType("string")))])]);

        JavaScriptCompilation emitted = JavaScriptBackend.Emit(invalid);

        Assert.False(emitted.Success);
        Assert.Null(emitted.Reachability);
        Assert.Contains(emitted.Diagnostics, diagnostic => diagnostic.Id == "COPE-JS-0002");
    }
}
