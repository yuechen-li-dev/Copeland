using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class NpmImportEmissionTests
{
    [Fact]
    public void Emits_a_native_named_esm_import_for_npm_calls()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(Source, new CopelandCompilationOptions
        {
            SourcePath = "main.ts",
            NpmPackages = [new CopelandNpmPackageContract("@fixture/transform", "1.0.0", [new CopelandNpmFunctionContract("delayedTransform", ["Request"], "Response", "RemoteError", true)])],
        });
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        Assert.Contains("import { delayedTransform } from \"@fixture/transform\";", emitted.SourceText, StringComparison.Ordinal);
        Assert.Contains("Promise.resolve(delayedTransform(", emitted.SourceText, StringComparison.Ordinal);
    }

    private const string Source = """
        import { delayedTransform } from "@fixture/transform";
        const $schema: string = "copeland://npm/test";
        record Request { value: number; }
        record Response { value: number; }
        record RemoteError { message: string; }
        function request(value: number): Request { return { value }; }
        async function load(value: number): Response ! RemoteError { const pending: Async<Response ! RemoteError> = delayedTransform(request(value)); return await pending; }
        """;
}
