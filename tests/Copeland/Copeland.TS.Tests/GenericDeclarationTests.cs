using Copeland.TS.Syntax;
using Copeland.TS.Semantics;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Tests;


public sealed class GenericDeclarationTests
{
    [Fact]
    public void Explicit_template_declarations_export_once_through_the_host_project_graph()
    {
        var compilation = CopelandProjectCompiler.CompileToMir(
            [
                new CopelandProjectSource("Library.ts", "C:/workspace/Library.ts", "export template<type T> function Identity(value: T): T { return value; }"),
                new CopelandProjectSource(
                    "App.ts",
                    "C:/workspace/App.ts",
                    "import { Identity } from './Library'; function main(): int { return Identity<int>(7); }"
                ),
            ]);
        Assert.True(compilation.Success, string.Join("\n", compilation.Diagnostics));
    }

    public const string Witness = """
        interface HasValue<T> { value: T; }
        template<type T>
        record Box { value: T; }
        type Boxes<T> = Box<T>[];
        template<type T = int, static Count: int = 2>
        function Make(value: T): Box<T> { return { value: value }; }
        function Read<T extends HasValue<int>>(value: T): int { return value.value; }
        function Forward<T extends HasValue<int>>(value: T): int { return Read<T>(value); }
        function Add<static Amount: int = 2>(value: int): int { return value + Amount; }
        function main(): int {
            const box: Box<int> = { value: 7 };
            const values: Boxes<int> = [box];
            return Forward(box) + Read(Make<int>(values[0].value)) + Add<Amount: 3>(7);
        }
        """;
    [Fact]
    public void Host_backport_closes_template_functions_records_aliases_interfaces_and_static_values()
    {
        var tree = SyntaxTree.Parse(Witness);
        Assert.Empty(tree.Diagnostics);
        var bound = Binder.Bind(tree);
        Assert.Empty(bound.Diagnostics);
        var mir = CopelandCompiler.CompileToMir(Witness);
        Assert.True(mir.Success, string.Join("\n", mir.Diagnostics));
    }

    [Fact]
    public void Record_defaults_can_use_aliases_from_the_compilation_unit()
    {
        const string source = """
            type Scalar = int;
            record Box<T = Scalar> { value: T; }
            type DefaultBox = Box;
            function main(): int { const box: DefaultBox = { value: 7 }; return box.value; }
            """;
        var compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join("\n", compilation.Diagnostics));
        Assert.DoesNotContain("error", compilation.MirText!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Defaults_and_static_arithmetic_forward_as_typed_arguments()
    {
        const string source = """
            function Sum<static N: int = 2, static M: int = (N + 1)>(v: int): int { return v + N + M; }
            function Forward<static N: int>(v: int): int { return Sum<N, (N + 1)>(v); }
            function Choose<static Enabled: boolean>(v: int): int { if (Enabled) { return v; } return 0; }
            function main(): int { return Sum(4) + Forward<2>(4) + Choose<true>(4); }
            """;
        var compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join("\n", compilation.Diagnostics));
    }

    [Theory]
    [InlineData("interface Value { value: int; } record Box<T extends Value> { item: T; } function main(): int { const box: Box<int> = { item: 1 }; return 1; }", "COPE-REQUIREMENT-0005")]
    [InlineData("interface Value { value: int; } record Box<T extends Value> { item: T; } type Invalid = Box<int>;", "COPE-REQUIREMENT-0005")]
    [InlineData("function Add<static N: int>(v: int): int { return v + N; } function main(v: int): int { return Add<N: v>(v); }", "COPE-GENERIC-0016")]
    [InlineData("function Bad<static N: int>(v: N): N { return v; }", "COPE-GENERIC-0016")]
    [InlineData("type Cycle<T> = Cycle<T>;", "COPE-ALIAS-0005")]
    public void Host_rejects_invalid_requirements_runtime_static_inputs_and_recursive_aliases(string source, string diagnostic)
    {
        var compilation = CopelandCompiler.CompileToMir(source);
        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, item => item.Id == diagnostic);
    }
}
