namespace Copeland.TS.Mir;

public sealed record MirValidationDiagnostic(string Message);

public static class MirValidator
{
    public static IReadOnlyList<MirValidationDiagnostic> Validate(MirProgram program)
    {
        var diagnostics = new List<MirValidationDiagnostic>();
        foreach (var function in program.Functions)
        {
            var handlerIds = new HashSet<MirHandlerId>();
            ValidateStatements(function.Body, [], handlerIds, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidateStatements(IReadOnlyList<MirStatement> statements, List<HandlerScope> activeHandlers, HashSet<MirHandlerId> handlerIds, List<MirValidationDiagnostic> diagnostics)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case MirVariableDeclarationStatement declaration:
                    ValidateExpression(declaration.Initializer, activeHandlers, handlerIds, diagnostics);
                    break;
                case MirExpressionStatement expression:
                    ValidateExpression(expression.Expression, activeHandlers, handlerIds, diagnostics);
                    break;
                case MirReturnStatement { Expression: not null } returnStatement:
                    ValidateExpression(returnStatement.Expression, activeHandlers, handlerIds, diagnostics);
                    break;
                case MirIfStatement conditional:
                    ValidateExpression(conditional.Condition, activeHandlers, handlerIds, diagnostics);
                    ValidateStatements(conditional.ThenStatements, activeHandlers, handlerIds, diagnostics);
                    if (conditional.ElseStatements is not null)
                    {
                        ValidateStatements(conditional.ElseStatements, activeHandlers, handlerIds, diagnostics);
                    }
                    break;
            }
        }
    }

    private static void ValidateExpression(MirExpression expression, List<HandlerScope> activeHandlers, HashSet<MirHandlerId> handlerIds, List<MirValidationDiagnostic> diagnostics)
    {
        switch (expression)
        {
            case MirTryExpression tryExpression:
                ValidateTryExpression(tryExpression, activeHandlers, handlerIds, diagnostics);
                return;
            case MirPropagateExpression propagation:
                ValidatePropagation(propagation, activeHandlers, diagnostics);
                ValidateExpression(propagation.Operand, activeHandlers, handlerIds, diagnostics);
                return;
            case MirAssignmentExpression assignment:
                ValidateExpression(assignment.Expression, activeHandlers, handlerIds, diagnostics);
                return;
            case MirUnaryExpression unary:
                ValidateExpression(unary.Operand, activeHandlers, handlerIds, diagnostics);
                return;
            case MirBinaryExpression binary:
                ValidateExpression(binary.Left, activeHandlers, handlerIds, diagnostics);
                ValidateExpression(binary.Right, activeHandlers, handlerIds, diagnostics);
                return;
            case MirCallExpression call:
                foreach (var argument in call.Arguments) ValidateExpression(argument, activeHandlers, handlerIds, diagnostics);
                return;
            case MirArrayExpression array:
                foreach (var element in array.Elements) ValidateExpression(element, activeHandlers, handlerIds, diagnostics);
                return;
            case MirEnumValueExpression value:
                foreach (var argument in value.Arguments) ValidateExpression(argument, activeHandlers, handlerIds, diagnostics);
                return;
            case MirMatchExpression match:
                ValidateExpression(match.Scrutinee, activeHandlers, handlerIds, diagnostics);
                foreach (var arm in match.Arms) ValidateExpression(arm.Expression, activeHandlers, handlerIds, diagnostics);
                return;
            case MirResultMatchExpression resultMatch:
                ValidateExpression(resultMatch.Scrutinee, activeHandlers, handlerIds, diagnostics);
                ValidateExpression(resultMatch.OkExpression, activeHandlers, handlerIds, diagnostics);
                ValidateExpression(resultMatch.ErrExpression, activeHandlers, handlerIds, diagnostics);
                return;
            case MirIfExpression conditional:
                ValidateExpression(conditional.Condition, activeHandlers, handlerIds, diagnostics);
                ValidateExpression(conditional.ThenExpression, activeHandlers, handlerIds, diagnostics);
                ValidateExpression(conditional.ElseExpression, activeHandlers, handlerIds, diagnostics);
                return;
            case MirOkExpression ok:
                ValidateExpression(ok.Payload, activeHandlers, handlerIds, diagnostics);
                return;
            case MirErrExpression err:
                ValidateExpression(err.Payload, activeHandlers, handlerIds, diagnostics);
                return;
            case MirUnwrapExpression unwrap:
                ValidateExpression(unwrap.Operand, activeHandlers, handlerIds, diagnostics);
                return;
        }
    }

    private static void ValidateTryExpression(MirTryExpression tryExpression, List<HandlerScope> activeHandlers, HashSet<MirHandlerId> handlerIds, List<MirValidationDiagnostic> diagnostics)
    {
        if (!handlerIds.Add(tryExpression.HandlerId))
        {
            diagnostics.Add(new MirValidationDiagnostic($"Duplicate try handler identity '{tryExpression.HandlerId}' in one function."));
        }

        var scope = new HandlerScope(tryExpression.HandlerId, tryExpression.HandledErrorType);
        activeHandlers.Add(scope);
        ValidateValueBlock(tryExpression.Protected, activeHandlers, handlerIds, diagnostics);
        activeHandlers.RemoveAt(activeHandlers.Count - 1);

        if (!scope.WasTargeted)
        {
            diagnostics.Add(new MirValidationDiagnostic($"Try handler '{tryExpression.HandlerId}' has no targeted propagation in its protected value block."));
        }

        ValidateValueBlock(tryExpression.Handler, activeHandlers, handlerIds, diagnostics);
    }

    private static void ValidateValueBlock(MirValueBlock block, List<HandlerScope> activeHandlers, HashSet<MirHandlerId> handlerIds, List<MirValidationDiagnostic> diagnostics)
    {
        foreach (var statement in block.PrefixStatements)
        {
            if (statement is not MirVariableDeclarationStatement and not MirExpressionStatement)
            {
                diagnostics.Add(new MirValidationDiagnostic("Try value blocks may contain only variable declarations and expression statements before their final value."));
            }

            ValidateStatements([statement], activeHandlers, handlerIds, diagnostics);
        }

        ValidateExpression(block.ValueExpression, activeHandlers, handlerIds, diagnostics);
    }

    private static void ValidatePropagation(MirPropagateExpression propagation, List<HandlerScope> activeHandlers, List<MirValidationDiagnostic> diagnostics)
    {
        if (propagation.Operand.Type is not MirResultType resultType)
        {
            diagnostics.Add(new MirValidationDiagnostic("Propagation operand must be a Result."));
            return;
        }

        if (propagation.Target is not MirPropagationTarget.LexicalExcept lexical)
        {
            return;
        }

        var scope = activeHandlers.LastOrDefault(handler => handler.HandlerId == lexical.HandlerId);
        if (scope is null)
        {
            diagnostics.Add(new MirValidationDiagnostic($"Lexical propagation target '{lexical.HandlerId}' is dangling, out of scope, or targets its own handler body."));
            return;
        }

        if (!MirTypeFacts.AreEquivalent(scope.ErrorType, resultType.ErrorType))
        {
            diagnostics.Add(new MirValidationDiagnostic($"Lexical propagation target '{lexical.HandlerId}' has incompatible error type '{resultType.ErrorType.Name}'."));
            return;
        }

        scope.WasTargeted = true;
    }

    private sealed class HandlerScope(MirHandlerId handlerId, MirType errorType)
    {
        public MirHandlerId HandlerId { get; } = handlerId;
        public MirType ErrorType { get; } = errorType;
        public bool WasTargeted { get; set; }
    }
}
