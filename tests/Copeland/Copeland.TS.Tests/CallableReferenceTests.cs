using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class CallableReferenceTests
{
    [Fact]
    public void Noncapturing_arrows_are_lifted_and_contextually_typed_once()
    {
        const string source = """
            type Operation = (value: number) => number;
            function main(): number {
                const double = (value: number) => value * 2;
                const choose: Operation = value => value + 1;
                const block: Operation = (value: number): number => {
                    const adjusted = value + 1;
                    return adjusted * 2;
                };
                return double(4) + choose(4) + block(4);
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains("function-ref __cope_arrow_0", compilation.MirText, StringComparison.Ordinal);
        Assert.Contains("func __cope_arrow_2", compilation.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Implicit_arrow_capture_is_rejected_with_the_binding_name()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            function makeAdder(base: number): (value: number) => number {
                return (value: number) => base + value;
            }
            """);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-CALL-0017"
            && diagnostic.Length > 0
            && diagnostic.Message.Contains("base", StringComparison.Ordinal));
    }

    [Fact]
    public void Explicit_capture_lowers_to_a_callable_construction_with_environment_values()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            function makeAdder(base: number): (value: number) => number {
                return capture { base } (value: number) => base + value;
            }
            function main(): number { return makeAdder(20)(22); }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains("callable-new __cope_arrow_0 env(base)", compilation.MirText, StringComparison.Ordinal);
        Assert.Contains("func __cope_arrow_0(base: number, value: number)", compilation.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Named_and_closed_generic_function_values_lower_to_distinct_reference_and_invoke_nodes()
    {
        const string source = """
            type Operation = (value: number) => number;

            function increment(value: number): number { return value + 1; }
            function identity<T>(value: T): T { return value; }
            function apply(operation: Operation, value: number): number { return operation(value); }
            function provide(): Operation { return increment; }
            function main(): number {
                const first = increment;
                const second = identity<number>;
                const supplied = provide();
                return apply(first, 20) + second(supplied(20));
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains("function-ref increment", compilation.MirText, StringComparison.Ordinal);
        Assert.Contains("function-ref identity__", compilation.MirText, StringComparison.Ordinal);
        Assert.Contains("invoke operation(value)", compilation.MirText, StringComparison.Ordinal);
        Assert.Contains("call apply(first, 20)", compilation.MirText, StringComparison.Ordinal);
        Assert.DoesNotContain("Operation", compilation.MirText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("function id<T>(value: T): T { return value; } function main(): number { const value = id; return 0; }", "COPE-CALL-0003")]
    [InlineData("function f(value: number): number { return value; } function main(): number { const value: number = 1; return value(1); }", "COPE-CALL-0004")]
    [InlineData("function f(value: number): number { return value; } function main(): boolean { const value = f; return value == f; }", "COPE-CALL-0008")]
    public void Unsupported_callable_uses_have_focused_diagnostics(string source, string diagnosticId)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId && diagnostic.Length > 0);
    }

    [Fact]
    public void Callable_values_flow_through_arrays_records_and_enum_payloads()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            type Operation = (value: number) => number;
            record Box { operation: Operation; }
            enum Choice { Value(operation: Operation), }
            function increment(value: number): number { return value + 1; }
            function main(): number {
                const values: Operation[] = [increment];
                const box: Box = { operation: increment };
                const choice: Choice = Choice.Value(box.operation);
                return match choice { Value(operation) => operation(41), };
            }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Fact]
    public void Nested_explicit_capture_and_exact_interface_field_requirements_bind_once()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            type Operation = (value: number) => number;
            interface HasOperation { operation: Operation; }
            record Box { operation: Operation; }

            function select<T extends HasOperation>(value: T): Operation {
                return value.operation;
            }

            function make(base: number): (first: number) => (second: number) => number {
                return capture { base } (first: number): (second: number) => number => {
                    const total = base + first;
                    return capture { total } (second: number) => total + second;
                };
            }

            function main(): number {
                const box: Box = { operation: make(10)(20) };
                return select<Box>(box)(12);
            }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        string mirText = Assert.IsType<string>(compilation.MirText);
        Assert.Equal(2, mirText.Split("callable-new", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Callable_resource_boundaries_are_exact()
    {
        string acceptedParameters = string.Join(", ", Enumerable.Range(0, 32).Select(index => $"p{index}: number"));
        string rejectedParameters = string.Join(", ", Enumerable.Range(0, 33).Select(index => $"p{index}: number"));

        Assert.True(CopelandCompiler.CompileToMir($"type Operation = ({acceptedParameters}) => number; function main(): number {{ return 0; }}").Success);
        Assert.Contains(CopelandCompiler.CompileToMir($"type Operation = ({rejectedParameters}) => number; function main(): number {{ return 0; }}").Diagnostics,
            diagnostic => diagnostic.Id == "COPE-CALL-0001" && diagnostic.Length > 0);

        Assert.True(CopelandCompiler.CompileToMir(BuildNestedCallableSource(16)).Success);
        Assert.Contains(CopelandCompiler.CompileToMir(BuildNestedCallableSource(17)).Diagnostics,
            diagnostic => diagnostic.Id == "COPE-CALL-0002" && diagnostic.Length > 0);
    }

    private static string BuildNestedCallableSource(int depth)
    {
        string type = "number";
        for (int index = 0; index < depth; index++)
        {
            type = "(value: " + type + ") => number";
        }

        return "type Operation = " + type + "; function main(): number { return 0; }";
    }
}
