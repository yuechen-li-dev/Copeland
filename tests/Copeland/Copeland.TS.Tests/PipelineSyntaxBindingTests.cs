using Copeland.TS.Compiler;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class PipelineSyntaxBindingTests
{
    [Fact]
    public void Pipeline_is_left_associative_and_binds_to_the_same_mir_as_nested_calls()
    {
        const string piped = """
            function increment(value: number): number { return value + 1; }
            function double(value: number): number { return value * 2; }
            function main(): number { return 20 |> increment |> double; }
            """;
        const string handwritten = """
            function increment(value: number): number { return value + 1; }
            function double(value: number): number { return value * 2; }
            function main(): number { return double(increment(20)); }
            """;

        SyntaxTree tree = SyntaxTree.Parse(piped);
        var declaration = Assert.IsType<FunctionDeclarationSyntax>(tree.Root.Members[2]);
        var result = Assert.IsType<ReturnStatementSyntax>(declaration.Body.Statements[0]);
        var outer = Assert.IsType<BinaryExpressionSyntax>(result.Expression);
        Assert.Equal(SyntaxKind.PipeGreaterToken, outer.OperatorToken.Kind);
        var inner = Assert.IsType<BinaryExpressionSyntax>(outer.Left);
        Assert.Equal(SyntaxKind.PipeGreaterToken, inner.OperatorToken.Kind);

        CopelandCompilation pipedCompilation = CopelandCompiler.CompileToMir(piped);
        CopelandCompilation handwrittenCompilation = CopelandCompiler.CompileToMir(handwritten);

        Assert.True(pipedCompilation.Success, string.Join(Environment.NewLine, pipedCompilation.Diagnostics));
        Assert.True(handwrittenCompilation.Success, string.Join(Environment.NewLine, handwrittenCompilation.Diagnostics));
        Assert.Equal(handwrittenCompilation.MirText, pipedCompilation.MirText);
    }

    [Fact]
    public void Imported_function_pipeline_uses_the_existing_local_module_call_law()
    {
        CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource(
                "Normalize.ts",
                "Normalize.ts",
                "export function Normalize(value: number): number { return value * 2; }"),
            new CopelandProjectSource(
                "Main.ts",
                "Main.ts",
                "import { Normalize as NormalizeValue } from './Normalize'; export function Run(): number { return 21 |> NormalizeValue; }"),
        ],
        new CopelandCompilationOptions { SourcePath = "Project.ts" });

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains("call Normalize", compilation.Compilation!.MirText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("function f(value: number): number { return value; } function main(): number { return 1 |> f(2); }", "COPE-PIPE-0001")]
    [InlineData("function main(): number { return 1 |> 2; }", "COPE-CALL-0004")]
    public void Pipeline_rejections_are_repair_oriented(string source, string diagnosticId)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId && diagnostic.Length > 0);
    }
}
