using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Semantics;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

/// <summary>Expands finite authoring-time iterations through the ordinary GPU binder.</summary>
internal sealed class GpuStaticFor
{
    private const int MaximumIterations = 256;
    private const int MaximumExpandedIterations = 4096;
    private const int MaximumDepth = 16;
    private int expandedIterations;
    private int depth;

    public IReadOnlyList<VdMirStatement> Expand(
        StaticForStatementSyntax loop,
        VdMirSourceSpan source,
        GpuValues values,
        bool nameExists,
        Func<ExpressionSyntax, VdMirExpression> bindExpression,
        Func<IEnumerable<VdMirFunction>> functions,
        Func<IReadOnlyList<VdMirStatement>> bindBody,
        Action<string, string, VdMirSourceSpan> error)
    {
        if (nameExists || values.StaticParameter(loop.Identifier.Text) is not null)
        {
            error("COPE-GPU-STATIC-0002", "Static for requires a fresh immutable iteration name.", source);
            return [];
        }
        if (depth >= MaximumDepth)
        {
            error("COPE-STATIC-0015", "Static for exceeds the 16-level expansion depth budget.", source);
            return [];
        }

        try
        {
            ExpressionSyntax iterable = loop.Iterable;
            if (iterable is GenericCallExpressionSyntax { Target: NameExpressionSyntax { IdentifierToken.Text: "Indices" } } indices)
            {
                if (indices.Arguments.Count != 0 || indices.TypeArguments is not [LiteralTypeSyntax { LiteralToken.Value: int extent }]
                    || extent is < 0 or > 256)
                {
                    throw StaticEvaluationException.Unsupported("Indices<N>() requires one literal extent from 0 through 256 and no runtime arguments.");
                }
                iterable = new ArrayLiteralExpressionSyntax(
                    new(SyntaxKind.OpenBracketToken, loop.Identifier.Position, "[", null),
                    Enumerable.Range(0, extent).Select(index => (ExpressionSyntax)new LiteralExpressionSyntax(
                        new(SyntaxKind.NumberToken, loop.Identifier.Position, index.ToString(System.Globalization.CultureInfo.InvariantCulture), index))).ToArray(),
                    Enumerable.Range(0, Math.Max(0, extent - 1)).Select(_ => new SyntaxToken(SyntaxKind.CommaToken, loop.Identifier.Position, ",", null)).ToArray(),
                    new(SyntaxKind.CloseBracketToken, loop.Identifier.Position, "]", null));
            }
            while (iterable is ParenthesizedExpressionSyntax parenthesized)
            {
                iterable = parenthesized.Expression;
            }

            IReadOnlyList<VdMirExpression> elements;
            if (iterable is ArrayLiteralExpressionSyntax array)
            {
                // No runtime array or storage layout is needed for a compile-time list.
                if (array.Elements.Count > MaximumIterations)
                {
                    throw StaticEvaluationException.Budget("Static for admits at most 256 values per loop.");
                }
                elements = array.Elements.Select(bindExpression).ToArray();
            }
            else
            {
                VdMirExpression bound = bindExpression(iterable);
                VdMirValueType? shape = values.Find(bound.Type);
                if (shape is not { Kind: "array", Shape.Count: 1 })
                {
                    throw StaticEvaluationException.Unsupported("Static for requires a finite literal list or a compile-time fixed Array value.");
                }
                VdMirExpression aggregate = Fold(bound);
                elements = aggregate.Operands ?? [];
            }

            if (elements.Count > MaximumIterations || expandedIterations + elements.Count > MaximumExpandedIterations)
            {
                throw StaticEvaluationException.Budget("Static for exceeds its 256-value loop or 4096-iteration module budget.");
            }

            // Certify every item before binding any body. Runtime data cannot accidentally unroll.
            VdMirExpression[] constants = elements.Select(Fold).ToArray();
            if (constants.Any(value => value.Type is not ("u32" or "f32" or "bool")))
            {
                throw StaticEvaluationException.Unsupported("Static for iteration values currently require u32, f32 or bool.");
            }
            if (constants.Select(value => value.Type).Distinct().Count() > 1)
            {
                throw StaticEvaluationException.Unsupported("Static for iteration values require one homogeneous scalar type.");
            }

            expandedIterations += constants.Length;
            depth++;
            try
            {
                var result = new List<VdMirStatement>();
                foreach (VdMirExpression value in constants)
                {
                    using var context = values.EnterStaticValue(loop.Identifier.Text, value);
                    result.Add(new("block", source, Body: bindBody()));
                }
                return result;
            }
            finally
            {
                depth--;
            }
        }
        catch (StaticEvaluationException exception)
        {
            error(exception.DiagnosticId, exception.Message, source);
            return [];
        }

        VdMirExpression Fold(VdMirExpression expression)
        {
            return GpuStaticEvaluation.Fold(expression, functions(), values.StaticDefinitions, aggregate => aggregate);
        }
    }
}
