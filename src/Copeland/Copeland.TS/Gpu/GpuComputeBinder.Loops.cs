using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

public static partial class GpuComputeBinder
{
    private sealed partial class Binder
    {
        private VdMirStatement BindBoundedLoop(string path, ForOfStatementSyntax loop,
            Dictionary<string, ValueBinding> scope, string returnType, IReadOnlyList<VdMirStatement> preceding)
        {
            var iterator = (GenericCallExpressionSyntax)loop.Iterable;
            if (loop.DeclarationKeyword.Kind != SyntaxKind.LetKeyword || scope.ContainsKey(loop.Identifier.Text)
                || _values.StaticParameter(loop.Identifier.Text) is not null
                || iterator.TypeArguments is not [LiteralTypeSyntax { LiteralToken.Value: int maximum }]
                || maximum is < 1 or > 512 || iterator.Arguments.Count != 1)
            {
                AddDiagnostic("COPE-GPU-LOOP-0001", "SDSL-V4500", "bounded-loop",
                    "BoundedIndices<Max>(count) needs a literal Max of 1..512, one uniform u32 count and a fresh let index.", Span(path, loop));
                return new("block", Span(path, loop), Body: []);
            }
            var count = BindExpression(path, iterator.Arguments[0], scope, "u32");
            if (count.Type != "u32" || !IsWorkgroupUniform(count, FlattenBlocks(preceding)))
            {
                AddDiagnostic("COPE-GPU-LOOP-0002", "SDSL-V4501", "bounded-loop",
                    "Bounded loop counts must be unshadowed immutable workgroup-uniform literals or readonly parameters.", Span(path, iterator.Arguments[0]));
            }
            var nestedScope = new Dictionary<string, ValueBinding>(scope, StringComparer.Ordinal)
            {
                [loop.Identifier.Text] = new("u32", false, null, false),
            };
            var body = BindNestedStatement(path, loop.Body, nestedScope, returnType);
            if (Descendants(body).Any(statement => statement.Kind is "return" or "cooperative-matmul"))
            {
                AddDiagnostic("COPE-GPU-LOOP-0003", "SDSL-V4502", "bounded-loop",
                    "Bounded loops cannot return from the invocation or issue cooperative-matrix commands; all iterations must complete.", Span(path, loop));
            }
            return new("bounded-loop", Span(path, loop), Name: loop.Identifier.Text,
                Type: "u32", Expression: count, Body: body) { MaximumIterations = maximum };
        }
    }
}
