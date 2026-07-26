using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.CSharp.Tests.Runtime;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class PipelineRuntimeTests
{
    [Fact]
    public void Pipeline_uses_ordinary_calls_in_generated_csharp()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            function increment(value: number): number { return value + 1; }
            function double(value: number): number { return value * 2; }
            function main(): number { return 20 |> increment |> double; }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        CSharpCompilation emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Assert.DoesNotContain("Pipeline", emitted.SourceText, StringComparison.Ordinal);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(42d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
    }
}
