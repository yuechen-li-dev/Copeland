using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class InferredRecordShapeTests
{
    [Fact]
    public void Uncontextualized_literals_infer_ordered_immutable_record_shapes()
    {
        const string source = """
interface HasX { x: int; }
function identity<T>(value: T): T { return value; }
function readX<T extends HasX>(value: T): int { return value.x; }

function main(): int {
    const point = { x: 1, y: 2 };
    const sameShape = identity({ x: 3, y: 4 });
    const moved = point with { x: sameShape.x + 6 };
    const nested = { position: moved, label: "ready" };
    return readX(nested.position) + nested.position.y;
}
""";

        CopelandCompilation first = CopelandCompiler.CompileToMir(source);
        CopelandCompilation second = CopelandCompiler.CompileToMir(source);

        Assert.True(first.Success, Describe(first));
        Assert.Equal(first.MirText, second.MirText);
        Assert.Contains("__CopeInferredRecord_", first.MirText, StringComparison.Ordinal);
        Assert.Contains("record-with", first.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_ordered_shape_is_interned_but_field_order_remains_semantic()
    {
        const string sameShape = """
function choose<T>(left: T, right: T): T { return right; }
function main(): int {
    const point = choose({ x: 1, y: 2 }, { x: 3, y: 4 });
    return point.x + point.y;
}
""";
        const string reorderedShape = """
function choose<T>(left: T, right: T): T { return right; }
function main(): int {
    const left = choose({ x: 1, y: 2 }, { x: 1, y: 2 });
    const right = choose({ y: 4, x: 3 }, { y: 4, x: 3 });
    const point = choose(left, right);
    return point.x + point.y;
}
""";

        CopelandCompilation accepted = CopelandCompiler.CompileToMir(sameShape);
        CopelandCompilation rejected = CopelandCompiler.CompileToMir(reorderedShape);

        Assert.True(accepted.Success, Describe(accepted));
        Assert.Single(
            accepted.MirCompilation!.Program!.Records,
            record => record.Name.StartsWith("__CopeInferredRecord_", StringComparison.Ordinal));
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Id == "COPE-INFER-0002");
    }

    [Fact]
    public void Named_context_still_constructs_the_named_nominal_record()
    {
        const string source = """
record Point { x: int; y: int; }
function main(): int {
    const point: Point = { x: 40, y: 2 };
    return point.x + point.y;
}
""";

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.True(compilation.Success, Describe(compilation));
        Assert.Single(compilation.MirCompilation!.Program!.Records);
        Assert.Equal("Point", compilation.MirCompilation.Program.Records[0].Name);
    }

    [Theory]
    [InlineData("function main(): int { const point = { x: 1 }; point.x = 2; return point.x; }", "COPE-REC-0011")]
    [InlineData("function main(): int { const point = { x: 1 }; return (point with { y: 2 }).x; }", "COPE-REC-0007")]
    [InlineData("function main(): int { const point = { x: 1 }; return (point with {}).x; }", "COPE-REC-0013")]
    [InlineData("function bad<T>(value: T): T { const wrapper = { value: value }; return wrapper.value; }", "COPE-REC-0005")]
    public void Inferred_records_reject_mutation_shape_growth_and_empty_updates(string source, string diagnosticId)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
        Assert.Null(compilation.MirCompilation?.Program);
    }

    private static string Describe(CopelandCompilation compilation)
        => string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}"));
}
