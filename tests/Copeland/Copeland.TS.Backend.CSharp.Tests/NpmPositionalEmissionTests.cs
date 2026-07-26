using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.CSharp.Tests.Runtime;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class NpmPositionalEmissionTests
{
    [Fact]
    public void Emits_compilable_private_argument_tuple_transport_for_positional_npm_calls()
    {
        CopelandCompilation source = CopelandCompiler.CompileToMir(Source, new CopelandCompilationOptions
        {
            SourcePath = "main.ts",
            NpmDependencies = new CopelandNpmDependencyGraph(
            [
                new CopelandNpmPackageContract("@fixture/math", "1.0.0", [new CopelandNpmFunctionContract("sum", ["number", "number"], "number", "RemoteError", IsPromise: true)]),
            ]),
        });
        Assert.True(source.Success, string.Join(Environment.NewLine, source.Diagnostics));

        CSharpCompilation emitted = CSharpBackend.Emit(source.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Assert.Contains("npm:@fixture/math@1.0.0:sum", emitted.SourceText, StringComparison.Ordinal);

        RoslynCompileResult compiled = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
        Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Diagnostics));
    }

    private const string Source = """
        import { sum } from "@fixture/math";
        const $schema: string = "copeland://npm/test";
        record RemoteError { message: string; }
        async function add(left: number, right: number): number ! RemoteError {
            const pending: Async<number ! RemoteError> = sum(left, right);
            return await pending;
        }
        """;
}
