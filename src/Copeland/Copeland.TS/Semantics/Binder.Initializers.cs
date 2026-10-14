using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Semantics;

public static partial class Binder
{
    private sealed partial class BinderImpl
    {
        private readonly Dictionary<RepeatedInitializerSyntax, int> _initializerCounts = [];

        private static bool IsInitializerCount(BoundExpression expression)
        {
            return expression switch
            {
                BoundLiteralExpression => true,
                BoundGenericStaticExpression => true,
                BoundUnaryExpression unary => IsInitializerCount(unary.Operand),
                BoundBinaryExpression binary => IsInitializerCount(binary.Left) && IsInitializerCount(binary.Right),
                _ => false,
            };
        }

        private int ResolveInitializerCount(BoundExpression expression, SyntaxToken anchor)
        {
            if (!TypeFacts.AreEquivalent(expression.Type, PrimitiveTypeSymbol.Int))
            {
                Report("COPE-INIT-0001", "Repetition count must have type int.", anchor);
                return 0;
            }
            if (!IsInitializerCount(expression))
            {
                Report("COPE-INIT-0002", "Repetition count must be compile-time integer arithmetic or a closed static parameter; runtime values and function calls are unsupported.", anchor);
                return 0;
            }
            try
            {
                var evaluator = new StaticEvaluator([], new Dictionary<FunctionSymbol, FunctionEffectSummary>(), StaticEvaluationLimits.M1);
                if (evaluator.Evaluate(expression) is StaticPrimitiveValue { Value: int count })
                {
                    if (count < 0)
                    {
                        Report("COPE-INIT-0003", "Repetition count must be nonnegative; zero is valid.", anchor);
                        return 0;
                    }
                    if (count > StaticEvaluationLimits.M1.MaximumArrayLength)
                    {
                        Report("COPE-INIT-0004", "Repetition exceeds the 65536-element initializer limit.", anchor);
                        return 0;
                    }
                    return count;
                }
            }
            catch (StaticEvaluationException exception)
            {
                Report("COPE-INIT-0004", exception.Message, anchor);
            }
            return 0;
        }

        private int BindInitializerCount(RepeatedInitializerSyntax syntax)
        {
            if (_initializerCounts.TryGetValue(syntax, out int existing))
            {
                return existing;
            }
            int count = ResolveInitializerCount(BindExpression(syntax.Count!), syntax.EllipsisToken);
            _initializerCounts.Add(syntax, count);
            return count;
        }

        private int? StatedInitializerLength(ArrayLiteralExpressionSyntax syntax)
        {
            if (syntax.Elements.OfType<RepeatedInitializerSyntax>().Any(element => element.Count is null))
            {
                return null;
            }
            long length = 0;
            foreach (ExpressionSyntax element in syntax.Elements)
            {
                length += element is RepeatedInitializerSyntax repeated ? BindInitializerCount(repeated) : 1;
            }
            if (length > StaticEvaluationLimits.M1.MaximumArrayLength && syntax.Elements.Any(element => element is RepeatedInitializerSyntax))
            {
                Report("COPE-INIT-0004", "Combined repetition exceeds the 65536-element initializer limit.", syntax.OpenBracketToken);
                return 0;
            }
            return (int)length;
        }

        private BoundExpression BindArrayInitializer(ArrayLiteralExpressionSyntax syntax, TypeSymbol? contextual, int? extent = null)
        {
            var values = new List<BoundExpression>();
            var counts = new List<int>();
            var deferred = new Dictionary<int, BoundExpression>();
            bool repeated = syntax.Elements.Any(element => element is RepeatedInitializerSyntax);
            long length = 0;
            for (int index = 0; index < syntax.Elements.Count; index++)
            {
                ExpressionSyntax element = syntax.Elements[index];
                ExpressionSyntax value = element is RepeatedInitializerSyntax repeat ? repeat.Value : element;
                values.Add(BindExpression(value, contextual is ArrayTypeSymbol array ? array.ElementType : null));
                int count = 1;
                if (element is RepeatedInitializerSyntax repetition)
                {
                    if (repetition.Count is null)
                    {
                        if (index != syntax.Elements.Count - 1)
                        {
                            Report("COPE-INIT-0006", "A remainder fill must be the final initializer element.", repetition.EllipsisToken);
                            count = 0;
                        }
                        else if (extent is null)
                        {
                            Report("COPE-INIT-0005", "Nothing fixes this initializer's length. Write an explicit count or a table column that states its length.", repetition.EllipsisToken);
                            count = 0;
                        }
                        else if (length > extent)
                        {
                            Report("COPE-INIT-0007", "Initializer prefix exceeds the destination extent.", repetition.EllipsisToken);
                            count = 0;
                        }
                        else
                        {
                            count = extent.Value - (int)length;
                        }
                    }
                    else if (_initializerCounts.TryGetValue(repetition, out int cached))
                    {
                        count = cached;
                    }
                    else
                    {
                        BoundExpression boundCount = BindExpression(repetition.Count);
                        if (IsInitializerCount(boundCount) && OpenStaticIdentity(boundCount) is not null)
                        {
                            if (!TypeFacts.AreEquivalent(boundCount.Type, PrimitiveTypeSymbol.Int))
                            {
                                Report("COPE-INIT-0001", "Repetition count must have type int.", repetition.EllipsisToken);
                            }
                            else if (extent is not null)
                            {
                                Report("COPE-INIT-0002", "Table repetition counts must be closed when the table is bound; dependent table template initialization is unsupported. Write the table's known extent.", repetition.EllipsisToken);
                            }
                            else
                            {
                                deferred.Add(index, boundCount);
                            }
                            count = 0;
                        }
                        else
                        {
                            count = ResolveInitializerCount(boundCount, repetition.EllipsisToken);
                        }
                    }
                }
                counts.Add(count);
                length += count;
            }
            if (repeated && length > StaticEvaluationLimits.M1.MaximumArrayLength)
            {
                Report("COPE-INIT-0004", "Combined repetition exceeds the 65536-element initializer limit.", syntax.OpenBracketToken);
                for (int index = 0; index < counts.Count; index++)
                {
                    counts[index] = 0;
                }
            }
            TypeSymbol elementType = contextual is ArrayTypeSymbol context
                ? context.ElementType
                : values.FirstOrDefault()?.Type ?? PrimitiveTypeSymbol.Error;
            if (values.Count == 0 && contextual is not ArrayTypeSymbol)
            {
                Report("COPE-TYPE-0010", "Empty array requires contextual type.", syntax.OpenBracketToken);
            }
            foreach (BoundExpression value in values)
            {
                if (contextual is ArrayTypeSymbol ? !IsAssignable(elementType, value.Type) : !TypeFacts.AreEquivalent(elementType, value.Type))
                {
                    Report("COPE-TYPE-0009", "Array element type mismatch.", syntax.OpenBracketToken);
                }
            }
            return new BoundArrayExpression(values, contextual ?? new ArrayTypeSymbol(elementType), repeated ? counts : null, deferred.Count == 0 ? null : deferred, syntax);
        }
    }
}
