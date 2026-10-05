using System.Globalization;
using System.Text;
using Copeland.TS.Diagnostics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Templates;

public static class StateMachineDiagramLimits
{
    public const int MaximumStates = 256;
    public const int MaximumTransitions = 1_024;
    public const int MaximumGuardDisplayBytes = 120;
    public const int MaximumGuardSemanticBytes = 4_096;
    public const int MaximumEmittedDiagramBytes = 1_048_576;
}

public sealed record StateMachineState(
    string Identity,
    string Label,
    FlowSourceCorrelation Source);

public sealed record StateMachineTransition(
    string Identity,
    int Order,
    string SourceStateIdentity,
    string TargetStateIdentity,
    string Event,
    BoundExpression? Guard,
    FlowSourceCorrelation Source,
    FlowSourceCorrelation? GuardSource);

/// <summary>
/// Bounded, syntax-free semantic view shared by any state-diagram backend.
/// Bound guard expressions remain semantic compiler data, never source text.
/// </summary>
public sealed record StateMachineSemanticView(
    string Identity,
    string Name,
    IReadOnlyList<StateMachineState> States,
    IReadOnlyList<StateMachineTransition> Transitions,
    string InitialStateIdentity,
    IReadOnlyList<string> FinalStateIdentities,
    FlowSourceCorrelation Source);

public static class StateMachineDiagramProjection
{
    public static bool TryCreateSemanticView(
        BoundFlowDefinition flow,
        out StateMachineSemanticView? view,
        out IReadOnlyList<Diagnostic> diagnostics)
    {
        var errors = new List<Diagnostic>();
        Dictionary<string, BoundFlowState> statesByName = flow.States
            .GroupBy(state => state.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var transitions = new List<StateMachineTransition>();
        int globalOrder = 0;

        foreach (BoundFlowState source in flow.States)
        {
            foreach (BoundFlowTransition transition in source.Transitions)
            {
                if (!statesByName.TryGetValue(transition.TargetState, out BoundFlowState? target))
                {
                    errors.Add(new Diagnostic(
                        "COPE-STATE-DIAGRAM-0002",
                        $"Transition '{transition.StableIdentity}' references missing state '{transition.TargetState}'.",
                        0,
                        0,
                        transition.Source.Path));
                    continue;
                }

                transitions.Add(new StateMachineTransition(
                    transition.StableIdentity,
                    globalOrder++,
                    source.StableIdentity,
                    target.StableIdentity,
                    transition.EventName,
                    transition.Guard,
                    transition.Source,
                    transition.GuardSource));
            }
        }

        if (!statesByName.TryGetValue(flow.InitialState, out BoundFlowState? initial))
        {
            errors.Add(new Diagnostic(
                "COPE-STATE-DIAGRAM-0003",
                $"Flow '{flow.Name}' has no semantic initial state '{flow.InitialState}'.",
                0,
                0,
                flow.Source.Path));
        }

        diagnostics = errors;
        if (errors.Count > 0)
        {
            view = null;
            return false;
        }

        view = new StateMachineSemanticView(
            flow.StableIdentity,
            flow.Name,
            flow.States.Select(state => new StateMachineState(
                state.StableIdentity,
                state.Name,
                state.Source)).ToArray(),
            transitions,
            initial!.StableIdentity,
            flow.States
                .Where(state => state.Terminal is not null)
                .Select(state => state.StableIdentity)
                .ToArray(),
            flow.Source);
        return true;
    }

    public static bool TryProject(
        BoundProgram program,
        string flowName,
        out StateMachineSemanticView? view,
        out Diagram? diagram,
        out IReadOnlyList<Diagnostic> diagnostics)
    {
        BoundFlowDefinition? flow = program.Flows.SingleOrDefault(
            candidate => string.Equals(candidate.Name, flowName, StringComparison.Ordinal));
        if (flow is null)
        {
            view = null;
            diagram = null;
            diagnostics =
            [
                new Diagnostic(
                    "COPE-STATE-DIAGRAM-0001",
                    $"Flow visualization target '{flowName}' was not found.",
                    0,
                    0),
            ];
            return false;
        }

        if (!TryCreateSemanticView(flow, out view, out diagnostics))
        {
            diagram = null;
            return false;
        }
        return TryProject(view!, out diagram, out diagnostics);
    }

    public static bool TryProject(
        StateMachineSemanticView view,
        out Diagram? diagram,
        out IReadOnlyList<Diagnostic> diagnostics)
    {
        var errors = Validate(view);
        if (errors.Count > 0)
        {
            diagram = null;
            diagnostics = errors;
            return false;
        }

        var edges = new List<DiagramEdge>();
        foreach (StateMachineTransition transition in view.Transitions)
        {
            if (!GuardDisplayFormatter.TryFormat(
                transition.Guard,
                out string? guardDisplay,
                out string? guardError))
            {
                errors.Add(new Diagnostic(
                    "COPE-STATE-DIAGRAM-0008",
                    $"Guard on transition '{transition.Identity}' cannot be represented: {guardError}",
                    0,
                    0,
                    transition.GuardSource?.Path ?? transition.Source.Path));
                continue;
            }

            string label = guardDisplay is null
                ? transition.Event
                : transition.Event + " [" + guardDisplay + "]";
            edges.Add(new DiagramEdge(
                transition.SourceStateIdentity,
                transition.TargetStateIdentity,
                label,
                transition.Identity,
                transition.Order));
        }

        if (errors.Count > 0)
        {
            diagram = null;
            diagnostics = errors;
            return false;
        }

        if (Diagram.TryCreate(
                view.States.Select(state => new DiagramNode(state.Identity, state.Label)),
                edges,
                DiagramDirection.TopDown,
                new DiagramProvenance("stateDiagram", view.Identity),
                out diagram,
                out IReadOnlyList<Diagnostic> diagramDiagnostics,
                DiagramBackendKind.State,
                view.InitialStateIdentity,
                view.FinalStateIdentities))
        {
            string mermaid = MermaidEmitter.Emit(diagram!);
            if (Encoding.UTF8.GetByteCount(mermaid) <= StateMachineDiagramLimits.MaximumEmittedDiagramBytes)
            {
                diagnostics = [];
                return true;
            }
            errors.Add(new Diagnostic(
                "COPE-STATE-DIAGRAM-0009",
                $"State diagram emits more than {StateMachineDiagramLimits.MaximumEmittedDiagramBytes} UTF-8 bytes.",
                0,
                0,
                view.Source.Path));
        }
        else
        {
            errors.AddRange(diagramDiagnostics);
        }

        diagram = null;
        diagnostics = errors;
        return false;
    }

    private static List<Diagnostic> Validate(StateMachineSemanticView view)
    {
        var errors = new List<Diagnostic>();
        if (string.IsNullOrWhiteSpace(view.Identity))
        {
            errors.Add(new Diagnostic(
                "COPE-STATE-DIAGRAM-0004",
                "State-machine semantic identity must be non-empty.",
                0,
                0,
                view.Source.Path));
        }
        if (view.States.Count > StateMachineDiagramLimits.MaximumStates)
        {
            errors.Add(new Diagnostic(
                "COPE-STATE-DIAGRAM-0005",
                $"State diagram has {view.States.Count} states; maximum is {StateMachineDiagramLimits.MaximumStates}.",
                0,
                0,
                view.Source.Path));
        }
        if (view.Transitions.Count > StateMachineDiagramLimits.MaximumTransitions)
        {
            errors.Add(new Diagnostic(
                "COPE-STATE-DIAGRAM-0006",
                $"State diagram has {view.Transitions.Count} transitions; maximum is {StateMachineDiagramLimits.MaximumTransitions}.",
                0,
                0,
                view.Source.Path));
        }

        string[] missingStateIdentities = view.States
            .Where(state => string.IsNullOrWhiteSpace(state.Identity))
            .Select(state => state.Label)
            .ToArray();
        foreach (string label in missingStateIdentities)
        {
            errors.Add(new Diagnostic(
                "COPE-STATE-DIAGRAM-0007",
                $"State '{label}' has no semantic identity.",
                0,
                0,
                view.Source.Path));
        }

        foreach (IGrouping<string, StateMachineTransition> group in view.Transitions
            .GroupBy(transition => transition.Identity, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            {
                string display = string.IsNullOrWhiteSpace(group.Key) ? "<empty>" : group.Key;
                errors.Add(new Diagnostic(
                    "COPE-STATE-DIAGRAM-0010",
                    $"Transition semantic identity '{display}' must be non-empty and unique.",
                    0,
                    0,
                    group.First().Source.Path));
            }
        }
        return errors;
    }

    private static class GuardDisplayFormatter
    {
        public static bool TryFormat(
            BoundExpression? guard,
            out string? display,
            out string? error)
        {
            if (guard is null)
            {
                display = null;
                error = null;
                return true;
            }
            if (!TryFormatExpression(guard, out string full))
            {
                display = null;
                error = $"unsupported semantic expression '{guard.GetType().Name}'";
                return false;
            }
            int semanticBytes = Encoding.UTF8.GetByteCount(full);
            if (semanticBytes > StateMachineDiagramLimits.MaximumGuardSemanticBytes)
            {
                display = null;
                error = $"semantic display requires {semanticBytes} UTF-8 bytes; maximum is {StateMachineDiagramLimits.MaximumGuardSemanticBytes}";
                return false;
            }
            display = TruncateUtf8(full, StateMachineDiagramLimits.MaximumGuardDisplayBytes);
            error = null;
            return true;
        }

        private static bool TryFormatExpression(BoundExpression expression, out string display)
        {
            switch (expression)
            {
                case BoundLiteralExpression literal:
                    display = FormatLiteral(literal.Value);
                    return true;
                case BoundVariableExpression variable:
                    display = variable.Variable.Name;
                    return true;
                case BoundRecordFieldAccessExpression field
                    when TryFormatExpression(field.Receiver, out string receiver):
                    display = receiver + "." + field.Field.Name;
                    return true;
                case BoundUnaryExpression unary
                    when TryGetOperator(unary.OperatorKind, out string unaryOperator)
                    && TryFormatExpression(unary.Operand, out string operand):
                    display = unaryOperator + ParenthesizeIfBinary(unary.Operand, operand);
                    return true;
                case BoundBinaryExpression binary
                    when TryGetOperator(binary.OperatorKind, out string binaryOperator)
                    && TryFormatExpression(binary.Left, out string left)
                    && TryFormatExpression(binary.Right, out string right):
                    display = ParenthesizeIfBinary(binary.Left, left)
                        + " " + binaryOperator + " "
                        + ParenthesizeIfBinary(binary.Right, right);
                    return true;
                case BoundNumericConversionExpression conversion:
                    return TryFormatExpression(conversion.Operand, out display);
                default:
                    display = string.Empty;
                    return false;
            }
        }

        private static string ParenthesizeIfBinary(BoundExpression expression, string display)
            => expression is BoundBinaryExpression ? "(" + display + ")" : display;

        private static string FormatLiteral(object? value)
            => value switch
            {
                null => "null",
                bool boolean => boolean ? "true" : "false",
                string text => "\"" + text.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty,
            };

        private static bool TryGetOperator(SyntaxKind kind, out string value)
        {
            value = kind switch
            {
                SyntaxKind.BangToken => "!",
                SyntaxKind.PlusToken => "+",
                SyntaxKind.MinusToken => "-",
                SyntaxKind.StarToken => "*",
                SyntaxKind.SlashToken => "/",
                SyntaxKind.PercentToken => "%",
                SyntaxKind.LessToken => "<",
                SyntaxKind.LessOrEqualsToken => "<=",
                SyntaxKind.GreaterToken => ">",
                SyntaxKind.GreaterOrEqualsToken => ">=",
                SyntaxKind.EqualsEqualsToken or SyntaxKind.EqualsEqualsEqualsToken => "==",
                SyntaxKind.BangEqualsToken or SyntaxKind.BangEqualsEqualsToken => "!=",
                SyntaxKind.AmpersandAmpersandToken => "&&",
                SyntaxKind.PipePipeToken => "||",
                _ => string.Empty,
            };
            return value.Length > 0;
        }

        private static string TruncateUtf8(string value, int maximumBytes)
        {
            if (Encoding.UTF8.GetByteCount(value) <= maximumBytes)
            {
                return value;
            }
            const string suffix = "...";
            int budget = maximumBytes - Encoding.UTF8.GetByteCount(suffix);
            var builder = new StringBuilder();
            foreach (Rune rune in value.EnumerateRunes())
            {
                if (Encoding.UTF8.GetByteCount(builder.ToString()) + rune.Utf8SequenceLength > budget)
                {
                    break;
                }
                builder.Append(rune.ToString());
            }
            return builder.Append(suffix).ToString();
        }
    }
}
