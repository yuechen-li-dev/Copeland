using Copeland.TS.Compiler;
using Copeland.TS.Backend.CSharp.Tests.Runtime;
using Copeland.TS.Backend.CSharp;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class LengthValueTests
{
    [Fact]
    public void Unit_values_in_immutable_record_configurations_execute_through_csharp()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            record Config { gap: Length; }
            function main(): number {
                const comfortable: Config = { gap: 12px };
                const dense = comfortable with { gap: 8px };
                return comfortable.gap.value * 100 + dense.gap.value;
            }
            """);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        CSharpCompilation emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(1208d, GeneratedModuleInvoker.Invoke(generated.Assembly!, "main"));
    }
}
