using Copeland.TS.Backend.CSharp.Tests.Runtime;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class NamedCaptureTests
{
    [Fact]
    public void Named_initializers_evaluate_once_in_outer_scope_order_and_escape_as_snapshots()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            function Make(): () => number {
                let current: number = 1;
                const value = capture {
                    first: current = current + 1,
                    second: current = current + 1,
                    snapshot: current
                } () => first * 100 + second * 10 + snapshot;
                current = 99;
                return value;
            }
            function main(): number { return Make()(); }
            """);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        CSharpCompilation emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(233d, GeneratedModuleInvoker.Invoke(generated.Assembly!, "main"));
    }

    [Theory]
    [InlineData("capture { value: 1, value: 2 } () => value", "COPE-CALL-0012")]
    [InlineData("capture { value: 1 } (value: number) => value", "COPE-BIND-0005")]
    [InlineData("capture { other: 1 } () => source", "COPE-CALL-0017")]
    [InlineData("capture { value: 1 } () => { value = 2; }", "COPE-CALL-0018")]
    public void Named_capture_preserves_duplicate_collision_implicit_capture_and_immutability_checks(string expression, string expected)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir($$"""
            function main(): void {
                const source = 1;
                const callback: () => void = {{expression}};
            }
            """);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == expected);
    }
}
