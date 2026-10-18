namespace Copeland.TS.Mir;

/// <summary>Validates explicit module storage and rejects reads before its authored initialization.</summary>
public static class MirModuleValueValidator
{
    public static IReadOnlyList<MirValidationDiagnostic> Validate(MirProgram program)
    {
        var diagnostics = new List<MirValidationDiagnostic>();
        if (program.ModuleValues.Count == 0) return diagnostics;
        var functions = program.Functions.GroupBy(function => function.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var storage = new Dictionary<string, MirModuleValue>(StringComparer.Ordinal);
        foreach (MirModuleValue value in program.ModuleValues)
        {
            if (string.IsNullOrWhiteSpace(value.Name) || !storage.TryAdd(value.Name, value) || functions.ContainsKey(value.Name))
            {
                diagnostics.Add(new($"Module value '{value.Name}' has a blank or conflicting storage identity."));
            }
            if (!functions.TryGetValue(value.InitializerFunction, out MirFunction? initializer)
                || initializer.Parameters.Count != 0 || initializer.IsAsync || initializer.IsGenerator || initializer.IsRemote
                || !MirTypeFacts.AreEquivalent(initializer.ReturnType, value.Type) || value.Type.Identifier == "void")
            {
                diagnostics.Add(new($"Module value '{value.Name}' requires a synchronous nullary initializer with its exact value type."));
            }
        }
        if (diagnostics.Count > 0) return diagnostics;

        foreach (MirFunction function in program.Functions)
        {
            foreach (MirAssignmentExpression write in function.Body.SelectMany(Expressions).OfType<MirAssignmentExpression>())
            {
                if (IsModuleReference(function, write.Name))
                {
                    diagnostics.Add(new($"Immutable module value '{write.Name}' cannot be assigned."));
                }
            }
        }
        if (diagnostics.Count > 0) return diagnostics;

        var initialized = new HashSet<string>(StringComparer.Ordinal);
        foreach (MirModuleValue value in program.ModuleValues)
        {
            var pending = new Stack<MirFunction>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            pending.Push(functions[value.InitializerFunction]);
            while (pending.TryPop(out MirFunction? function))
            {
                if (!visited.Add(function.Name)) continue;
                foreach (MirExpression expression in function.Body.SelectMany(Expressions))
                {
                    if (expression is MirVariableExpression read && IsModuleReference(function, read.Name) && !initialized.Contains(read.Name))
                    {
                        diagnostics.Add(new($"Module value '{value.Name}' reads '{read.Name}' before initialization through '{function.Name}'. Declare the dependency first or construct the stream locally."));
                    }
                    string? callee = expression switch
                    {
                        MirCallExpression call => call.FunctionName,
                        MirInvokeExpression { Callee: MirFunctionReferenceExpression reference } => reference.FunctionName,
                        MirInvokeExpression { Callee: MirCallableConstructionExpression construction } => construction.CodeFunctionName,
                        _ => null,
                    };
                    if (callee is not null && functions.TryGetValue(callee, out MirFunction? target)) pending.Push(target);
                    if (expression is MirInvokeExpression { Callee: not MirFunctionReferenceExpression and not MirCallableConstructionExpression })
                    {
                        diagnostics.Add(new($"Module value '{value.Name}' reaches an indirect invocation whose initialization dependencies are unknown. Construct the stream locally or invoke a named function directly."));
                    }
                }
            }
            initialized.Add(value.Name);
        }
        return diagnostics;

        bool IsModuleReference(MirFunction function, string name)
            => storage.ContainsKey(name)
                && !function.Parameters.Any(parameter => parameter.Name == name)
                && !function.Locals.Any(local => local.Name == name);
    }

    private static IEnumerable<MirExpression> Expressions(MirStatement statement)
    {
        IEnumerable<MirExpression> roots = statement switch
        {
            MirVariableDeclarationStatement value => [value.Initializer],
            MirResourceUsingDeclarationStatement value => [value.Initializer],
            MirExpressionStatement value => [value.Expression],
            MirReturnStatement { Expression: not null } value => [value.Expression],
            MirYieldStatement { Expression: not null } value => [value.Expression],
            MirIfStatement value => new[] { value.Condition }.Concat(value.ThenStatements.SelectMany(Expressions)).Concat(value.ElseStatements?.SelectMany(Expressions) ?? []),
            MirWhileStatement value => new[] { value.Condition }.Concat(value.BodyStatements.SelectMany(Expressions)),
            MirForStatement value => (value.Initializer is null ? [] : Expressions(value.Initializer))
                .Concat(value.Condition is null ? [] : [value.Condition]).Concat(value.Increment is null ? [] : [value.Increment]).Concat(value.BodyStatements.SelectMany(Expressions)),
            MirForOfStatement value => new[] { value.Iterable }.Concat(value.BodyStatements.SelectMany(Expressions)),
            _ => [],
        };
        return roots.SelectMany(Expressions);
    }

    private static IEnumerable<MirExpression> Expressions(MirExpression expression)
    {
        yield return expression;
        IEnumerable<MirExpression> children = expression switch
        {
            MirAssignmentExpression value => [value.Expression],
            MirUnaryExpression value => [value.Operand],
            MirAwaitExpression value => [value.Operand],
            MirNumericConversionExpression value => [value.Operand],
            MirBinaryExpression value => [value.Left, value.Right],
            MirCallExpression value => value.Arguments,
            MirCallableConstructionExpression value => value.Captures,
            MirInvokeExpression value => value.Arguments.Prepend(value.Callee),
            MirArrayExpression value => value.Elements,
            MirArrayLengthExpression value => [value.Receiver],
            MirArrayElementAccessExpression value => [value.Receiver, value.Index],
            MirArrayIterableExpression value => [value.Receiver],
            MirMutableArrayConstructionExpression value => [value.Length],
            MirMutableArrayLengthExpression value => [value.Receiver],
            MirMutableArrayElementAccessExpression value => [value.Receiver, value.Index],
            MirMutableArrayElementAssignmentExpression value => [value.Receiver, value.Index, value.Value],
            MirMutableArrayIterableExpression value => [value.Receiver],
            MirMutableArrayFreezeExpression value => [value.Receiver],
            MirBatchExpression value => value.Body.PrefixStatements.SelectMany(Expressions).Append(value.Input).Append(value.Body.ValueExpression),
            MirRecordConstructionExpression value => value.Initializers.Select(field => field.Value),
            MirRecordFieldAccessExpression value => [value.Receiver],
            MirRecordWithExpression value => value.Replacements.Select(field => field.Value).Prepend(value.Source),
            MirTableChangesExpression value => value.Children,
            MirTableWithRowExpression value => value.Children,
            MirTableWithExpression value => value.Replacements.Select(field => (MirExpression)field.Value).Prepend(value.Source),
            MirTableColumnAccessExpression value => [value.Receiver],
            MirTableRowAccessExpression value => [value.Receiver, value.Index],
            MirColumnElementAccessExpression value => [value.Receiver, value.Index],
            MirTableRowFieldAccessExpression value => [value.Receiver],
            MirTableRowsExpression value => [value.Table],
            MirTableWhereExpression value => value.Predicates.Prepend(value.Source),
            MirTableSelectExpression value => [value.Source, value.Projector],
            MirTableSummaryExpression value => [value.Source],
            MirTableRecalculationExpression value => value.Inputs.Select(input => input.Value),
            MirTableAggregateExpression value => [value.Receiver],
            MirEnumValueExpression value => value.Arguments,
            MirMatchExpression value => value.Arms.Select(arm => arm.Expression).Prepend(value.Scrutinee),
            MirResultMatchExpression value => [value.Scrutinee, value.OkExpression, value.ErrExpression],
            MirIfExpression value => [value.Condition, value.ThenExpression, value.ElseExpression],
            MirOkExpression value => [value.Payload],
            MirErrExpression value => [value.Payload],
            MirPropagateExpression value => [value.Operand],
            MirUnwrapExpression value => [value.Operand],
            MirTryExpression value => value.Protected.PrefixStatements.SelectMany(Expressions).Concat(value.Handler.PrefixStatements.SelectMany(Expressions))
                .Append(value.Protected.ValueExpression).Append(value.Handler.ValueExpression),
            MirTsonEncodeExpression value => [value.Operand],
            MirTsonTransportExpression value => [value.Operation, value.Request],
            MirNpmCallExpression value => value.Arguments.Append(value.ArgumentTuple),
            MirNpmDirectCallExpression value => value.Arguments,
            MirNpmComponentExpression => [],
            MirJavaScriptHostCallExpression value => value.Arguments,
            MirReactElementExpression value => value.Properties.Select(property => property.Value).Concat(value.Children).Prepend(value.ElementType),
            MirTextDocumentExpression value => value.Slots.Select(slot => slot.Expression),
            MirReactRootRenderExpression value => [value.Root, value.Node],
            MirReactRootUnmountExpression value => [value.Root],
            MirClrInvocationExpression value => value.Arguments.Concat(value.Receiver is null ? [] : [value.Receiver]),
            MirClrPropertyAccessExpression value => value.Receiver is null ? [] : [value.Receiver],
            _ => [],
        };
        foreach (MirExpression child in children)
        {
            foreach (MirExpression nested in Expressions(child)) yield return nested;
        }
    }
}
