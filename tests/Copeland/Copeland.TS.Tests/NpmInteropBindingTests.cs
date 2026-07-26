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

    [Fact]
    public void Relative_import_explains_the_missing_source_module_boundary()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("import { summary } from \"./recipe-book\";", new CopelandCompilationOptions { SourcePath = "main.ts" });

        var diagnostic = Assert.Single(compilation.Diagnostics);
        Assert.Equal("COPE-MODULE-0001", diagnostic.Id);
        Assert.Contains("no source-module resolver", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("declared package contract", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Contract_and_materialization_failures_are_distinct()
    {
        CopelandCompilation noContract = CopelandCompiler.CompileToMir("import { delayedTransform } from \"@fixture/transform\";", new CopelandCompilationOptions
        {
            SourcePath = "main.ts",
            NpmDependencies = new CopelandNpmDependencyGraph([new CopelandNpmPackageContract("@fixture/transform", "1.0.0", [])]),
        });
        Assert.Contains(noContract.Diagnostics, diagnostic => diagnostic.Id == "COPE-NPM-0006");

        CopelandCompilation unavailable = CopelandCompiler.CompileToMir("import { delayedTransform } from \"@fixture/transform\";", new CopelandCompilationOptions
        {
            SourcePath = "main.ts",
            NpmDependencies = new CopelandNpmDependencyGraph([new CopelandNpmPackageContract("@fixture/transform", "1.0.0", [new CopelandNpmFunctionContract("delayedTransform", ["Request"], "Response")], IsMaterialized: false)]),
        });
        Assert.Contains(unavailable.Diagnostics, diagnostic => diagnostic.Id == "COPE-NPM-0007");
    }

    [Fact]
    public void Reserved_generated_helper_alias_is_rejected()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("import { delayedTransform as __cope_async_pending } from \"@fixture/transform\";", new CopelandCompilationOptions
        {
            SourcePath = "main.ts",
            NpmDependencies = new CopelandNpmDependencyGraph([new CopelandNpmPackageContract("@fixture/transform", "1.0.0", [new CopelandNpmFunctionContract("delayedTransform", ["Request"], "Response")])]),
        });
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-NPM-0008");
    }

    [Fact]
    public void Positional_npm_arguments_are_bound_without_an_authored_request_wrapper()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            import { sum } from "@fixture/math";
            const $schema: string = "copeland://npm/test";
            record RemoteError { message: string; }
            async function add(left: number, right: number): number ! RemoteError {
                const pending: Async<number ! RemoteError> = sum(left, right);
                return await pending;
            }
            """, new CopelandCompilationOptions
        {
            SourcePath = "main.ts",
            NpmDependencies = new CopelandNpmDependencyGraph(
            [
                new CopelandNpmPackageContract("@fixture/math", "1.0.0", [new CopelandNpmFunctionContract("sum", ["number", "number"], "number", "RemoteError", IsPromise: true)]),
            ]),
        });

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains("npm:@fixture/math@1.0.0:sum", compilation.MirText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sum(1)", "expected 2, got 1")]
    [InlineData("sum(1, 2, 3)", "expected 2, got 3")]
    public void Positional_npm_arity_is_diagnostic(string invocation, string message)
    {
        CopelandCompilation compilation = CompileMathCall(invocation, ["number", "number"], "number");

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TYPE-0004" && diagnostic.Message.Contains(message, StringComparison.Ordinal));
    }

    [Fact]
    public void Positional_npm_argument_type_mismatch_is_diagnostic_at_its_position()
    {
        CopelandCompilation compilation = CompileMathCall("sum(1, true)", ["number", "number"], "number");

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TYPE-0005" && diagnostic.Message.Contains("number", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("number[][]", "number")]
    [InlineData("number", "number[][]")]
    public void Unsupported_nested_npm_argument_or_return_shape_is_rejected(string parameter, string result)
    {
        CopelandCompilation compilation = CompileMathCall("sum(1)", [parameter], result);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-NPM-0005");
    }

    private static CopelandCompilation CompileMathCall(string invocation, IReadOnlyList<string> parameters, string result)
    {
        return CopelandCompiler.CompileToMir($$"""
            import { sum } from "@fixture/math";
            const $schema: string = "copeland://npm/test";
            record RemoteError { message: string; }
            async function call(): {{result}} ! RemoteError {
                const pending: Async<{{result}} ! RemoteError> = {{invocation}};
                return await pending;
            }
            """, new CopelandCompilationOptions
        {
            SourcePath = "main.ts",
            NpmDependencies = new CopelandNpmDependencyGraph(
            [
                new CopelandNpmPackageContract("@fixture/math", "1.0.0", [new CopelandNpmFunctionContract("sum", parameters, result, "RemoteError")]),
            ]),
        });
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
