using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Semantics;

public static partial class Binder
{
    private sealed partial class BinderImpl
    {
        private void BindExecutionFlow(FlowDeclarationSyntax declaration)
        {
            string name = declaration.Identifier.Text;
            if (!_recordTypes.TryGetValue(FlowExecutionProjection.Name(name, "Board"), out var board)
                || !_recordTypes.TryGetValue(FlowExecutionProjection.Name(name, "Frame"), out var frame))
            {
                return;
            }
            TypeSymbol result = declaration.ResultType is null
                ? PrimitiveTypeSymbol.Void
                : BindType(declaration.ResultType, declaration.Identifier, "COPE-FLOW-0027", "flow result");
            TypeSymbol? failure = (result as ResultTypeSymbol)?.ErrorType;
            if (result is ResultTypeSymbol fallible) result = fallible.SuccessType;
            var parameters = declaration.Parameters.Select(parameter => new ParameterSymbol(
                parameter.Identifier.Text,
                BindType(parameter.Type, parameter.Identifier, "COPE-FLOW-EXEC-0010", "flow parameter"))).ToArray();
            var states = declaration.States.Select(state => new BoundFlowState(
                state.Identifier.Text,
                "flow:" + name + ".state:" + state.Identifier.Text,
                state.InitialKeyword is not null,
                [],
                null,
                CreateFlowSourceCorrelation(state.StateKeyword, state.CloseBraceToken))).ToArray();
            var functions = _functions.Where(function => function.Symbol.Name.StartsWith("__flow_" + name + "_", StringComparison.Ordinal)).ToArray();
            string FunctionIdentity(string functionName) => functions.FirstOrDefault(function => function.Symbol.Name == functionName)?.Symbol.EmissionName ?? functionName;
            var executionStates = declaration.States.Select(state => new BoundFlowExecutionState(
                state.Identifier.Text,
                FunctionIdentity(functions.Any(function => function.Symbol.Name == FlowExecutionProjection.Name(name, "State_" + state.Identifier.Text))
                    ? FlowExecutionProjection.Name(name, "State_" + state.Identifier.Text)
                    : FlowExecutionProjection.Name(name, "Activation_" + name + "_State_" + state.Identifier.Text)),
                state.Parameters.Select(parameter => new ParameterSymbol(parameter.Identifier.Text,
                    BindType(parameter.Type, parameter.Identifier, "COPE-FLOW-EXEC-0010", "state-entry parameter"))).ToArray(),
                ExecutionControls(FlowExecutionProjection.StateBody(state)).ToArray())).ToArray();
            _flows.Add(new BoundFlowDefinition(name, "flow:" + name, board, [], [], states,
                states.FirstOrDefault(state => state.IsInitial)?.Name ?? "<missing>", result, failure,
                CreateFlowSourceCorrelation(declaration.FlowKeyword, declaration.CloseBraceToken))
            {
                Execution = new BoundFlowExecutionPlan(frame,
                    FunctionIdentity(FlowExecutionProjection.Name(name, "Start")),
                    FunctionIdentity(FlowExecutionProjection.Name(name, "Step")), parameters,
                    functions, executionStates),
            });
        }

        private IEnumerable<BoundFlowExecutionControl> ExecutionControls(StatementSyntax statement)
        {
            if (statement is FlowControlStatementSyntax control)
            {
                yield return new(control.Keyword.Text, control.SuccessTarget?.Text, control.FailureTarget?.Text,
                    control.Value is CallExpressionSyntax { Target: NameExpressionSyntax child } && control.Keyword.Text == "push"
                        ? child.IdentifierToken.Text : null,
                    CreateFlowSourceCorrelation(control.Keyword, control.SemicolonToken));
            }
            else if (statement is YieldStatementSyntax yielded)
            {
                yield return new("yield", null, null, null, CreateFlowSourceCorrelation(yielded.YieldKeyword, yielded.SemicolonToken));
            }
            IEnumerable<StatementSyntax> children = statement switch
            {
                BlockStatementSyntax block => block.Statements,
                IfStatementSyntax conditional => conditional.ElseStatement is null
                    ? [conditional.ThenStatement] : [conditional.ThenStatement, conditional.ElseStatement],
                WhileStatementSyntax loop => [loop.Body],
                ForStatementSyntax loop => [loop.Body],
                ForOfStatementSyntax loop => [loop.Body],
                _ => [],
            };
            foreach (StatementSyntax child in children)
            {
                foreach (BoundFlowExecutionControl nested in ExecutionControls(child)) yield return nested;
            }
        }
    }
}
