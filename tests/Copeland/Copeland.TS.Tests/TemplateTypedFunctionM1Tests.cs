using Copeland.TS.Semantics;
using Copeland.TS.Templates;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TemplateTypedFunctionM1Tests
{
    [Fact]
    public void Template_returns_array_from_ordinary_static_safe_function_calls()
    {
        const string source = """
            record OperationArgs {
                count: int;
                width: number;
            }

            enum Operation {
                Repeat(args: OperationArgs),
            }

            function Repeat(args: OperationArgs): Operation {
                return Operation.Repeat(args);
            }

            template<static count: int = 12, static width: number = 8.0> Operations: Operation[] {
                return [Repeat({ count, width })];
            }
            """;

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Operations");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        TemplateTypedValue value = Assert.IsType<TemplateTypedValue>(result.Value);
        object?[] operations = Assert.IsType<object?[]>(value.Value);
        StaticEnumValue operation = Assert.IsType<StaticEnumValue>(Assert.Single(operations));
        Assert.Equal("Repeat", operation.Case.Name);
        Assert.NotEmpty(value.DeterministicHash);
    }

    [Fact]
    public void Ordinary_function_and_template_share_static_iteration_and_payload_enum_values()
    {
        const string source = """
            enum Marker {
                At(index: int),
            }

            function At(index: int): Marker {
                return Marker.At(index);
            }

            template<> Markers: Marker[] {
                const source: int[] = [0, 1, 2];
                static for (const index of source) {
                    emit(textFile("ignored", "ignored"));
                }
                return [At(0), At(1), At(2)];
            }
            """;

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Markers");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        object?[] values = Assert.IsType<object?[]>(result.Value!.Value);
        Assert.Equal([0, 1, 2], values.Cast<StaticEnumValue>()
            .Select(value => Assert.IsType<StaticPrimitiveValue>(Assert.Single(value.Payloads)).Value));
    }

    [Fact]
    public void Mixed_template_array_elements_are_rejected_by_normal_types()
    {
        const string source = """
            template<> Invalid: int[] {
                return [1, "wrong"];
            }
            """;

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Invalid");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-TEMPLATE-0017");
    }

    [Fact]
    public void Template_returns_contextually_typed_record_array()
    {
        const string source = """
            record Pair {
                left: int;
                right: string;
            }

            template<> Pairs: Pair[] {
                return [{ left: 1, right: "one" }, { left: 2, right: "two" }];
            }
            """;

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Pairs");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        object?[] values = Assert.IsType<object?[]>(result.Value!.Value);
        Assert.All(values, value => Assert.IsAssignableFrom<StaticValue>(value));
        Assert.All(values, value => Assert.Equal("Pair", ((StaticValue)value!).Type.Name));
    }

    [Fact]
    public void Unsafe_ordinary_function_is_rejected_during_template_evaluation()
    {
        const string source = """
            using System;

            function RuntimeOnly(): number {
                return Math.Round(1.2);
            }

            template<> Invalid: number[] {
                return [RuntimeOnly()];
            }
            """;

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Invalid");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-STATIC-0012");
    }
}
