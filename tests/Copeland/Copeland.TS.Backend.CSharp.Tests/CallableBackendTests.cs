using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Backend.CSharp.Tests.Runtime;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class CallableBackendTests
{
    [Fact]
    public void Callable_references_emit_delegates_in_csharp_and_provenance_carriers_in_javascript()
    {
        const string source = """
            type Operation = (value: number) => number;
            function increment(value: number): number { return value + 1; }
            function apply(operation: Operation, value: number): number { return operation(value); }
            function main(): number { const operation = increment; return apply(operation, 4); }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation javascript = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation symbolicJavaScript = JavaScriptBackend.Emit(
            compilation.MirCompilation.Program!,
            new JavaScriptEmissionOptions { Profile = JavaScriptEmissionProfile.Symbolic });

        Assert.Empty(csharp.Diagnostics);
        Assert.Contains("delegate", csharp.SourceText, StringComparison.Ordinal);
        Assert.Contains("operation(value)", csharp.SourceText, StringComparison.Ordinal);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(5d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
        Assert.True(javascript.Success, string.Join(Environment.NewLine, javascript.Diagnostics));
        Assert.Contains("WeakSet", javascript.SourceText, StringComparison.Ordinal);
        Assert.Contains("Object.create(null)", javascript.SourceText, StringComparison.Ordinal);
        Assert.Contains("__cope_callable_invoke", javascript.SourceText, StringComparison.Ordinal);
        Assert.True(symbolicJavaScript.Success, string.Join(Environment.NewLine, symbolicJavaScript.Diagnostics));
    }
}
