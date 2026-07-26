using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class GeneratorSyntaxBindingTests
{
    [Fact]
    public void Generator_aliases_bind_to_one_typed_yield_operation()
    {
        const string source = """
            export function* values(): Iterable<number> {
                yield 1;
                yield return 2;
                yield break;
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        BoundFunctionDeclaration function = Assert.Single(compilation.BoundCompilation!.Program.Functions);
        Assert.True(function.Symbol.IsGenerator);
        Assert.IsType<IterableTypeSymbol>(function.Symbol.ReturnType);
        Assert.IsType<BoundYieldStatement>(function.Body.Statements[0]);
        Assert.IsType<BoundYieldStatement>(function.Body.Statements[1]);
        Assert.IsType<BoundReturnStatement>(function.Body.Statements[2]);

        MirFunction lowered = Assert.Single(compilation.MirCompilation!.Program!.Functions);
        Assert.True(lowered.IsGenerator);
        Assert.All(lowered.Body.Take(2), statement => Assert.IsType<MirYieldStatement>(statement));
    }

    [Theory]
    [InlineData("function invalid(): number { yield 1; }", "COPE-GEN-0003")]
    [InlineData("function* invalid(): Iterable<number> { return 1; }", "COPE-GEN-0005")]
    [InlineData("async function* invalid(): Iterable<number> { yield 1; }", "COPE-GEN-0002")]
    public void Generator_misuse_has_focused_diagnostics(string source, string diagnosticId)
    {
        CopelandCompilation compilation = CopelandCompiler.Compile(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }
}
