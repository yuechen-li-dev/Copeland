using System.Reflection;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests.Runtime;

public sealed class RecordRuntimeTests
{
    [Fact]
    public void Constructs_accesses_and_rebinds_an_immutable_record_to_42()
    {
        Assembly assembly = Compile("""
            record Point {
                x: number;
                y: number;
            }

            function main(): number {
                let point: Point = { x: 1, y: 2 };
                point = point with { x: 40 };
                return point.x + point.y;
            }
            """);

        Assert.Equal(42d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(assembly, "main")));
    }

    [Fact]
    public void Construction_preserves_authored_order_and_exactly_once_evaluation()
    {
        Assembly assembly = Compile("""
            record Sample {
                x: number;
                y: number;
                observed: number;
            }

            function main(): number {
                let trace: number = 0;
                const sample: Sample = {
                    y: trace = trace * 10 + 2,
                    x: trace = trace * 10 + 1,
                    observed: trace,
                };
                return sample.x * 10000 + sample.y * 100 + sample.observed;
            }
            """);

        Assert.Equal(210221d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(assembly, "main")));
    }

    [Fact]
    public void With_evaluates_source_then_replacements_in_authored_order_once()
    {
        Assembly assembly = Compile("""
            record Sample {
                x: number;
                y: number;
                observed: number;
            }

            function main(): number {
                let trace: number = 0;
                let source: Sample = { x: 1, y: 2, observed: 0 };
                const updated: Sample = (source = source with { x: source.x + 10 }) with {
                    y: trace = trace * 10 + 2,
                    x: trace = trace * 10 + 1,
                    observed: trace,
                };
                return source.x * 1000000 + updated.x * 10000 + updated.y * 100 + updated.observed;
            }
            """);

        Assert.Equal(11210221d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(assembly, "main")));
    }

    [Fact]
    public void Field_access_evaluates_a_complex_receiver_once()
    {
        Assembly assembly = Compile("""
            record Point {
                x: number;
            }

            function main(): number {
                let point: Point = { x: 1 };
                const accessed: number = (point = point with { x: point.x + 1 }).x;
                return accessed * 10 + point.x;
            }
            """);

        Assert.Equal(22d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(assembly, "main")));
    }

    [Fact]
    public void Argument_context_construction_preserves_outer_left_to_right_order()
    {
        Assembly assembly = Compile("""
            record Point {
                x: number;
            }

            function combine(first: number, point: Point): number {
                return first * 1000 + point.x * 10;
            }

            function main(): number {
                let trace: number = 0;
                const result: number = combine(
                    trace = trace * 10 + 1,
                    { x: trace = trace * 10 + 2 }
                );
                return result + trace;
            }
            """);

        Assert.Equal(1132d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(assembly, "main")));
    }

    [Fact]
    public void Records_compose_with_results_propagation_unwrap_and_try_except()
    {
        Assembly assembly = Compile("""
            record Point {
                x: number;
                y: number;
            }

            record Box {
                outcome: Point ! string;
            }

            function good(): Point ! string {
                return ok({ x: 40, y: 2 });
            }

            function bad(): Point ! string {
                return err("bad");
            }

            function fallback(): Point {
                return { x: 40, y: 2 };
            }

            function forwarded(): Point ! string {
                const point: Point = good()?;
                return ok(point);
            }

            function recovered(): Point {
                return try { bad()? } except (error) { fallback() };
            }

            function main(): number {
                const box: Box = { outcome: forwarded() };
                const matched: Point = match box.outcome {
                    ok(point) => point,
                    err(error) => recovered(),
                };
                return matched.x + forwarded()!.y;
            }
            """);

        Assert.Equal(42d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(assembly, "main")));
    }

    [Fact]
    public void A_record_can_be_a_result_error_and_a_match_can_return_a_record()
    {
        Assembly assembly = Compile("""
            record Point {
                x: number;
                y: number;
            }

            function failed(): number ! Point {
                return err({ x: 40, y: 2 });
            }

            function main(): number {
                const point: Point = match failed() {
                    ok(value) => { x: value, y: 0 },
                    err(error) => error,
                };
                return point.x + point.y;
            }
            """);

        Assert.Equal(42d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(assembly, "main")));
    }

    [Fact]
    public void Records_compose_with_payload_enums_and_nested_contextual_construction()
    {
        Assembly assembly = Compile("""
            record Point {
                x: number;
                y: number;
            }

            record Envelope {
                point: Point;
                kind: Kind;
            }

            enum Kind {
                Moved,
            }

            enum Event {
                Changed(point: Point),
            }

            function load(): Envelope ! string {
                return ok({ point: { x: 1, y: 2 }, kind: Kind.Moved });
            }

            function total(point: Point): number {
                return point.x + point.y;
            }

            function main(): number {
                const envelope: Envelope = load()!;
                let current: Point = envelope.point;
                current = current with { x: 40 };
                const event: Event = Event.Changed(current);
                const point: Point = match event {
                    Changed(value) => value with { y: envelope.point.y },
                };
                return total(point);
            }
            """);

        Assert.Equal(42d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(assembly, "main")));
    }

    [Fact]
    public void Same_shaped_records_have_distinct_generated_nominal_types()
    {
        CSharpCompilation compilation = Emit("""
            record ScreenPoint { x: number; }
            record WorldPoint { x: number; }
            function screen(): ScreenPoint { return { x: 1 }; }
            function world(): WorldPoint { return { x: 1 }; }
            """);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(compilation.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));

        object screen = GeneratedModuleInvoker.Invoke(generated.Assembly!, "screen")!;
        object world = GeneratedModuleInvoker.Invoke(generated.Assembly!, "world")!;

        Assert.NotEqual(screen.GetType(), world.GetType());
        Assert.True(screen.GetType().IsSealed);
        Assert.True(world.GetType().IsSealed);
        Assert.All(screen.GetType().GetProperties(BindingFlags.Instance | BindingFlags.NonPublic), property => Assert.False(property.CanWrite));
    }

    [Fact]
    public void Repeated_generation_and_execution_are_deterministic()
    {
        const string source = "record Point { x: number; y: number; } function main(): number { const point: Point = { y: 2, x: 40 }; return point.x + point.y; }";
        CSharpCompilation first = Emit(source);
        CSharpCompilation second = Emit(source);

        Assert.Equal(first.SourceText, second.SourceText);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(first.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        for (int iteration = 0; iteration < 5; iteration++)
        {
            Assert.Equal(42d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
        }
    }

    private static Assembly Compile(string source)
    {
        CSharpCompilation compilation = Emit(source);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(compilation.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        return generated.Assembly!;
    }

    private static CSharpCompilation Emit(string source)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        CSharpCompilation generated = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(generated.Diagnostics);
        return generated;
    }
}
