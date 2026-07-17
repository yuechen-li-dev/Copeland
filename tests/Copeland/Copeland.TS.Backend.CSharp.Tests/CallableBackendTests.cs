using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Backend.CSharp.Tests.Runtime;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class CallableBackendTests
{
    [Fact]
    public void Lifted_noncapturing_arrows_run_without_backend_specific_closures()
    {
        const string source = """
            type Operation = (value: number) => number;
            function main(): number {
                const double = (value: number) => value * 2;
                const increment: Operation = value => value + 1;
                return increment(double(20));
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(csharp.Diagnostics);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(41d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
    }

    [Fact]
    public void Explicit_capture_uses_a_generated_immutable_environment_carrier()
    {
        const string source = """
            function makeAdder(base: number): (value: number) => number {
                return capture { base } (value: number) => base + value;
            }
            function main(): number { return makeAdder(20)(22); }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(csharp.Diagnostics);
        Assert.Contains("CapturedCallableEnvironment", csharp.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("=> base", csharp.SourceText, StringComparison.Ordinal);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(42d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
    }

    [Fact]
    public void Explicit_capture_snapshots_a_rebound_let_at_construction_time()
    {
        const string source = """
            type Operation = (value: number) => number;
            function make(): Operation {
                let base: number = 10;
                const add = capture { base } (value: number) => base + value;
                base = 20;
                return add;
            }
            function main(): number { return make()(5); }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(15d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
    }

    [Fact]
    public void Callable_values_survive_array_record_and_enum_storage()
    {
        const string source = """
            type Operation = (value: number) => number;
            record Box { operation: Operation; }
            enum Choice { Value(operation: Operation), }
            function makeAdder(base: number): Operation { return capture { base } (value: number) => base + value; }
            function main(): number {
                const values: Operation[] = [makeAdder(1)];
                const box: Box = { operation: makeAdder(2) };
                const choice: Choice = Choice.Value(box.operation);
                return match choice { Value(operation) => operation(40), };
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(42d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
    }

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

    [Fact]
    public void Callable_delegate_family_is_demand_emitted_and_reuses_exact_signatures()
    {
        const string callableSource = """
            type Operation = (value: number) => number;
            function increment(value: number): number { return value + 1; }
            function decrement(value: number): number { return value - 1; }
            function main(): number {
                const first: Operation = increment;
                const second: Operation = decrement;
                return first(second(5));
            }
            """;

        CopelandCompilation callableCompilation = CopelandCompiler.CompileToMir(callableSource);
        Assert.True(callableCompilation.Success, string.Join(Environment.NewLine, callableCompilation.Diagnostics));
        CSharpCompilation callableEmission = CSharpBackend.Emit(callableCompilation.MirCompilation!.Program!);
        Assert.Empty(callableEmission.Diagnostics);
        Assert.Equal(1, callableEmission.SourceText.Split("delegate ", StringSplitOptions.None).Length - 1);
        Assert.Contains(")increment;", callableEmission.SourceText, StringComparison.Ordinal);
        Assert.Contains(")decrement;", callableEmission.SourceText, StringComparison.Ordinal);

        CopelandCompilation ordinaryCompilation = CopelandCompiler.CompileToMir("function main(): number { return 1; }");
        Assert.True(ordinaryCompilation.Success, string.Join(Environment.NewLine, ordinaryCompilation.Diagnostics));
        CSharpCompilation ordinaryEmission = CSharpBackend.Emit(ordinaryCompilation.MirCompilation!.Program!);
        JavaScriptCompilation ordinaryJavaScript = JavaScriptBackend.Emit(ordinaryCompilation.MirCompilation.Program!);
        Assert.DoesNotContain("delegate ", ordinaryEmission.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("__cope_callable", ordinaryJavaScript.SourceText, StringComparison.Ordinal);
    }
}
