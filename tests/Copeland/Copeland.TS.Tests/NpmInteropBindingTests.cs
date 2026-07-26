using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class NpmInteropBindingTests
{
    [Fact]
    public void Named_npm_import_lowers_to_a_compiler_owned_transport_operation()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(Source, new CopelandCompilationOptions
        {
            SourcePath = "main.ts",
            NpmPackages =
            [
                new CopelandNpmPackageContract(
                    "@fixture/transform",
                    "1.0.0",
                    [new CopelandNpmFunctionContract("delayedTransform", ["Request"], "Response", "RemoteError", IsPromise: true)]),
            ],
        });

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains("npm:@fixture/transform@1.0.0:delayedTransform", compilation.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_and_unsupported_npm_contracts_are_diagnostic()
    {
        CopelandCompilation missing = CopelandCompiler.CompileToMir("import { delayedTransform } from \"@fixture/missing\";", new CopelandCompilationOptions { SourcePath = "main.ts" });
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Id == "COPE-NPM-0001");

        CopelandCompilation unsupported = CopelandCompiler.CompileToMir("import transform from \"@fixture/transform\";", new CopelandCompilationOptions
        {
            SourcePath = "main.ts",
            NpmPackages = [new CopelandNpmPackageContract("@fixture/transform", "1.0.0", [])],
        });
        Assert.Contains(unsupported.Diagnostics, diagnostic => diagnostic.Id == "COPE-NPM-0002");
    }

    private const string Source = """
        import { delayedTransform } from "@fixture/transform";
        const $schema: string = "copeland://npm/test";
        record Request { value: number; }
        record Response { value: number; }
        record RemoteError { message: string; }
        function request(value: number): Request { return { value }; }
        async function load(value: number): Response ! RemoteError {
            const pending: Async<Response ! RemoteError> = delayedTransform(request(value));
            return await pending;
        }
        """;
}
