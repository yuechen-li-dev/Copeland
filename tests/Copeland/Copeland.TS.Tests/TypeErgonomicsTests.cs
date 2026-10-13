using System.Text;
using Copeland.TS.Compiler;
using Copeland.TS.Syntax;
using Copeland.TS.Templates;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TypeErgonomicsTests
{
    [Theory]
    [InlineData("type Key = string | int;", "typed payload cases")]
    [InlineData("type Phase = \"loading\" | \"ready\";", "enum Phase")]
    [InlineData("type Keys = keyof Shape;", "reflect fieldsOf<T>()")]
    [InlineData("function main(): int { type Count = int; return 1; }", "module scope")]
    public void Unsupported_type_instincts_have_one_primary_diagnostic_and_a_repair(string source, string repair)
    {
        SyntaxTree tree = SyntaxTree.Parse(source);

        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Contains(repair, diagnostic.Message, StringComparison.Ordinal);
        Assert.True(diagnostic.Length > 0);
        Assert.DoesNotContain(tree.Diagnostics, item => item.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
    }

    [Fact]
    public void Structural_alias_constraints_support_width_generic_specialization_and_forwarding()
    {
        const string source = """
            type HasValue<Value> = { value: Value; };
            type IntegerValue = HasValue<int>;
            interface Named { name: string; }
            record Box<T extends IntegerValue> { item: T; }
            type Boxes<T extends IntegerValue> = Box<T>[];
            record Item { value: int; name: string; }

            function Read<T extends IntegerValue>(item: T): int { return item.value; }
            function Forward<T extends HasValue<int> & Named>(item: T): int { return Read<T>(item); }
            function main(): int {
                const item: Item = { value: 20, name: "named" };
                const boxes: Boxes<Item> = [{ item }];
                return Forward(boxes[0].item) + Read({ value: 22, extra: true });
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.DoesNotContain("IntegerValue", compilation.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Static_templates_use_the_same_closed_structural_alias_requirement()
    {
        const string source = """
            type HasValue<Value> = { value: Value; };
            record Item { value: int; extra: boolean; }
            template<type T extends HasValue<int>> Inner: ProjectTree {
                emit(textFile("accepted.txt", reflect nameOf<T>()));
            }
            template<type T extends HasValue<int> = Item> Entry: ProjectTree {
                return instantiate Inner<T>;
            }
            """;

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Entry");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal("Item", Encoding.UTF8.GetString(Assert.Single(result.Project!.Files).Bytes));
    }

    [Fact]
    public void Imported_generic_projection_shapes_preserve_exact_requirements()
    {
        CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir(
        [
            new("Shapes.ts", "C:/workspace/Shapes.ts", """
                type Full<Value> = { value: Value; internal: boolean; };
                export type Public<Value> = Pick<Full<Value>, "value">;
                """),
            new("Main.ts", "C:/workspace/Main.ts", """
                import type { Public } from "./Shapes";
                function Read<Value, Item extends Public<Value>>(fallback: Value, item: Item): Value {
                    return item.value;
                }
                function main(): int { return Read(0, { value: 42, extra: true }); }
                """),
        ]);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Theory]
    [InlineData("type Shape = { value: int; }; record Item { extra: boolean; } function Read<T extends Shape>(item: T): int { return item.value; } function main(): int { return Read<Item>({ extra: true }); }", "COPE-REQUIREMENT-0006")]
    [InlineData("type Shape = { value: int; }; record Item { value: string; } function Read<T extends Shape>(item: T): int { return item.value; } function main(): int { return Read<Item>({ value: \"wrong\" }); }", "COPE-REQUIREMENT-0007")]
    [InlineData("type Shape = { value: int; }; type Other = { value: string; }; function Read<T extends Shape & Other>(item: T): T { return item; }", "COPE-REQUIREMENT-0003")]
    [InlineData("type Shape = { value: int; }; function Read<T extends Shape & Shape>(item: T): T { return item; }", "COPE-REQUIREMENT-0002")]
    [InlineData("type Scalar = int; function Read<T extends Scalar>(item: T): T { return item; }", "COPE-REQUIREMENT-0008")]
    [InlineData("type Shape = { value: int; }; record Box<T extends Shape> { item: T; } type Invalid = Box<int>;", "COPE-REQUIREMENT-0005")]
    [InlineData("type Shape = { value: int; }; function Read(item: Shape): int { return item.value; }", "COPE-REC-0010")]
    public void Shape_requirements_preserve_exact_field_types_and_runtime_boundaries(string source, string diagnosticId)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Fact]
    public void Callable_fields_use_normal_invocation_for_records_inferred_shapes_and_requirements()
    {
        const string source = """
            type Operation<T> = (value: T) => T;
            type HasOperation = { operation: Operation<int>; };
            record Box { operation: Operation<int>; }
            function Make(base: int): Box {
                return { operation: capture { base } (value: int) => base + value };
            }
            function Apply<T extends HasOperation>(box: T, value: int): int {
                return box.operation(value);
            }
            function main(): int {
                const inferred = { operation: (value: int) => value + 1 };
                return Make(20).operation(21) + Apply(inferred, 0);
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains("invoke", compilation.MirText, StringComparison.Ordinal);
        Assert.Equal(1, compilation.MirText!.Split("call Make(20)", StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [InlineData("record Box { value: int; } function main(box: Box): int { return box.value(); }", "COPE-CALL-0004")]
    [InlineData("type Operation = (value: int) => int; record Box { operation: Operation; } function main(box: Box): int { return box.operation(); }", "COPE-CALL-0005")]
    [InlineData("type Operation = (value: int) => int; record Box { operation: Operation; } function main(box: Box): int { return box.operation(\"wrong\"); }", "COPE-CALL-0006")]
    [InlineData("type Operation = (value: int) => int; record Box { operation: Operation; } function main(box: Box): Operation { return (value: int) => box.operation(value); }", "COPE-CALL-0017")]
    [InlineData("class Box { private operation: (value: int) => int; constructor(operation: (value: int) => int): Box { return { operation }; } } function main(box: Box): int { return box.operation(1); }", "COPE-CLASS-0009")]
    public void Field_invocation_keeps_callable_capture_and_privacy_checks(string source, string diagnosticId)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.False(compilation.Success);
        var diagnostic = Assert.Single(compilation.Diagnostics);
        Assert.Equal(diagnosticId, diagnostic.Id);
    }
}
