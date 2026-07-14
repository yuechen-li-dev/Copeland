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
            ValidateFunctionPropagationTargets(function.Body, function.ReturnType, diagnostics);
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

    private static void ValidateFunctionPropagationTargets(
        IReadOnlyList<MirStatement> statements,
        MirType functionReturnType,
        List<MirValidationDiagnostic> diagnostics)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case MirVariableDeclarationStatement declaration:
                    ValidateFunctionPropagationTarget(declaration.Initializer, functionReturnType, diagnostics);
                    break;
                case MirExpressionStatement expression:
                    ValidateFunctionPropagationTarget(expression.Expression, functionReturnType, diagnostics);
                    break;
                case MirReturnStatement { Expression: not null } returnStatement:
                    ValidateFunctionPropagationTarget(returnStatement.Expression, functionReturnType, diagnostics);
                    break;
                case MirIfStatement conditional:
                    ValidateFunctionPropagationTarget(conditional.Condition, functionReturnType, diagnostics);
                    ValidateFunctionPropagationTargets(conditional.ThenStatements, functionReturnType, diagnostics);
                    if (conditional.ElseStatements is not null)
                    {
                        ValidateFunctionPropagationTargets(conditional.ElseStatements, functionReturnType, diagnostics);
                    }
                    break;
            }
        }
    }

    private static void ValidateFunctionPropagationTarget(
        MirExpression expression,
        MirType functionReturnType,
        List<MirValidationDiagnostic> diagnostics)
    {
        switch (expression)
        {
            case MirPropagateExpression propagation:
                if (propagation.Target is MirPropagationTarget.FunctionReturn)
                {
                    if (functionReturnType is not MirResultType functionResult)
                    {
                        diagnostics.Add(new MirValidationDiagnostic("Function-return propagation requires a Result function return type."));
                    }
                    else if (propagation.Operand.Type is MirResultType operandResult
                        && !MirTypeFacts.AreEquivalent(functionResult.ErrorType, operandResult.ErrorType))
                    {
                        diagnostics.Add(new MirValidationDiagnostic(
                            $"Function-return propagation error type '{operandResult.ErrorType.Name}' does not match function Result error type '{functionResult.ErrorType.Name}'."));
                    }
                }

                ValidateFunctionPropagationTarget(propagation.Operand, functionReturnType, diagnostics);
                return;
            case MirTryExpression tryExpression:
                ValidateValueBlockFunctionPropagationTargets(tryExpression.Protected, functionReturnType, diagnostics);
                ValidateValueBlockFunctionPropagationTargets(tryExpression.Handler, functionReturnType, diagnostics);
                return;
            case MirAssignmentExpression assignment:
                ValidateFunctionPropagationTarget(assignment.Expression, functionReturnType, diagnostics);
                return;
            case MirUnaryExpression unary:
                ValidateFunctionPropagationTarget(unary.Operand, functionReturnType, diagnostics);
                return;
            case MirBinaryExpression binary:
                ValidateFunctionPropagationTarget(binary.Left, functionReturnType, diagnostics);
                ValidateFunctionPropagationTarget(binary.Right, functionReturnType, diagnostics);
                return;
            case MirCallExpression call:
                foreach (var argument in call.Arguments)
                {
                    ValidateFunctionPropagationTarget(argument, functionReturnType, diagnostics);
                }
                return;
            case MirArrayExpression array:
                foreach (var element in array.Elements)
                {
                    ValidateFunctionPropagationTarget(element, functionReturnType, diagnostics);
                }
                return;
            case MirEnumValueExpression value:
                foreach (var argument in value.Arguments)
                {
                    ValidateFunctionPropagationTarget(argument, functionReturnType, diagnostics);
                }
                return;
            case MirMatchExpression match:
                ValidateFunctionPropagationTarget(match.Scrutinee, functionReturnType, diagnostics);
                foreach (var arm in match.Arms)
                {
                    ValidateFunctionPropagationTarget(arm.Expression, functionReturnType, diagnostics);
                }
                return;
            case MirResultMatchExpression resultMatch:
                ValidateFunctionPropagationTarget(resultMatch.Scrutinee, functionReturnType, diagnostics);
                ValidateFunctionPropagationTarget(resultMatch.OkExpression, functionReturnType, diagnostics);
                ValidateFunctionPropagationTarget(resultMatch.ErrExpression, functionReturnType, diagnostics);
                return;
            case MirIfExpression conditional:
                ValidateFunctionPropagationTarget(conditional.Condition, functionReturnType, diagnostics);
                ValidateFunctionPropagationTarget(conditional.ThenExpression, functionReturnType, diagnostics);
                ValidateFunctionPropagationTarget(conditional.ElseExpression, functionReturnType, diagnostics);
                return;
            case MirOkExpression ok:
                ValidateFunctionPropagationTarget(ok.Payload, functionReturnType, diagnostics);
                return;
            case MirErrExpression err:
                ValidateFunctionPropagationTarget(err.Payload, functionReturnType, diagnostics);
                return;
            case MirUnwrapExpression unwrap:
                ValidateFunctionPropagationTarget(unwrap.Operand, functionReturnType, diagnostics);
                return;
        }
    }

    private static void ValidateValueBlockFunctionPropagationTargets(
        MirValueBlock block,
        MirType functionReturnType,
        List<MirValidationDiagnostic> diagnostics)
    {
        ValidateFunctionPropagationTargets(block.PrefixStatements, functionReturnType, diagnostics);
        ValidateFunctionPropagationTarget(block.ValueExpression, functionReturnType, diagnostics);
    }

    private sealed class HandlerScope(MirHandlerId handlerId, MirType errorType)
    {
        public MirHandlerId HandlerId { get; } = handlerId;
        public MirType ErrorType { get; } = errorType;
        public bool WasTargeted { get; set; }
    }
}
