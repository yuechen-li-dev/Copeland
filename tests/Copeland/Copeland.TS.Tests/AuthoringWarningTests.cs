using Copeland.TS.Compiler;
using Copeland.TS.Gpu;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class AuthoringWarningTests
{
    private const string Chain = """
        function Read(value: number): number {
            if (value == 0) { return 0; }
            else if (value == 1) { return 1; }
            else if (value == 2) { return 2; }
            else if (value == 3) { return 3; }
            else if (value == 4) { return 4; }
            else if (value == 5) { return 5; }
            else { return 6; }
        }
        """;

    [Fact]
    public void Long_else_if_is_legal_and_emits_one_nonblocking_authoring_warning()
    {
        var syntax = SyntaxTree.Parse(Chain);
        Assert.Empty(syntax.Diagnostics);
        var warning = Assert.Single(syntax.Warnings);
        Assert.Equal("COPE-STYLE-0001", warning.Id);
        Assert.Contains("6 conditional branches", warning.Message);
        var compilation = CopelandCompiler.CompileToMir(Chain);
        Assert.True(compilation.Success, string.Join("; ", compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    [InlineData(7, 1)]
    public void Warning_threshold_and_deduplication_follow_chain_roots(int branches, int warningCount)
    {
        string chain = string.Join(" else ", Enumerable.Range(0, branches)
            .Select(value => $"if (value == {value}) {{ return {value}; }}"));
        var syntax = SyntaxTree.Parse($"function Read(value: number): number {{ {chain} else {{ return 9; }} }}");
        Assert.Empty(syntax.Diagnostics);
        Assert.Equal(warningCount, syntax.Warnings.Count);
    }

    [Fact]
    public void Visual_compute_warnings_do_not_block_the_closed_gpu_module()
    {
        string source = Chain.Replace("number", "f32", StringComparison.Ordinal) + """
            @compute @numthreads(1, 1, 1)
            function Main(@builtin(dispatchThreadId) thread: uint3,
                @binding(0) readwrite Output: StorageBuffer<f32>): void {
                Output[thread.x] = Read(0.0);
            }
            """;
        // GPU constants require f32 operands; keep the shape of the authored chain.
        for (int value = 0; value <= 6; value++)
        {
            source = source.Replace($"== {value})", $"== {value}.0)", StringComparison.Ordinal)
                .Replace($"return {value};", $"return {value}.0;", StringComparison.Ordinal);
        }
        var module = GpuComputeBinder.Compile(new([new("warning.v.ts", source)]));
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal("COPE-STYLE-0001", Assert.Single(module.Warnings!).Code);
    }
}
