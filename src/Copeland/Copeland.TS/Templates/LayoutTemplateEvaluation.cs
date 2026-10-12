using Copeland.TS.Diagnostics;
using Copeland.TS.MachinaSource;
using Copeland.TS.Mir.Machina;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Templates;

public static partial class TemplateCompiler
{
    /// <summary>The layout consumer evaluates the same typed template plans before runtime binding.</summary>
    internal static object? EvaluateValue(IReadOnlyList<BoundTemplateDeclaration> templates,
        BoundTemplateValue value, List<Diagnostic> diagnostics, IReadOnlyList<BoundFunctionDeclaration> functions)
    {
        IReadOnlyList<BoundTemplateDeclaration> reachable = CollectLayoutTemplates(templates, value);
        var evaluator = new BoundPlanEvaluator([], reachable, diagnostics, functions);
        return evaluator.EvaluateRootValue(value);
    }

    private static IReadOnlyList<BoundTemplateDeclaration> CollectLayoutTemplates(
        IReadOnlyList<BoundTemplateDeclaration> templates, BoundTemplateValue value)
    {
        var declarations = new HashSet<BoundTemplateDeclaration>();
        var pending = new Stack<object>();
        pending.Push(value);
        foreach (BoundTemplateDeclaration template in templates) pending.Push(template);
        while (pending.TryPop(out object? node))
        {
            switch (node)
            {
                case BoundTemplateDeclaration declaration when declarations.Add(declaration):
                    if (declaration.Plan is not null) pending.Push(declaration.Plan);
                    foreach (BoundTemplateValue? argument in declaration.ParameterDefaults)
                    {
                        if (argument is not null) pending.Push(argument);
                    }
                    break;
                case BoundTemplateInvocation invocation:
                    if (invocation.Template.Declaration is { } target) pending.Push(target);
                    foreach (BoundTemplateValue argument in invocation.Arguments) pending.Push(argument);
                    break;
                case BoundTemplateDefaultArgument argument:
                    pending.Push(argument.Value);
                    break;
                case BoundTemplateBlock block:
                    foreach (BoundTemplateStatement statement in block.Statements) pending.Push(statement);
                    break;
                case BoundTemplateReturn { Value: not null } returned:
                    pending.Push(returned.Value);
                    break;
                case BoundTemplateLocal local:
                    pending.Push(local.Initializer);
                    break;
                case BoundTemplateEmit emit:
                    pending.Push(emit.Value);
                    break;
                case BoundStaticIf conditional:
                    pending.Push(conditional.Condition);
                    pending.Push(conditional.ThenStatement);
                    if (conditional.ElseStatement is not null) pending.Push(conditional.ElseStatement);
                    break;
                case BoundStaticFor loop:
                    pending.Push(loop.Values);
                    pending.Push(loop.Body);
                    break;
                case BoundStaticMatch selection:
                    pending.Push(selection.Input);
                    foreach (BoundStaticMatchArm arm in selection.Arms) pending.Push(arm.Statement);
                    break;
                case BoundTemplateStructuralObject record:
                    foreach (BoundTemplateStructuralField field in record.Fields) pending.Push(field.Value);
                    break;
                case BoundTemplateArray array:
                    foreach (BoundTemplateValue element in array.Elements) pending.Push(element);
                    break;
                case BoundTemplateMemberAccess access:
                    pending.Push(access.Receiver);
                    break;
                case BoundTemplateString text:
                    foreach (BoundTemplateValue part in text.Parts) pending.Push(part);
                    break;
                case BoundTemplateBinary binary:
                    pending.Push(binary.Left);
                    pending.Push(binary.Right);
                    break;
                case BoundTemplateLayoutConstruction layout:
                    pending.Push(layout.X);
                    pending.Push(layout.Y);
                    foreach (BoundLayoutPropertyPlan property in layout.Properties) pending.Push(property.Value);
                    foreach (BoundLayoutNodePlan child in layout.Nodes) pending.Push(child);
                    break;
                case BoundLayoutNodePlan layoutNode:
                    if (layoutNode.Condition is not null) pending.Push(layoutNode.Condition);
                    foreach (BoundLayoutPropertyPlan property in layoutNode.Properties) pending.Push(property.Value);
                    foreach (BoundLayoutNodePlan child in layoutNode.Children) pending.Push(child);
                    foreach (BoundLayoutNodePlan child in layoutNode.ElseChildren ?? []) pending.Push(child);
                    break;
            }
        }
        return declarations.OrderBy(declaration => declaration.Symbol.StableIdentity, StringComparer.Ordinal).ToArray();
    }

    private static string DescribeLayoutValue(BoundLayoutDeclaration layout)
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            layout.Name,
            layout.Profile,
            layout.Origin,
            Layers = layout.ResolvedLayerSet.Layers,
            Contract = layout.Satisfaction?.ContractName,
            Root = DescribeNode(layout.Root)
        });

        static object DescribeNode(BoundLayoutNode node)
        {
            return new
            {
                node.Name,
                node.Kind,
                Dimensions = node.Dimensions.OrderBy(field => field.Key, StringComparer.Ordinal).ToArray(),
                Positions = node.Positions.OrderBy(field => field.Key, StringComparer.Ordinal).ToArray(),
                node.Gap,
                node.Columns,
                node.Padding,
                node.Style,
                Paint = node.ResolvedPaint,
                Overflow = node.ResolvedOverflow,
                TextFit = node.TextFit,
                Children = node.Children.Select(DescribeNode).ToArray()
            };
        }
    }

    private sealed partial class BoundPlanEvaluator
    {
        public object? EvaluateRootValue(BoundTemplateValue value)
            => EvaluateValue(value, new BoundEvaluationContext([]));

        private static object? Unwrap(object? value)
            => value is StaticPrimitiveValue primitive ? primitive.Value : value;

        private BoundLayoutDeclaration? EvaluateLayout(BoundTemplateLayoutConstruction plan, BoundEvaluationContext context)
        {
            int diagnosticCount = _diagnostics.Count;
            LayoutPropertyData[] Properties(IReadOnlyList<BoundLayoutPropertyPlan> properties)
                => properties.Select(property => new LayoutPropertyData(property.Identifier,
                    SpatialValue(property.Value.Anchor, EvaluateValue(property.Value, context)))).ToArray();
            IEnumerable<LayoutNodeData> Nodes(IReadOnlyList<BoundLayoutNodePlan> nodes)
            {
                foreach (BoundLayoutNodePlan node in nodes)
                {
                    if (node.Condition is not null)
                    {
                        object? condition = Unwrap(EvaluateValue(node.Condition, context));
                        if (condition is not bool selected)
                        {
                            Report("COPE-TEMPLATE-0002", "A static layout condition must evaluate to boolean.", node.KindToken);
                            continue;
                        }
                        foreach (LayoutNodeData child in Nodes(selected ? node.Children : node.ElseChildren ?? []))
                        {
                            yield return child;
                        }
                    }
                    else
                    {
                        yield return new LayoutNodeData(node.KindToken, node.Identifier,
                            Properties(node.Properties), Nodes(node.Children).ToArray());
                    }
                }
            }
            var construction = new LayoutConstruction(plan.Anchor, plan.Profile,
                SpatialCoordinate(plan.X.Anchor, EvaluateValue(plan.X, context)),
                SpatialCoordinate(plan.Y.Anchor, EvaluateValue(plan.Y, context)), Properties(plan.Properties),
                Nodes(plan.Nodes).ToArray(), plan.Contract is null ? null : plan.Anchor with { Text = plan.Contract.Name });
            if (_diagnostics.Count != diagnosticCount) return null;
            BoundLayoutDeclaration? result = LayoutDataCompiler.BindConstruction(construction, plan.SourcePath,
                out IReadOnlyList<Diagnostic> diagnostics, plan.Contract);
            _diagnostics.AddRange(diagnostics);
            return result;
        }

        private LayoutValue SpatialCoordinate(SyntaxToken anchor, object? value)
        {
            if (value is StaticRecordValue { RecordType: LengthTypeSymbol } record)
            {
                double quantity = Convert.ToDouble(Unwrap(record.Fields[record.RecordType.Fields[0]]), System.Globalization.CultureInfo.InvariantCulture);
                string? unit = Unwrap(record.Fields[record.RecordType.Fields[1]]) as string;
                if (double.IsFinite(quantity) && (unit is "px" or "ui"))
                {
                    return new LayoutValue(anchor, new BoundLayoutCoordinate(quantity,
                        unit == "px" ? LayoutCoordinateUnit.Px : LayoutCoordinateUnit.Ui));
                }
            }
            Report("COPE-LAYOUT-ORIGIN-0002", "A layout origin requires a finite static px or ui coordinate.", anchor);
            return new LayoutValue(anchor);
        }

        private LayoutValue SpatialValue(SyntaxToken anchor, object? value)
        {
            value = Unwrap(value);
            if (value is StaticRecordValue record)
            {
                if (record.RecordType is LengthTypeSymbol)
                {
                    double quantity = Convert.ToDouble(Unwrap(record.Fields[record.RecordType.Fields[0]]),
                        System.Globalization.CultureInfo.InvariantCulture);
                    string? unit = Unwrap(record.Fields[record.RecordType.Fields[1]]) as string;
                    if (!double.IsFinite(quantity) || unit is not ("px" or "ui"))
                    {
                        Report("COPE-LAYOUT-DIMENSION-0001", "A Length requires a finite quantity and unit px or ui.", anchor);
                        return new LayoutValue(anchor);
                    }
                    try
                    {
                        return new LayoutValue(anchor, unit == "px" ? MachinaLength.Pixels(quantity) : MachinaLength.Normalized(quantity));
                    }
                    catch (MachinaLayoutException exception)
                    {
                        Report(exception.Code, exception.Message, anchor);
                        return new LayoutValue(anchor);
                    }
                }
                return new LayoutValue(anchor, Fields: record.Fields.ToDictionary(field => field.Key.Name,
                    field => SpatialValue(anchor, field.Value), StringComparer.Ordinal));
            }
            if (value is LayoutKeywordValue keyword) return new LayoutValue(anchor, Keyword: keyword.Name);
            if (value is IReadOnlyDictionary<string, object?> fields)
            {
                return new LayoutValue(anchor, Fields: fields.ToDictionary(field => field.Key,
                    field => SpatialValue(anchor, field.Value), StringComparer.Ordinal));
            }
            return new LayoutValue(anchor, value);
        }
    }
}
