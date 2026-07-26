namespace Copeland.TS.Mir;

public sealed record MirValidationDiagnostic(string Message);

public static class MirValidator
{
    public static IReadOnlyList<MirValidationDiagnostic> Validate(MirProgram program)
    {
        var diagnostics = new List<MirValidationDiagnostic>();
        ValidateCallableModel(program, diagnostics);
        if (diagnostics.Count > 0)
        {
            return diagnostics;
        }
        ValidateArrayModel(program, diagnostics);
        if (diagnostics.Count > 0)
        {
            return diagnostics;
        }

        ValidateRecordModel(program, diagnostics);
        ValidateEnumModel(program, diagnostics);
        ValidateTableModel(program, diagnostics);
        ValidateNpmModel(program, diagnostics);
        ValidateTsonEncodingModel(program, diagnostics);
        foreach (var function in program.Functions)
        {
            var handlerIds = new HashSet<MirHandlerId>();
            ValidateStatements(function.Body, [], handlerIds, diagnostics, loopDepth: 0);
            ValidateFunctionPropagationTargets(function.Body, function.ReturnType, diagnostics);
        }

        MirSuspensionAutomatonValidator.Validate(program, diagnostics);

        return diagnostics;
    }

    private static void ValidateNpmModel(MirProgram program, List<MirValidationDiagnostic> diagnostics)
    {
        var imports = new Dictionary<string, MirNpmImport>(StringComparer.Ordinal);
        foreach (MirNpmImport import in program.NpmImports)
        {
            if (string.IsNullOrWhiteSpace(import.PackageName)
                || string.IsNullOrWhiteSpace(import.PackageVersion)
                || string.IsNullOrWhiteSpace(import.ExportName)
                || string.IsNullOrWhiteSpace(import.LocalBinding)
                || !imports.TryAdd(import.LocalBinding, import))
            {
                diagnostics.Add(new MirValidationDiagnostic("npm module metadata has a blank or duplicate local binding."));
            }
        }

        foreach (MirFunction function in program.Functions)
        {
            foreach (MirStatement statement in function.Body)
            {
                ValidateNpmCalls(statement, imports, diagnostics);
            }
        }
    }

    private static void ValidateNpmCalls(MirStatement statement, IReadOnlyDictionary<string, MirNpmImport> imports, List<MirValidationDiagnostic> diagnostics)
    {
        IEnumerable<MirExpression> expressions = statement switch
        {
            MirVariableDeclarationStatement declaration => [declaration.Initializer],
            MirExpressionStatement expression => [expression.Expression],
            MirReturnStatement { Expression: not null } returned => [returned.Expression],
            MirIfStatement conditional => new[] { conditional.Condition }.Concat(conditional.ThenStatements.SelectMany(EnumerateStatementExpressions)).Concat(conditional.ElseStatements?.SelectMany(EnumerateStatementExpressions) ?? Enumerable.Empty<MirExpression>()),
            _ => Enumerable.Empty<MirExpression>(),
        };
        foreach (MirExpression expression in expressions)
        {
            ValidateNpmCalls(expression, imports, diagnostics);
        }
    }

    private static IEnumerable<MirExpression> EnumerateStatementExpressions(MirStatement statement)
        => statement switch
        {
            MirVariableDeclarationStatement declaration => [declaration.Initializer],
            MirExpressionStatement expression => [expression.Expression],
            MirReturnStatement { Expression: not null } returned => [returned.Expression],
            MirIfStatement conditional => new[] { conditional.Condition }.Concat(conditional.ThenStatements.SelectMany(EnumerateStatementExpressions)).Concat(conditional.ElseStatements?.SelectMany(EnumerateStatementExpressions) ?? Enumerable.Empty<MirExpression>()),
            _ => Enumerable.Empty<MirExpression>(),
        };

    private static void ValidateNpmCalls(MirExpression expression, IReadOnlyDictionary<string, MirNpmImport> imports, List<MirValidationDiagnostic> diagnostics)
    {
        if (expression is MirNpmCallExpression npm)
        {
            if (!imports.TryGetValue(npm.LocalBinding, out MirNpmImport? import)
                || import.PackageName != npm.PackageName
                || import.PackageVersion != npm.PackageVersion
                || import.ExportName != npm.ExportName)
            {
                diagnostics.Add(new MirValidationDiagnostic($"npm call '{npm.LocalBinding}' does not resolve to module import metadata."));
            }
        }
        foreach (MirExpression child in EnumerateTsonExpressionChildren(expression))
        {
            ValidateNpmCalls(child, imports, diagnostics);
        }
    }

    private static void ValidateCallableModel(MirProgram program, List<MirValidationDiagnostic> diagnostics)
    {
        var functions = new Dictionary<string, MirFunction>(StringComparer.Ordinal);
        foreach (var function in program.Functions)
        {
            if (string.IsNullOrWhiteSpace(function.Name) || !functions.TryAdd(function.Name, function))
            {
                diagnostics.Add(new MirValidationDiagnostic($"Duplicate or blank function identity '{function.Name}'."));
            }
        }

        foreach (var record in program.Records)
        {
            foreach (var field in record.Fields)
            {
                ValidateCallableBoundaryType(field.Type, $"record field '{record.Name}.{field.Name}'", diagnostics, allowDirectCallable: true);
            }
        }
        foreach (var @enum in program.Enums)
        {
            foreach (var @case in @enum.Cases)
            {
                foreach (var payload in @case.PayloadFields) ValidateCallableBoundaryType(payload.Type, $"enum payload '{@enum.Name}.{@case.Name}.{payload.Name}'", diagnostics, allowDirectCallable: true);
            }
        }
        foreach (var table in program.Tables)
        {
            foreach (var column in table.Columns) ValidateNoCallableContainer(column.ElementType, $"table column '{table.Name}.{column.Name}'", diagnostics);
        }

        foreach (var function in program.Functions)
        {
            ValidateCallableBoundaryType(function.ReturnType, $"return type of function '{function.Name}'", diagnostics, allowDirectCallable: true);
            foreach (var parameter in function.Parameters) ValidateCallableBoundaryType(parameter.Type, $"parameter '{parameter.Name}' of function '{function.Name}'", diagnostics, allowDirectCallable: true);
            foreach (var local in function.Locals) ValidateCallableBoundaryType(local.Type, $"local '{local.Name}' of function '{function.Name}'", diagnostics, allowDirectCallable: true);
            ValidateCallableStatements(function.Body, functions, diagnostics);
        }
    }

    private static void ValidateCallableStatements(IReadOnlyList<MirStatement> statements, IReadOnlyDictionary<string, MirFunction> functions, List<MirValidationDiagnostic> diagnostics)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case MirVariableDeclarationStatement declaration:
                    ValidateCallableExpression(declaration.Initializer, functions, diagnostics);
                    break;
                case MirExpressionStatement expression:
                    ValidateCallableExpression(expression.Expression, functions, diagnostics);
                    break;
                case MirReturnStatement { Expression: not null } returned:
                    ValidateCallableExpression(returned.Expression, functions, diagnostics);
                    break;
                case MirIfStatement conditional:
                    ValidateCallableExpression(conditional.Condition, functions, diagnostics);
                    ValidateCallableStatements(conditional.ThenStatements, functions, diagnostics);
                    if (conditional.ElseStatements is not null) ValidateCallableStatements(conditional.ElseStatements, functions, diagnostics);
                    break;
                case MirWhileStatement loop:
                    ValidateCallableExpression(loop.Condition, functions, diagnostics);
                    ValidateCallableStatements(loop.BodyStatements, functions, diagnostics);
                    break;
                case MirForStatement loop:
                    if (loop.Initializer is not null) ValidateCallableStatements([loop.Initializer], functions, diagnostics);
                    if (loop.Condition is not null) ValidateCallableExpression(loop.Condition, functions, diagnostics);
                    if (loop.Increment is not null) ValidateCallableExpression(loop.Increment, functions, diagnostics);
                    ValidateCallableStatements(loop.BodyStatements, functions, diagnostics);
                    break;
            }
        }
    }

    private static void ValidateCallableExpression(MirExpression expression, IReadOnlyDictionary<string, MirFunction> functions, List<MirValidationDiagnostic> diagnostics)
    {
        switch (expression)
        {
            case MirFunctionReferenceExpression reference:
                ValidateCallableType(reference.CallableType, diagnostics);
                if (!functions.TryGetValue(reference.FunctionName, out var target))
                {
                    diagnostics.Add(new MirValidationDiagnostic($"Callable reference targets unknown function '{reference.FunctionName}'."));
                }
                else
                {
                    var expected = new MirCallableType(target.Parameters.Select(parameter => new MirCallableParameter(parameter.Name, parameter.Type)).ToArray(), target.ReturnType);
                    if (!MirTypeFacts.AreEquivalent(reference.CallableType, expected)) diagnostics.Add(new MirValidationDiagnostic($"Callable reference '{reference.FunctionName}' signature does not match the target function."));
                }
                return;
            case MirCallableConstructionExpression construction:
                ValidateCallableType(construction.CallableType, diagnostics);
                foreach (MirExpression capture in construction.Captures)
                {
                    ValidateCallableExpression(capture, functions, diagnostics);
                }
                if (!functions.TryGetValue(construction.CodeFunctionName, out MirFunction? code))
                {
                    diagnostics.Add(new MirValidationDiagnostic($"Callable construction targets unknown code function '{construction.CodeFunctionName}'."));
                }
                else
                {
                    if (construction.Captures.Count > code.Parameters.Count)
                    {
                        diagnostics.Add(new MirValidationDiagnostic($"Callable construction '{construction.CodeFunctionName}' has more environment values than code parameters."));
                    }
                    else
                    {
                        var expected = new MirCallableType(code.Parameters.Skip(construction.Captures.Count)
                            .Select(parameter => new MirCallableParameter(parameter.Name, parameter.Type)).ToArray(), code.ReturnType);
                        if (!MirTypeFacts.AreEquivalent(construction.CallableType, expected))
                        {
                            diagnostics.Add(new MirValidationDiagnostic($"Callable construction '{construction.CodeFunctionName}' signature does not match the code function tail signature."));
                        }
                        for (int index = 0; index < construction.Captures.Count; index++)
                        {
                            if (!MirTypeFacts.AreEquivalent(construction.Captures[index].Type, code.Parameters[index].Type))
                            {
                                diagnostics.Add(new MirValidationDiagnostic($"Callable construction environment value {index + 1} does not match the code function environment slot."));
                            }
                        }
                    }
                }
                return;
            case MirInvokeExpression invoke:
                ValidateCallableExpression(invoke.Callee, functions, diagnostics);
                foreach (var argument in invoke.Arguments) ValidateCallableExpression(argument, functions, diagnostics);
                if (invoke.Callee.Type is not MirCallableType callable)
                {
                    diagnostics.Add(new MirValidationDiagnostic("Invoke has a non-callable callee."));
                }
                else
                {
                    if (invoke.Arguments.Count != callable.Parameters.Count) diagnostics.Add(new MirValidationDiagnostic("Invoke arity does not match the callable signature."));
                    if (!MirTypeFacts.AreEquivalent(invoke.Type, callable.ReturnType)) diagnostics.Add(new MirValidationDiagnostic("Invoke result type does not match the callable return type."));
                    for (var index = 0; index < Math.Min(invoke.Arguments.Count, callable.Parameters.Count); index++)
                    {
                        if (!MirTypeFacts.AreEquivalent(invoke.Arguments[index].Type, callable.Parameters[index].Type)) diagnostics.Add(new MirValidationDiagnostic($"Invoke argument {index + 1} does not match the callable parameter type."));
                    }
                }
                return;
            case MirAssignmentExpression assignment:
                ValidateCallableExpression(assignment.Expression, functions, diagnostics);
                return;
            case MirUnaryExpression unary:
                ValidateCallableExpression(unary.Operand, functions, diagnostics);
                return;
            case MirBinaryExpression binary:
                ValidateCallableExpression(binary.Left, functions, diagnostics);
                ValidateCallableExpression(binary.Right, functions, diagnostics);
                return;
            case MirCallExpression call:
                foreach (var argument in call.Arguments) ValidateCallableExpression(argument, functions, diagnostics);
                return;
            case MirArrayExpression array:
                foreach (var element in array.Elements) ValidateCallableExpression(element, functions, diagnostics);
                return;
            case MirRecordConstructionExpression construction:
                foreach (var initializer in construction.Initializers) ValidateCallableExpression(initializer.Value, functions, diagnostics);
                return;
            case MirRecordFieldAccessExpression access:
                ValidateCallableExpression(access.Receiver, functions, diagnostics);
                return;
            case MirRecordWithExpression update:
                ValidateCallableExpression(update.Source, functions, diagnostics);
                foreach (var replacement in update.Replacements) ValidateCallableExpression(replacement.Value, functions, diagnostics);
                return;
            case MirEnumValueExpression value:
                foreach (var argument in value.Arguments) ValidateCallableExpression(argument, functions, diagnostics);
                return;
            case MirMatchExpression match:
                ValidateCallableExpression(match.Scrutinee, functions, diagnostics);
                foreach (var arm in match.Arms) ValidateCallableExpression(arm.Expression, functions, diagnostics);
                return;
            case MirResultMatchExpression match:
                ValidateCallableExpression(match.Scrutinee, functions, diagnostics);
                ValidateCallableExpression(match.OkExpression, functions, diagnostics);
                ValidateCallableExpression(match.ErrExpression, functions, diagnostics);
                return;
            case MirIfExpression conditional:
                ValidateCallableExpression(conditional.Condition, functions, diagnostics);
                ValidateCallableExpression(conditional.ThenExpression, functions, diagnostics);
                ValidateCallableExpression(conditional.ElseExpression, functions, diagnostics);
                return;
            case MirOkExpression ok:
                ValidateCallableExpression(ok.Payload, functions, diagnostics);
                return;
            case MirErrExpression err:
                ValidateCallableExpression(err.Payload, functions, diagnostics);
                return;
            case MirPropagateExpression propagate:
                ValidateCallableExpression(propagate.Operand, functions, diagnostics);
                return;
            case MirUnwrapExpression unwrap:
                ValidateCallableExpression(unwrap.Operand, functions, diagnostics);
                return;
            case MirTryExpression attempt:
                ValidateCallableStatements(attempt.Protected.PrefixStatements, functions, diagnostics);
                ValidateCallableExpression(attempt.Protected.ValueExpression, functions, diagnostics);
                ValidateCallableStatements(attempt.Handler.PrefixStatements, functions, diagnostics);
                ValidateCallableExpression(attempt.Handler.ValueExpression, functions, diagnostics);
                return;
        }
    }

    private static void ValidateCallableBoundaryType(MirType type, string context, List<MirValidationDiagnostic> diagnostics, bool allowDirectCallable)
    {
        ValidateNestedCallableTypes(type, diagnostics);
    }

    private static void ValidateNestedCallableTypes(MirType root, List<MirValidationDiagnostic> diagnostics)
    {
        var pending = new Stack<MirType>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            MirType type = pending.Pop();
            switch (type)
            {
                case MirCallableType callable:
                    ValidateCallableType(callable, diagnostics);
                    foreach (MirCallableParameter parameter in callable.Parameters) pending.Push(parameter.Type);
                    pending.Push(callable.ReturnType);
                    break;
                case MirArrayType array:
                    pending.Push(array.ElementType);
                    break;
                case MirResultType result:
                    pending.Push(result.SuccessType);
                    pending.Push(result.ErrorType);
                    break;
            }
        }
    }

    private static void ValidateNoCallableContainer(MirType type, string context, List<MirValidationDiagnostic> diagnostics)
    {
        if (ContainsCallable(type)) diagnostics.Add(new MirValidationDiagnostic($"Callable type is not supported in {context}."));
    }

    private static bool ContainsCallable(MirType type) => type switch
    {
        MirCallableType => true,
        MirArrayType array => ContainsCallable(array.ElementType),
        MirResultType result => ContainsCallable(result.SuccessType) || ContainsCallable(result.ErrorType),
        MirColumnType column => ContainsCallable(column.ElementType),
        _ => false,
    };

    private static void ValidateCallableType(MirCallableType callable, List<MirValidationDiagnostic> diagnostics)
    {
        if (callable.Parameters.Count > 32) diagnostics.Add(new MirValidationDiagnostic("Callable type has more than 32 parameters."));
        var pending = new Stack<(MirType Type, int Depth)>();
        pending.Push((callable, 1));
        while (pending.Count > 0)
        {
            var (type, depth) = pending.Pop();
            if (type is not MirCallableType nested) continue;
            if (depth > 16)
            {
                diagnostics.Add(new MirValidationDiagnostic("Callable type nesting exceeds 16."));
                continue;
            }
            foreach (var parameter in nested.Parameters) pending.Push((parameter.Type, depth + 1));
            pending.Push((nested.ReturnType, depth + 1));
        }
    }

    private static void ValidateEnumModel(MirProgram program, List<MirValidationDiagnostic> diagnostics)
    {
        var enumsByName = new Dictionary<string, MirEnum>(StringComparer.Ordinal);
        foreach (MirEnum @enum in program.Enums)
        {
            if (string.IsNullOrWhiteSpace(@enum.Name) || !enumsByName.TryAdd(@enum.Name, @enum))
            {
                diagnostics.Add(new MirValidationDiagnostic($"Duplicate or blank enum name '{@enum.Name}'."));
            }

            var caseNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (MirEnumCase @case in @enum.Cases)
            {
                if (string.IsNullOrWhiteSpace(@case.Name) || !caseNames.Add(@case.Name))
                {
                    diagnostics.Add(new MirValidationDiagnostic($"Enum '{@enum.Name}' has a blank or duplicate case name '{@case.Name}'."));
                }

                var payloadNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (MirEnumPayloadField payload in @case.PayloadFields)
                {
                    if (string.IsNullOrWhiteSpace(payload.Name) || !payloadNames.Add(payload.Name))
                    {
                        diagnostics.Add(new MirValidationDiagnostic($"Enum '{@enum.Name}.{@case.Name}' has a blank or duplicate payload name '{payload.Name}'."));
                    }
                }
            }
        }

        foreach (MirFunction function in program.Functions)
        {
            ValidateEnumStatements(function.Body, enumsByName, diagnostics);
        }
    }

    private static void ValidateArrayModel(MirProgram program, List<MirValidationDiagnostic> diagnostics)
    {
        foreach (MirFunction function in program.Functions)
        {
            ValidateArrayStatements(function.Body, function.ReturnType, diagnostics);
        }
    }

    private static void ValidateArrayStatements(
        IReadOnlyList<MirStatement> statements,
        MirType functionReturnType,
        List<MirValidationDiagnostic> diagnostics)
    {
        foreach (MirStatement statement in statements)
        {
            switch (statement)
            {
                case MirVariableDeclarationStatement declaration:
                    ValidateArrayExpression(declaration.Initializer, diagnostics);
                    ValidateArrayBoundaryType(
                        declaration.Initializer.Type,
                        declaration.Local.Type,
                        $"Array initializer for local '{declaration.Local.Name}' does not match the local type.",
                        diagnostics);
                    break;
                case MirExpressionStatement expression:
                    ValidateArrayExpression(expression.Expression, diagnostics);
                    break;
                case MirReturnStatement { Expression: not null } returned:
                    ValidateArrayExpression(returned.Expression, diagnostics);
                    ValidateArrayBoundaryType(
                        returned.Expression.Type,
                        functionReturnType,
                        "Array return expression does not match the function return type.",
                        diagnostics);
                    break;
                case MirIfStatement conditional:
                    ValidateArrayExpression(conditional.Condition, diagnostics);
                    ValidateArrayStatements(conditional.ThenStatements, functionReturnType, diagnostics);
                    if (conditional.ElseStatements is not null)
                    {
                        ValidateArrayStatements(conditional.ElseStatements, functionReturnType, diagnostics);
                    }

                    break;
                case MirWhileStatement loop:
                    ValidateArrayExpression(loop.Condition, diagnostics);
                    ValidateArrayStatements(loop.BodyStatements, functionReturnType, diagnostics);
                    break;
                case MirForStatement loop:
                    if (loop.Initializer is not null)
                    {
                        ValidateArrayStatements([loop.Initializer], functionReturnType, diagnostics);
                    }
                    if (loop.Condition is not null)
                    {
                        ValidateArrayExpression(loop.Condition, diagnostics);
                    }
                    if (loop.Increment is not null)
                    {
                        ValidateArrayExpression(loop.Increment, diagnostics);
                    }
                    ValidateArrayStatements(loop.BodyStatements, functionReturnType, diagnostics);
                    break;
            }
        }
    }

    private static void ValidateArrayBoundaryType(
        MirType actual,
        MirType expected,
        string message,
        List<MirValidationDiagnostic> diagnostics)
    {
        if ((actual is MirArrayType || expected is MirArrayType)
            && !MirTypeFacts.AreEquivalent(actual, expected))
        {
            diagnostics.Add(new MirValidationDiagnostic(message));
        }
    }

    private static void ValidateArrayExpression(MirExpression? expression, List<MirValidationDiagnostic> diagnostics)
    {
        if (expression is null)
        {
            diagnostics.Add(new MirValidationDiagnostic("Array expression is missing."));
            return;
        }

        if (expression is MirArrayExpression array)
        {
            if (array.Type is not MirArrayType arrayType)
            {
                diagnostics.Add(new MirValidationDiagnostic("Array expression does not carry a MirArrayType."));
            }
            else if (arrayType.ElementType is null)
            {
                diagnostics.Add(new MirValidationDiagnostic("Array type does not have an element type."));
            }
            else
            {
                for (int index = 0; index < array.Elements.Count; index++)
                {
                    MirExpression? element = array.Elements[index];
                    if (element is null)
                    {
                        diagnostics.Add(new MirValidationDiagnostic($"Array element {index} is missing."));
                    }
                    else if (element.Type is null)
                    {
                        diagnostics.Add(new MirValidationDiagnostic($"Array element {index} does not have a type."));
                    }
                    else if (!MirTypeFacts.AreEquivalent(element.Type, arrayType.ElementType))
                    {
                        diagnostics.Add(new MirValidationDiagnostic($"Array element {index} does not match array element type '{arrayType.ElementType.Name}'."));
                    }
                }
            }
        }

        foreach (MirExpression child in EnumerateTsonExpressionChildren(expression))
        {
            ValidateArrayExpression(child, diagnostics);
        }
    }

    private static void ValidateTsonEncodingModel(MirProgram program, List<MirValidationDiagnostic> diagnostics)
    {
        var plans = new Dictionary<MirTsonEncodingPlanId, MirTsonEncodingPlan>();
        foreach (MirTsonEncodingPlan plan in program.TsonEncodingPlans)
        {
            if (string.IsNullOrWhiteSpace(plan.Id.Value) || !plans.TryAdd(plan.Id, plan))
            {
                diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan has a blank or duplicate identity '{plan.Id}'."));
                continue;
            }
            ValidateTsonEncodingPlan(plan, program, diagnostics);
        }

        if (program.TsonEncodingPlans.Count > 0)
        {
            MirEnum[] errors = program.Enums.Where(@enum => @enum.Name == "TsonEncodeError").ToArray();
            bool validError = errors.Length == 1
                && errors[0].Cases.Count == 2
                && errors[0].Cases[0].Name == "InvalidUnicode"
                && errors[0].Cases[0].PayloadFields.Count == 0
                && errors[0].Cases[1].Name == "OutputLimitExceeded"
                && errors[0].Cases[1].PayloadFields.Count == 0;
            if (!validError)
            {
                diagnostics.Add(new MirValidationDiagnostic("TSON encoding MIR requires the compiler-owned TsonEncodeError enum."));
            }
        }

        foreach (MirFunction function in program.Functions)
        {
            foreach (MirStatement statement in function.Body)
            {
                ValidateTsonEncodingExpression(statement, plans, diagnostics);
            }
        }
    }

    private static void ValidateTsonEncodingPlan(
        MirTsonEncodingPlan plan,
        MirProgram program,
        List<MirValidationDiagnostic> diagnostics)
    {
        if (!IsValidTsonSchemaIdentity(plan.SchemaIdentity))
        {
            diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan '{plan.Id}' has malformed schema identity '{plan.SchemaIdentity}'."));
        }
        if (plan.Limits.MaximumUtf8Bytes != 1_048_576
            || plan.Limits.MaximumStringCodeUnits != 262_144
            || plan.Limits.MaximumArrayLength != 100_000
            || plan.Limits.MaximumColumns != 256
            || plan.Limits.MaximumRows != 100_000
            || plan.Limits.MaximumCells != 100_000
            || plan.Limits.MaximumValueNodes != 100_000
            || plan.Limits.MaximumNestingDepth != 64)
        {
            diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan '{plan.Id}' has invalid fixed limits."));
        }
        try
        {
            string staticText = plan.TablePlan is null
                ? MirTsonCanonicalText.BuildDocumentPrefix(plan) + ";\n"
                : MirTsonCanonicalText.BuildTableStaticText(plan);
            int staticBytes = MirTsonCanonicalText.CountUtf8Bytes(staticText);
            if (staticBytes > plan.Limits.MaximumUtf8Bytes)
            {
                diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan '{plan.Id}' static document text exceeds the output limit."));
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan '{plan.Id}' has malformed canonical static text."));
        }
        if (plan.TablePlan is not null)
        {
            ValidateTsonTablePlan(plan, program, diagnostics);
        }
        else if (plan.RootType is not MirRecordType
            && !program.Enums.Any(@enum => @enum.Name == plan.RootType.Identifier))
        {
            diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan '{plan.Id}' root is not a nominal record or enum."));
        }
        if (!TsonPlanMatchesType(plan.RootValuePlan, plan.RootType))
        {
            diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan '{plan.Id}' root value plan does not match its root type."));
        }

        string[] orderedNames = plan.Definitions.Select(definition => definition.Name).ToArray();
        if (!orderedNames.SequenceEqual(orderedNames.OrderBy(name => name, StringComparer.Ordinal)))
        {
            diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan '{plan.Id}' declarations are not in ordinal name order."));
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (MirTsonNominalPlan definition in plan.Definitions)
        {
            if (!names.Add(definition.Name) || !identities.Add(definition.StableIdentity))
            {
                diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan '{plan.Id}' has a duplicate nominal name or identity '{definition.StableIdentity}'."));
            }
            if (definition.StableIdentity != $"{plan.SchemaIdentity}#{definition.Name}")
            {
                diagnostics.Add(new MirValidationDiagnostic($"TSON encoding definition '{definition.Name}' has malformed or cross-schema identity '{definition.StableIdentity}'."));
            }
            ValidateTsonNominalPlan(definition, plan, program, identities, diagnostics);
        }

        var definitionKeys = plan.Definitions.Select(TsonDefinitionKey).ToHashSet(StringComparer.Ordinal);
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        if (plan.TablePlan is null)
        {
            VisitTsonValuePlan(plan.RootValuePlan, plan, reachable, [], diagnostics);
        }
        else
        {
            foreach (MirTsonTableColumnPlan column in plan.TablePlan.Columns)
            {
                VisitTsonValuePlan(column.ElementPlan, plan, reachable, [], diagnostics);
            }
        }
        if (!definitionKeys.SetEquals(reachable))
        {
            diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan '{plan.Id}' contains missing or extraneous declarations."));
        }
    }

    private static void ValidateTsonTablePlan(
        MirTsonEncodingPlan plan,
        MirProgram program,
        List<MirValidationDiagnostic> diagnostics)
    {
        MirTsonTablePlan tablePlan = plan.TablePlan!;
        MirTableDefinition? table = program.Tables.FirstOrDefault(candidate => candidate.Id == tablePlan.TableId);
        if (table is null
            || plan.RootType is not MirTableType rootType
            || rootType.TableId != tablePlan.TableId
            || plan.RootValuePlan is not MirTsonTableValuePlan rootValue
            || rootValue.TableId != tablePlan.TableId)
        {
            diagnostics.Add(new MirValidationDiagnostic($"TSON table plan '{plan.Id}' has a missing table, root, or table value plan."));
            return;
        }

        if (string.IsNullOrWhiteSpace(tablePlan.TableId.Value)
            || tablePlan.Name != table.Name
            || tablePlan.StableIdentity != $"{plan.SchemaIdentity}#{table.Name}"
            || tablePlan.ExpectedRowCount != table.RowCount
            || tablePlan.ExpectedRowCount < 0
            || tablePlan.ExpectedRowCount > plan.Limits.MaximumRows
            || tablePlan.Columns.Count == 0
            || tablePlan.Columns.Count > plan.Limits.MaximumColumns)
        {
            diagnostics.Add(new MirValidationDiagnostic($"TSON table plan '{plan.Id}' has invalid table identity, shape, or bounds."));
        }

        if ((long)tablePlan.ExpectedRowCount * tablePlan.Columns.Count > plan.Limits.MaximumCells)
        {
            diagnostics.Add(new MirValidationDiagnostic($"TSON table plan '{plan.Id}' exceeds the table cell bound."));
        }

        if (tablePlan.Columns.Count != table.Columns.Count)
        {
            diagnostics.Add(new MirValidationDiagnostic($"TSON table plan '{plan.Id}' has missing or duplicate column plans."));
            return;
        }

        var ids = new HashSet<MirTableColumnId>();
        foreach ((MirTsonTableColumnPlan columnPlan, MirTableColumnDefinition column) in tablePlan.Columns.Zip(table.Columns))
        {
            if (string.IsNullOrWhiteSpace(columnPlan.ColumnId.Value)
                || !ids.Add(columnPlan.ColumnId)
                || columnPlan.ColumnId != column.Id
                || columnPlan.Name != column.Name
                || columnPlan.StableIdentity != $"{tablePlan.StableIdentity}.{column.Name}"
                || columnPlan.ExpectedElementCount != tablePlan.ExpectedRowCount
                || columnPlan.ExpectedElementCount != column.Constants.Count
                || !TsonPlanMatchesType(columnPlan.ElementPlan, column.ElementType)
                || ContainsTablePlan(columnPlan.ElementPlan))
            {
                diagnostics.Add(new MirValidationDiagnostic($"TSON table column plan '{tablePlan.Name}.{columnPlan.Name}' does not match its declaration, bounds, or cell plan."));
            }
        }
    }

    private static bool ContainsTablePlan(MirTsonValuePlan plan)
        => plan switch
        {
            MirTsonTableValuePlan => true,
            MirTsonArrayPlan array when array.ElementPlan is not null => ContainsTablePlan(array.ElementPlan),
            _ => false,
        };

    private static void ValidateTsonNominalPlan(
        MirTsonNominalPlan definition,
        MirTsonEncodingPlan plan,
        MirProgram program,
        HashSet<string> identities,
        List<MirValidationDiagnostic> diagnostics)
    {
        switch (definition)
        {
            case MirTsonRecordPlan recordPlan:
            {
                MirRecordDefinition? record = program.Records.FirstOrDefault(candidate => candidate.Id == recordPlan.RecordTypeId);
                if (record is null || record.IsClass || record.Name != recordPlan.Name || record.Fields.Count != recordPlan.Fields.Count)
                {
                    diagnostics.Add(new MirValidationDiagnostic($"TSON record plan '{recordPlan.Name}' does not match an ordinary serializable MIR record definition."));
                    return;
                }
                for (int index = 0; index < record.Fields.Count; index++)
                {
                    MirRecordFieldDefinition field = record.Fields[index];
                    MirTsonRecordFieldPlan fieldPlan = recordPlan.Fields[index];
                    if (field.Id != fieldPlan.FieldId
                        || field.Name != fieldPlan.Name
                        || fieldPlan.StableIdentity != $"{recordPlan.StableIdentity}.{field.Name}"
                        || !TsonPlanMatchesType(fieldPlan.ValuePlan, field.Type))
                    {
                        diagnostics.Add(new MirValidationDiagnostic($"TSON record field plan '{recordPlan.Name}.{fieldPlan.Name}' does not match declaration order, identity, or type."));
                    }
                    if (!identities.Add(fieldPlan.StableIdentity))
                    {
                        diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan has duplicate field identity '{fieldPlan.StableIdentity}'."));
                    }
                }
                break;
            }
            case MirTsonEnumPlan enumPlan:
            {
                MirEnum? @enum = program.Enums.FirstOrDefault(candidate => candidate.Name == enumPlan.Name);
                if (@enum is null || @enum.Cases.Count != enumPlan.Cases.Count)
                {
                    diagnostics.Add(new MirValidationDiagnostic($"TSON enum plan '{enumPlan.Name}' does not match its MIR enum definition."));
                    return;
                }
                for (int caseIndex = 0; caseIndex < @enum.Cases.Count; caseIndex++)
                {
                    MirEnumCase @case = @enum.Cases[caseIndex];
                    MirTsonEnumCasePlan casePlan = enumPlan.Cases[caseIndex];
                    if (@case.Name != casePlan.Name
                        || casePlan.StableIdentity != $"{enumPlan.StableIdentity}.{@case.Name}"
                        || @case.PayloadFields.Count != casePlan.Payloads.Count)
                    {
                        diagnostics.Add(new MirValidationDiagnostic($"TSON enum case plan '{enumPlan.Name}.{casePlan.Name}' does not match declaration order or identity."));
                        continue;
                    }
                    if (!identities.Add(casePlan.StableIdentity))
                    {
                        diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan has duplicate enum case identity '{casePlan.StableIdentity}'."));
                    }
                    for (int payloadIndex = 0; payloadIndex < @case.PayloadFields.Count; payloadIndex++)
                    {
                        MirEnumPayloadField payload = @case.PayloadFields[payloadIndex];
                        MirTsonEnumPayloadPlan payloadPlan = casePlan.Payloads[payloadIndex];
                        if (payload.Name != payloadPlan.Name
                            || payloadPlan.StableIdentity != $"{casePlan.StableIdentity}.{payload.Name}"
                            || !TsonPlanMatchesType(payloadPlan.ValuePlan, payload.Type))
                        {
                            diagnostics.Add(new MirValidationDiagnostic($"TSON enum payload plan '{enumPlan.Name}.{casePlan.Name}.{payloadPlan.Name}' does not match declaration order, identity, or type."));
                        }
                        if (!identities.Add(payloadPlan.StableIdentity))
                        {
                            diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan has duplicate enum payload identity '{payloadPlan.StableIdentity}'."));
                        }
                    }
                }
                break;
            }
            default:
                diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan '{plan.Id}' has an unsupported nominal definition."));
                break;
        }
    }

    private static void VisitTsonValuePlan(
        MirTsonValuePlan valuePlan,
        MirTsonEncodingPlan plan,
        HashSet<string> reachable,
        HashSet<string> visiting,
        List<MirValidationDiagnostic> diagnostics)
    {
        if (valuePlan is MirTsonArrayPlan array)
        {
            if (array.ElementPlan is null)
            {
                diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan '{plan.Id}' contains an array without an element plan."));
                return;
            }

            VisitTsonValuePlan(array.ElementPlan, plan, reachable, visiting, diagnostics);
            return;
        }

        if (valuePlan is MirTsonTableValuePlan)
        {
            diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan '{plan.Id}' contains a nested table value plan."));
            return;
        }

        string? key = valuePlan switch
        {
            MirTsonRecordValuePlan record => "record:" + record.RecordTypeId.Value,
            MirTsonEnumValuePlan @enum => "enum:" + @enum.EnumName,
            MirTsonBooleanPlan or MirTsonNumberPlan or MirTsonStringPlan => null,
            _ => "unsupported",
        };
        if (key is null) return;
        if (key == "unsupported")
        {
            diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan '{plan.Id}' contains an unsupported value family."));
            return;
        }
        if (reachable.Contains(key)) return;
        if (!visiting.Add(key))
        {
            diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan '{plan.Id}' contains a schema cycle at '{key}'."));
            return;
        }
        MirTsonNominalPlan? definition = plan.Definitions.FirstOrDefault(candidate => TsonDefinitionKey(candidate) == key);
        if (definition is null)
        {
            diagnostics.Add(new MirValidationDiagnostic($"TSON encoding plan '{plan.Id}' references missing declaration '{key}'."));
            visiting.Remove(key);
            return;
        }
        IEnumerable<MirTsonValuePlan> children = definition switch
        {
            MirTsonRecordPlan record => record.Fields.Select(field => field.ValuePlan),
            MirTsonEnumPlan @enum => @enum.Cases.SelectMany(@case => @case.Payloads.Select(payload => payload.ValuePlan)),
            _ => [],
        };
        foreach (MirTsonValuePlan child in children)
        {
            VisitTsonValuePlan(child, plan, reachable, visiting, diagnostics);
        }
        visiting.Remove(key);
        reachable.Add(key);
    }

    private static string TsonDefinitionKey(MirTsonNominalPlan definition)
        => definition switch
        {
            MirTsonRecordPlan record => "record:" + record.RecordTypeId.Value,
            MirTsonEnumPlan @enum => "enum:" + @enum.Name,
            _ => "unsupported",
        };

    private static bool TsonPlanMatchesType(MirTsonValuePlan plan, MirType type)
        => (plan, type) switch
        {
            (MirTsonBooleanPlan, MirType { Identifier: "boolean" }) => true,
            (MirTsonNumberPlan, MirType { Identifier: "number" }) => true,
            (MirTsonStringPlan, MirType { Identifier: "string" }) => true,
            (MirTsonRecordValuePlan value, MirRecordType record) => value.RecordTypeId == record.RecordTypeId,
            (MirTsonEnumValuePlan value, MirType named) when named is not MirArrayType and not MirResultType => value.EnumName == named.Identifier,
            (MirTsonArrayPlan array, MirArrayType arrayType) when array.ElementPlan is not null => TsonPlanMatchesType(array.ElementPlan, arrayType.ElementType),
            (MirTsonTableValuePlan table, MirTableType tableType) => table.TableId == tableType.TableId,
            _ => false,
        };

    private static bool IsValidTsonSchemaIdentity(string identity)
        => identity.StartsWith("copeland://", StringComparison.Ordinal)
            && identity.Length > "copeland://".Length
            && !identity.Any(char.IsWhiteSpace)
            && !identity.Contains('#', StringComparison.Ordinal);

    private static void ValidateTsonEncodingExpression(
        MirStatement statement,
        IReadOnlyDictionary<MirTsonEncodingPlanId, MirTsonEncodingPlan> plans,
        List<MirValidationDiagnostic> diagnostics)
    {
        switch (statement)
        {
            case MirVariableDeclarationStatement declaration: ValidateTsonEncodingExpression(declaration.Initializer, plans, diagnostics); break;
            case MirExpressionStatement expression: ValidateTsonEncodingExpression(expression.Expression, plans, diagnostics); break;
            case MirReturnStatement { Expression: not null } returned: ValidateTsonEncodingExpression(returned.Expression, plans, diagnostics); break;
            case MirIfStatement conditional:
                ValidateTsonEncodingExpression(conditional.Condition, plans, diagnostics);
                foreach (MirStatement nested in conditional.ThenStatements) ValidateTsonEncodingExpression(nested, plans, diagnostics);
                if (conditional.ElseStatements is not null) foreach (MirStatement nested in conditional.ElseStatements) ValidateTsonEncodingExpression(nested, plans, diagnostics);
                break;
            case MirWhileStatement loop:
                ValidateTsonEncodingExpression(loop.Condition, plans, diagnostics);
                foreach (MirStatement nested in loop.BodyStatements) ValidateTsonEncodingExpression(nested, plans, diagnostics);
                break;
            case MirForStatement loop:
                if (loop.Initializer is not null) ValidateTsonEncodingExpression(loop.Initializer, plans, diagnostics);
                if (loop.Condition is not null) ValidateTsonEncodingExpression(loop.Condition, plans, diagnostics);
                if (loop.Increment is not null) ValidateTsonEncodingExpression(loop.Increment, plans, diagnostics);
                foreach (MirStatement nested in loop.BodyStatements) ValidateTsonEncodingExpression(nested, plans, diagnostics);
                break;
        }
    }

    private static void ValidateTsonEncodingExpression(
        MirExpression expression,
        IReadOnlyDictionary<MirTsonEncodingPlanId, MirTsonEncodingPlan> plans,
        List<MirValidationDiagnostic> diagnostics)
    {
        if (expression is MirTsonEncodeExpression encode)
        {
            if (!plans.TryGetValue(encode.PlanId, out MirTsonEncodingPlan? plan))
            {
                diagnostics.Add(new MirValidationDiagnostic($"TSON encode expression references missing plan '{encode.PlanId}'."));
            }
            else
            {
                if (!MirTypeFacts.AreEquivalent(encode.Operand.Type, plan.RootType))
                    diagnostics.Add(new MirValidationDiagnostic($"TSON encode expression operand does not match plan '{encode.PlanId}' root type."));
                bool validResult = encode.ResultType.SuccessType.Identifier == "string"
                    && encode.ResultType.ErrorType.Identifier == "TsonEncodeError";
                if (!validResult)
                    diagnostics.Add(new MirValidationDiagnostic($"TSON encode expression for plan '{encode.PlanId}' has the wrong Result type."));
            }
        }
        if (expression is MirTsonTransportExpression transport)
        {
            plans.TryGetValue(transport.RequestPlanId, out MirTsonEncodingPlan? requestPlan);
            plans.TryGetValue(transport.ResponsePlanId, out MirTsonEncodingPlan? responsePlan);
            plans.TryGetValue(transport.RemoteErrorPlanId, out MirTsonEncodingPlan? errorPlan);
            bool plansExist = requestPlan is not null && responsePlan is not null && errorPlan is not null;
            if (!plansExist
                || transport.Operation.Type.Identifier != "string"
                || requestPlan is null
                || responsePlan is null
                || errorPlan is null
                || !MirTypeFacts.AreEquivalent(requestPlan.RootType, transport.Request.Type)
                || transport.AsyncType.EventualType is not MirResultType result
                || !MirTypeFacts.AreEquivalent(responsePlan.RootType, result.SuccessType)
                || !MirTypeFacts.AreEquivalent(errorPlan.RootType, result.ErrorType))
            {
                diagnostics.Add(new MirValidationDiagnostic("TSON transport expression has missing plans or incompatible request, response, or remote-error types."));
            }
        }
        if (expression is MirNpmCallExpression npm)
        {
            plans.TryGetValue(npm.RequestPlanId, out MirTsonEncodingPlan? requestPlan);
            plans.TryGetValue(npm.ResponsePlanId, out MirTsonEncodingPlan? responsePlan);
            plans.TryGetValue(npm.RemoteErrorPlanId, out MirTsonEncodingPlan? errorPlan);
            if (requestPlan is null
                || responsePlan is null
                || errorPlan is null
                || !MirTypeFacts.AreEquivalent(requestPlan.RootType, npm.ArgumentTuple.Type)
                || npm.AsyncType.EventualType is not MirResultType)
            {
                diagnostics.Add(new MirValidationDiagnostic("npm call has missing private argument-tuple, response, or remote-error transport metadata."));
            }
        }
        foreach (MirExpression child in EnumerateTsonExpressionChildren(expression))
        {
            ValidateTsonEncodingExpression(child, plans, diagnostics);
        }
    }

    private static IEnumerable<MirExpression> EnumerateTsonExpressionChildren(MirExpression expression)
        => expression switch
        {
            MirTsonEncodeExpression encode => [encode.Operand],
            MirTsonTransportExpression transport => [transport.Operation, transport.Request],
            MirNpmCallExpression npm => npm.Arguments.Append(npm.ArgumentTuple),
            MirAssignmentExpression assignment => [assignment.Expression],
            MirUnaryExpression unary => [unary.Operand],
            MirBinaryExpression binary => [binary.Left, binary.Right],
            MirCallExpression call => call.Arguments,
            MirArrayExpression array => array.Elements,
            MirRecordConstructionExpression record => record.Initializers.Select(value => value.Value),
            MirRecordFieldAccessExpression access => [access.Receiver],
            MirRecordWithExpression update => update.Replacements.Select(value => value.Value).Prepend(update.Source),
            MirEnumValueExpression value => value.Arguments,
            MirMatchExpression match => match.Arms.Select(arm => arm.Expression).Prepend(match.Scrutinee),
            MirResultMatchExpression match => [match.Scrutinee, match.OkExpression, match.ErrExpression],
            MirIfExpression conditional => [conditional.Condition, conditional.ThenExpression, conditional.ElseExpression],
            MirOkExpression ok => [ok.Payload],
            MirErrExpression err => [err.Payload],
            MirPropagateExpression propagate => [propagate.Operand],
            MirUnwrapExpression unwrap => [unwrap.Operand],
            MirTryExpression value => value.Protected.PrefixStatements.OfType<MirExpressionStatement>().Select(statement => statement.Expression).Append(value.Protected.ValueExpression).Append(value.Handler.ValueExpression),
            _ => [],
        };

    private static void ValidateTableModel(MirProgram program, List<MirValidationDiagnostic> diagnostics)
    {
        if (program.Tables.Count > 0)
        {
            ValidateTableBoundsErrorDefinition(program.Enums, diagnostics);
        }
        var tables = new Dictionary<MirTableId, MirTableDefinition>();
        var tableNames = new HashSet<string>(StringComparer.Ordinal);
        var rowTypeIds = new HashSet<string>(StringComparer.Ordinal);
        var columns = new Dictionary<MirTableColumnId, MirTableColumnDefinition>();
        foreach (var table in program.Tables)
        {
            if (string.IsNullOrWhiteSpace(table.Id.Value) || !tables.TryAdd(table.Id, table))
                diagnostics.Add(new MirValidationDiagnostic($"Table has a blank or duplicate identity '{table.Id}'."));
            if (string.IsNullOrWhiteSpace(table.Name) || !tableNames.Add(table.Name))
                diagnostics.Add(new MirValidationDiagnostic($"Table has a blank or duplicate name '{table.Name}'."));
            if (string.IsNullOrWhiteSpace(table.RowTypeId) || !rowTypeIds.Add(table.RowTypeId))
                diagnostics.Add(new MirValidationDiagnostic($"Table '{table.Name}' has a blank or duplicate row type identity '{table.RowTypeId}'."));
            if (table.Columns.Count == 0)
                diagnostics.Add(new MirValidationDiagnostic($"Table '{table.Name}' must have at least one column."));
            if (table.RowCount < 0)
                diagnostics.Add(new MirValidationDiagnostic($"Table '{table.Name}' has a negative row count."));

            var columnNames = new HashSet<string>(StringComparer.Ordinal);
            var constantValidationState = new TableConstantValidationState();
            foreach (var column in table.Columns)
            {
                if (string.IsNullOrWhiteSpace(column.Id.Value) || !columns.TryAdd(column.Id, column))
                    diagnostics.Add(new MirValidationDiagnostic($"Table '{table.Name}' has a blank or duplicate column identity '{column.Id}'."));
                if (string.IsNullOrWhiteSpace(column.Name) || !columnNames.Add(column.Name))
                    diagnostics.Add(new MirValidationDiagnostic($"Table '{table.Name}' has a blank or duplicate column name '{column.Name}'."));
                if (column.ElementType.Identifier is "error" or "void")
                    diagnostics.Add(new MirValidationDiagnostic($"Table column '{table.Name}.{column.Name}' has an invalid element type."));
                if (ContainsClassTableType(column.ElementType, program, []))
                    diagnostics.Add(new MirValidationDiagnostic($"Table column '{table.Name}.{column.Name}' contains a class value, which is not a table cell type."));
                if (column.Constants.Count != table.RowCount)
                    diagnostics.Add(new MirValidationDiagnostic($"Table column '{table.Name}.{column.Name}' has {column.Constants.Count} constants but row count is {table.RowCount}."));
                foreach (var constant in column.Constants)
                {
                    if (!MirTypeFacts.AreEquivalent(constant.Type, column.ElementType))
                        diagnostics.Add(new MirValidationDiagnostic($"Table constant in '{table.Name}.{column.Name}' does not match the column element type."));
                    ValidateTableConstant(
                        constant,
                        column.ElementType,
                        program,
                        diagnostics,
                        $"{table.Name}.{column.Name}",
                        constantValidationState);
                }
            }
        }

        foreach (var function in program.Functions)
        {
            ValidateTableType(function.ReturnType, tables, rowTypeIds, diagnostics, $"function '{function.Name}' return");
            foreach (var parameter in function.Parameters) ValidateTableType(parameter.Type, tables, rowTypeIds, diagnostics, $"parameter '{parameter.Name}'");
            foreach (var local in function.Locals) ValidateTableType(local.Type, tables, rowTypeIds, diagnostics, $"local '{local.Name}'");
            ValidateTableStatements(function.Body, tables, rowTypeIds, columns, diagnostics);
        }
    }

    private static bool ContainsClassTableType(MirType type, MirProgram program, HashSet<MirRecordTypeId> visiting)
    {
        switch (type)
        {
            case MirRecordType recordType:
            {
                MirRecordDefinition? definition = program.Records.FirstOrDefault(record => record.Id == recordType.RecordTypeId);
                if (definition is null || !visiting.Add(recordType.RecordTypeId))
                {
                    return definition?.IsClass == true;
                }
                bool contains = definition.IsClass || definition.Fields.Any(field => ContainsClassTableType(field.Type, program, visiting));
                visiting.Remove(recordType.RecordTypeId);
                return contains;
            }
            case MirArrayType array:
                return ContainsClassTableType(array.ElementType, program, visiting);
            case MirResultType result:
                return ContainsClassTableType(result.SuccessType, program, visiting)
                    || ContainsClassTableType(result.ErrorType, program, visiting);
            default:
                return false;
        }
    }

    private static void ValidateTableBoundsErrorDefinition(IReadOnlyList<MirEnum> enums, List<MirValidationDiagnostic> diagnostics)
    {
        MirEnum[] matchingDefinitions = enums.Where(@enum => @enum.Name == "TableBoundsError").ToArray();
        if (matchingDefinitions.Length != 1)
        {
            diagnostics.Add(new MirValidationDiagnostic("Table MIR requires the compiler-owned TableBoundsError enum."));
            return;
        }
        MirEnum boundsError = matchingDefinitions[0];

        bool hasInvalidIndex = boundsError.Cases.Any(@case => @case.Name == "InvalidIndex"
            && @case.PayloadFields.Count == 1
            && @case.PayloadFields[0].Name == "index"
            && @case.PayloadFields[0].Type.Identifier == "number");
        bool hasOutOfBounds = boundsError.Cases.Any(@case => @case.Name == "OutOfBounds"
            && @case.PayloadFields.Count == 2
            && @case.PayloadFields[0].Name == "index"
            && @case.PayloadFields[0].Type.Identifier == "number"
            && @case.PayloadFields[1].Name == "rowCount"
            && @case.PayloadFields[1].Type.Identifier == "number");
        if (!hasInvalidIndex || !hasOutOfBounds || boundsError.Cases.Count != 2)
        {
            diagnostics.Add(new MirValidationDiagnostic("TableBoundsError does not have its required compiler-owned cases and payload types."));
        }
    }

    private sealed class TableConstantValidationState
    {
        public int NodeCount { get; set; }
        public HashSet<MirTableConstant> Seen { get; } = new(ReferenceEqualityComparer.Instance);
    }

    private static void ValidateTableConstant(
        MirTableConstant constant,
        MirType expectedType,
        MirProgram program,
        List<MirValidationDiagnostic> diagnostics,
        string context,
        TableConstantValidationState? state = null,
        int depth = 1)
    {
        state ??= new TableConstantValidationState();
        state.NodeCount++;
        if (state.NodeCount > 100_000)
        {
            diagnostics.Add(new MirValidationDiagnostic($"Table constant in '{context}' exceeds the closed-constant node limit."));
            return;
        }
        if (depth > 64)
        {
            diagnostics.Add(new MirValidationDiagnostic($"Table constant in '{context}' exceeds the nesting-depth limit."));
            return;
        }
        if (!state.Seen.Add(constant))
        {
            diagnostics.Add(new MirValidationDiagnostic($"Table constant in '{context}' contains an alias or cycle."));
            return;
        }

        if (!MirTypeFacts.AreEquivalent(constant.Type, expectedType))
        {
            diagnostics.Add(new MirValidationDiagnostic($"Table constant in '{context}' does not match the column element type."));
        }

        switch (constant)
        {
            case MirTableLiteralConstant literal when IsValidTableLiteral(literal):
                return;
            case MirTableArrayConstant array:
                if (array.ArrayType.ElementType is null)
                {
                    diagnostics.Add(new MirValidationDiagnostic($"Table array constant in '{context}' has no element type."));
                    return;
                }
                if (array.Elements.Count > 100_000)
                {
                    diagnostics.Add(new MirValidationDiagnostic($"Table array constant in '{context}' exceeds the array-length limit."));
                    return;
                }
                foreach (MirTableConstant element in array.Elements)
                {
                    if (!MirTypeFacts.AreEquivalent(element.Type, array.ArrayType.ElementType))
                    {
                        diagnostics.Add(new MirValidationDiagnostic($"Table array constant in '{context}' has a heterogeneous element."));
                    }
                    ValidateTableConstant(
                        element,
                        array.ArrayType.ElementType,
                        program,
                        diagnostics,
                        context,
                        state,
                        depth + 1);
                }
                return;
            case MirTableRecordConstant record:
                MirRecordDefinition? definition = program.Records.FirstOrDefault(candidate => candidate.Id == record.RecordTypeId);
                if (definition is null || record.Type is not MirRecordType type || type.RecordTypeId != record.RecordTypeId)
                {
                    diagnostics.Add(new MirValidationDiagnostic($"Table record constant in '{context}' has an unknown record identity."));
                    return;
                }
                if (record.Fields.Count != definition.Fields.Count)
                    diagnostics.Add(new MirValidationDiagnostic($"Table record constant in '{context}' does not provide every record field."));
                var seenFieldIds = new HashSet<MirRecordFieldId>();
                foreach (var field in record.Fields)
                {
                    if (!seenFieldIds.Add(field.FieldId))
                    {
                        diagnostics.Add(new MirValidationDiagnostic($"Table record constant in '{context}' has a duplicate field identity '{field.FieldId}'."));
                    }
                    MirRecordFieldDefinition? fieldDefinition = definition.Fields.FirstOrDefault(candidate => candidate.Id == field.FieldId);
                    if (fieldDefinition is null) diagnostics.Add(new MirValidationDiagnostic($"Table record constant in '{context}' has an unknown field identity '{field.FieldId}'."));
                    else ValidateTableConstant(field.Value, fieldDefinition.Type, program, diagnostics, context, state, depth + 1);
                }
                if (definition.Fields.Any(field => !seenFieldIds.Contains(field.Id)))
                {
                    diagnostics.Add(new MirValidationDiagnostic($"Table record constant in '{context}' does not provide every record field."));
                }
                return;
            case MirTableEnumConstant value:
                MirEnum? enumDefinition = program.Enums.FirstOrDefault(candidate => candidate.Name == value.EnumName);
                MirEnumCase? @case = enumDefinition?.Cases.FirstOrDefault(candidate => candidate.Name == value.CaseName);
                if (enumDefinition is null || @case is null || value.Type.Identifier != value.EnumName)
                {
                    diagnostics.Add(new MirValidationDiagnostic($"Table enum constant in '{context}' has an unknown enum or case."));
                    return;
                }
                if (value.Payloads.Count != @case.PayloadFields.Count)
                    diagnostics.Add(new MirValidationDiagnostic($"Table enum constant in '{context}' has an invalid payload count."));
                for (int index = 0; index < Math.Min(value.Payloads.Count, @case.PayloadFields.Count); index++)
                    ValidateTableConstant(value.Payloads[index], @case.PayloadFields[index].Type, program, diagnostics, context, state, depth + 1);
                return;
            case MirTableResultConstant result:
                if (result.Type.SuccessType.Identifier == "void")
                {
                    diagnostics.Add(new MirValidationDiagnostic($"Table Result constant in '{context}' cannot use a void success payload."));
                    return;
                }
                ValidateTableConstant(result.Payload, result.IsOk ? result.Type.SuccessType : result.Type.ErrorType, program, diagnostics, context, state, depth + 1);
                return;
            default:
                diagnostics.Add(new MirValidationDiagnostic($"Table constant in '{context}' is not a supported closed constant."));
                return;
        }
    }

    private static bool IsValidTableLiteral(MirTableLiteralConstant literal)
        => literal.Type.Identifier switch
        {
            "number" => literal.Value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal,
            "string" => literal.Value is string,
            "boolean" => literal.Value is bool,
            _ => false,
        };

    private static void ValidateTableType(MirType type, IReadOnlyDictionary<MirTableId, MirTableDefinition> tables, IReadOnlySet<string> rowTypeIds, List<MirValidationDiagnostic> diagnostics, string context)
    {
        switch (type)
        {
            case MirTableType table when !tables.ContainsKey(table.TableId):
                diagnostics.Add(new MirValidationDiagnostic($"Table type '{table.TableId}' used by {context} has no definition."));
                break;
            case MirTableRowType row when !rowTypeIds.Contains(row.RowTypeId):
                diagnostics.Add(new MirValidationDiagnostic($"Table row type '{row.RowTypeId}' used by {context} has no definition."));
                break;
            case MirColumnType column:
                ValidateTableType(column.ElementType, tables, rowTypeIds, diagnostics, context);
                break;
            case MirArrayType array:
                ValidateTableType(array.ElementType, tables, rowTypeIds, diagnostics, context);
                break;
            case MirResultType result:
                ValidateTableType(result.SuccessType, tables, rowTypeIds, diagnostics, context);
                ValidateTableType(result.ErrorType, tables, rowTypeIds, diagnostics, context);
                break;
        }
    }

    private static void ValidateTableStatements(IReadOnlyList<MirStatement> statements, IReadOnlyDictionary<MirTableId, MirTableDefinition> tables, IReadOnlySet<string> rowTypeIds, IReadOnlyDictionary<MirTableColumnId, MirTableColumnDefinition> columns, List<MirValidationDiagnostic> diagnostics)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case MirVariableDeclarationStatement declaration: ValidateTableExpression(declaration.Initializer, tables, rowTypeIds, columns, diagnostics); break;
                case MirExpressionStatement expression: ValidateTableExpression(expression.Expression, tables, rowTypeIds, columns, diagnostics); break;
                case MirReturnStatement { Expression: not null } returned: ValidateTableExpression(returned.Expression, tables, rowTypeIds, columns, diagnostics); break;
                case MirIfStatement conditional:
                    ValidateTableExpression(conditional.Condition, tables, rowTypeIds, columns, diagnostics);
                    ValidateTableStatements(conditional.ThenStatements, tables, rowTypeIds, columns, diagnostics);
                    if (conditional.ElseStatements is not null) ValidateTableStatements(conditional.ElseStatements, tables, rowTypeIds, columns, diagnostics);
                    break;
                case MirWhileStatement loop:
                    ValidateTableExpression(loop.Condition, tables, rowTypeIds, columns, diagnostics);
                    ValidateTableStatements(loop.BodyStatements, tables, rowTypeIds, columns, diagnostics);
                    break;
                case MirForStatement loop:
                    if (loop.Initializer is not null) ValidateTableStatements([loop.Initializer], tables, rowTypeIds, columns, diagnostics);
                    if (loop.Condition is not null) ValidateTableExpression(loop.Condition, tables, rowTypeIds, columns, diagnostics);
                    if (loop.Increment is not null) ValidateTableExpression(loop.Increment, tables, rowTypeIds, columns, diagnostics);
                    ValidateTableStatements(loop.BodyStatements, tables, rowTypeIds, columns, diagnostics);
                    break;
            }
        }
    }

    private static void ValidateTableExpression(MirExpression expression, IReadOnlyDictionary<MirTableId, MirTableDefinition> tables, IReadOnlySet<string> rowTypeIds, IReadOnlyDictionary<MirTableColumnId, MirTableColumnDefinition> columns, List<MirValidationDiagnostic> diagnostics)
    {
        ValidateTableType(expression.Type, tables, rowTypeIds, diagnostics, "expression");
        switch (expression)
        {
            case MirTableReferenceExpression reference:
                if (!tables.TryGetValue(reference.TableId, out var table) || reference.Type is not MirTableType type || type.TableId != reference.TableId)
                    diagnostics.Add(new MirValidationDiagnostic($"Table reference '{reference.TableId}' has an unknown identity or incorrect type."));
                break;
            case MirTableColumnAccessExpression access:
                ValidateTableExpression(access.Receiver, tables, rowTypeIds, columns, diagnostics);
                if (!tables.TryGetValue(access.TableId, out var owner) || !columns.TryGetValue(access.ColumnId, out var column) || !owner.Columns.Contains(column)
                    || access.Receiver.Type is not MirTableType receiverType || receiverType.TableId != access.TableId
                    || access.Type is not MirColumnType columnType || !MirTypeFacts.AreEquivalent(columnType.ElementType, column.ElementType))
                    diagnostics.Add(new MirValidationDiagnostic($"Table column access '{access.ColumnId}' has an invalid table identity, receiver, or type."));
                break;
            case MirTableRowAccessExpression access:
                ValidateTableExpression(access.Receiver, tables, rowTypeIds, columns, diagnostics);
                ValidateTableExpression(access.Index, tables, rowTypeIds, columns, diagnostics);
                if (!tables.TryGetValue(access.TableId, out var indexedTable)
                    || access.Receiver.Type is not MirTableType tableReceiver || tableReceiver.TableId != access.TableId
                    || access.Index.Type.Identifier != "number"
                    || access.Type is not MirResultType { SuccessType: MirTableRowType row, ErrorType: MirNamedType rowError }
                    || row.RowTypeId != indexedTable.RowTypeId || rowError.Identifier != "TableBoundsError")
                    diagnostics.Add(new MirValidationDiagnostic($"Table row access '{access.TableId}' has an invalid receiver, index, or Result bounds type."));
                break;
            case MirColumnElementAccessExpression access:
                ValidateTableExpression(access.Receiver, tables, rowTypeIds, columns, diagnostics);
                ValidateTableExpression(access.Index, tables, rowTypeIds, columns, diagnostics);
                if (access.Receiver.Type is not MirColumnType columnReceiver || access.Index.Type.Identifier != "number"
                    || access.Type is not MirResultType { ErrorType: MirNamedType columnError } result
                    || !MirTypeFacts.AreEquivalent(result.SuccessType, columnReceiver.ElementType) || columnError.Identifier != "TableBoundsError")
                    diagnostics.Add(new MirValidationDiagnostic("Column element access has an invalid receiver, index, or Result bounds type."));
                break;
            case MirTableRowFieldAccessExpression access:
                ValidateTableExpression(access.Receiver, tables, rowTypeIds, columns, diagnostics);
                if (access.Receiver.Type is not MirTableRowType rowReceiver || rowReceiver.RowTypeId != access.RowTypeId || !rowTypeIds.Contains(access.RowTypeId))
                    diagnostics.Add(new MirValidationDiagnostic($"Table row field access '{access.FieldId}' has an invalid row receiver or row type."));
                else
                {
                    MirTableDefinition? rowOwner = tables.Values.FirstOrDefault(table => table.RowTypeId == access.RowTypeId);
                    MirTableColumnDefinition? field = rowOwner?.Columns.FirstOrDefault(column => access.FieldId == column.Id.Value + ".f");
                    if (field is null)
                        diagnostics.Add(new MirValidationDiagnostic($"Table row field access '{access.FieldId}' has an unknown field identity."));
                    else if (!MirTypeFacts.AreEquivalent(field.ElementType, access.Type))
                        diagnostics.Add(new MirValidationDiagnostic($"Table row field access type does not match field '{access.FieldId}'."));
                }
                break;
            default:
                foreach (var child in EnumerateTableExpressionChildren(expression)) ValidateTableExpression(child, tables, rowTypeIds, columns, diagnostics);
                break;
        }
    }

    private static IEnumerable<MirExpression> EnumerateTableExpressionChildren(MirExpression expression)
        => expression switch
        {
            MirUnaryExpression unary => [unary.Operand],
            MirBinaryExpression binary => [binary.Left, binary.Right],
            MirAssignmentExpression assignment => [assignment.Expression],
            MirCallExpression call => call.Arguments,
            MirArrayExpression array => array.Elements,
            MirRecordConstructionExpression record => record.Initializers.Select(value => value.Value),
            MirRecordFieldAccessExpression access => [access.Receiver],
            MirRecordWithExpression update => update.Replacements.Select(value => value.Value).Prepend(update.Source),
            MirEnumValueExpression value => value.Arguments,
            MirMatchExpression match => match.Arms.Select(arm => arm.Expression).Prepend(match.Scrutinee),
            MirResultMatchExpression match => [match.Scrutinee, match.OkExpression, match.ErrExpression],
            MirIfExpression conditional => [conditional.Condition, conditional.ThenExpression, conditional.ElseExpression],
            MirOkExpression ok => [ok.Payload],
            MirErrExpression err => [err.Payload],
            MirPropagateExpression propagate => [propagate.Operand],
            MirUnwrapExpression unwrap => [unwrap.Operand],
            MirTryExpression value => value.Protected.PrefixStatements.OfType<MirExpressionStatement>().Select(statement => statement.Expression).Append(value.Protected.ValueExpression).Append(value.Handler.ValueExpression),
            _ => [],
        };

    private static void ValidateRecordModel(MirProgram program, List<MirValidationDiagnostic> diagnostics)
    {
        var recordsById = new Dictionary<MirRecordTypeId, MirRecordDefinition>();
        var recordNames = new HashSet<string>(StringComparer.Ordinal);
        var allFieldIds = new HashSet<MirRecordFieldId>();
        foreach (var record in program.Records)
        {
            if (string.IsNullOrWhiteSpace(record.Id.Value))
            {
                diagnostics.Add(new MirValidationDiagnostic("Record identity must not be blank."));
            }
            else if (!recordsById.TryAdd(record.Id, record))
            {
                diagnostics.Add(new MirValidationDiagnostic($"Duplicate record identity '{record.Id}'."));
            }
            if (string.IsNullOrWhiteSpace(record.Name) || !recordNames.Add(record.Name))
            {
                diagnostics.Add(new MirValidationDiagnostic($"Duplicate or blank record name '{record.Name}'."));
            }

            var fieldIds = new HashSet<MirRecordFieldId>();
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in record.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Id.Value) || !fieldIds.Add(field.Id) || !allFieldIds.Add(field.Id))
                {
                    diagnostics.Add(new MirValidationDiagnostic($"Record '{record.Name}' has a blank or duplicate field identity '{field.Id}'."));
                }
                if (string.IsNullOrWhiteSpace(field.Name) || !fieldNames.Add(field.Name))
                {
                    diagnostics.Add(new MirValidationDiagnostic($"Record '{record.Name}' has a blank or duplicate field name '{field.Name}'."));
                }
            }
        }

        foreach (var record in program.Records)
        {
            foreach (var field in record.Fields)
            {
                ValidateRecordTypeReference(field.Type, recordsById, diagnostics, $"field '{record.Name}.{field.Name}'");
            }
        }
        ValidateRecordDefinitionCycles(program.Records, program.Enums, recordsById, diagnostics);

        foreach (var @enum in program.Enums)
        {
            foreach (var field in @enum.Cases.SelectMany(@case => @case.PayloadFields))
            {
                ValidateRecordTypeReference(field.Type, recordsById, diagnostics, $"enum '{@enum.Name}' payload");
            }
        }
        foreach (var function in program.Functions)
        {
            ValidateRecordTypeReference(function.ReturnType, recordsById, diagnostics, $"function '{function.Name}' return");
            foreach (var parameter in function.Parameters) ValidateRecordTypeReference(parameter.Type, recordsById, diagnostics, $"parameter '{parameter.Name}'");
            foreach (var local in function.Locals) ValidateRecordTypeReference(local.Type, recordsById, diagnostics, $"local '{local.Name}'");
            ValidateRecordStatements(function.Body, recordsById, diagnostics);
        }
    }

    private static void ValidateRecordDefinitionCycles(
        IReadOnlyList<MirRecordDefinition> records,
        IReadOnlyList<MirEnum> enums,
        IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> recordsById,
        List<MirValidationDiagnostic> diagnostics)
    {
        var enumsByName = new Dictionary<string, MirEnum>(StringComparer.Ordinal);
        foreach (var @enum in enums)
        {
            enumsByName.TryAdd(@enum.Name, @enum);
        }
        var visiting = new HashSet<MirRecordTypeId>();
        var visited = new HashSet<MirRecordTypeId>();
        foreach (var record in records)
        {
            Visit(record);
        }

        void Visit(MirRecordDefinition record)
        {
            if (visited.Contains(record.Id)) return;
            if (!visiting.Add(record.Id))
            {
                diagnostics.Add(new MirValidationDiagnostic($"Recursive record definition involving '{record.Id}' is unsupported."));
                return;
            }
            foreach (var id in record.Fields.SelectMany(field => EnumerateCycleRecordTypeIds(field.Type, enumsByName, [])))
            {
                if (recordsById.TryGetValue(id, out var dependency)) Visit(dependency);
            }
            visiting.Remove(record.Id);
            visited.Add(record.Id);
        }
    }

    private static IEnumerable<MirRecordTypeId> EnumerateCycleRecordTypeIds(
        MirType type,
        IReadOnlyDictionary<string, MirEnum> enumsByName,
        HashSet<string> visitedEnums)
    {
        switch (type)
        {
            case MirRecordType recordType:
                yield return recordType.RecordTypeId;
                break;
            case MirArrayType arrayType:
                foreach (var id in EnumerateCycleRecordTypeIds(arrayType.ElementType, enumsByName, visitedEnums)) yield return id;
                break;
            case MirResultType resultType:
                foreach (var id in EnumerateCycleRecordTypeIds(resultType.SuccessType, enumsByName, visitedEnums)) yield return id;
                foreach (var id in EnumerateCycleRecordTypeIds(resultType.ErrorType, enumsByName, visitedEnums)) yield return id;
                break;
            case MirNamedType namedType when enumsByName.TryGetValue(namedType.Identifier, out var @enum) && visitedEnums.Add(namedType.Identifier):
                foreach (var payloadType in @enum.Cases.SelectMany(@case => @case.PayloadFields).Select(field => field.Type))
                {
                    foreach (var id in EnumerateCycleRecordTypeIds(payloadType, enumsByName, visitedEnums)) yield return id;
                }
                break;
        }
    }

    private static IEnumerable<MirRecordTypeId> EnumerateRecordTypeIds(MirType type)
    {
        switch (type)
        {
            case MirRecordType recordType:
                yield return recordType.RecordTypeId;
                break;
            case MirArrayType arrayType:
                foreach (var id in EnumerateRecordTypeIds(arrayType.ElementType)) yield return id;
                break;
            case MirResultType resultType:
                foreach (var id in EnumerateRecordTypeIds(resultType.SuccessType)) yield return id;
                foreach (var id in EnumerateRecordTypeIds(resultType.ErrorType)) yield return id;
                break;
        }
    }

    private static void ValidateRecordTypeReference(MirType type, IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records, List<MirValidationDiagnostic> diagnostics, string context)
    {
        foreach (var id in EnumerateRecordTypeIds(type))
        {
            if (!records.ContainsKey(id)) diagnostics.Add(new MirValidationDiagnostic($"Record type '{id}' used by {context} has no definition."));
        }
    }

    private static void ValidateRecordStatements(IReadOnlyList<MirStatement> statements, IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records, List<MirValidationDiagnostic> diagnostics)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case MirVariableDeclarationStatement declaration: ValidateRecordExpression(declaration.Initializer, records, diagnostics); break;
                case MirExpressionStatement expression: ValidateRecordExpression(expression.Expression, records, diagnostics); break;
                case MirReturnStatement { Expression: not null } returned: ValidateRecordExpression(returned.Expression, records, diagnostics); break;
                case MirIfStatement conditional:
                    ValidateRecordExpression(conditional.Condition, records, diagnostics);
                    ValidateRecordStatements(conditional.ThenStatements, records, diagnostics);
                    if (conditional.ElseStatements is not null) ValidateRecordStatements(conditional.ElseStatements, records, diagnostics);
                    break;
                case MirWhileStatement loop:
                    ValidateRecordExpression(loop.Condition, records, diagnostics);
                    ValidateRecordStatements(loop.BodyStatements, records, diagnostics);
                    break;
                case MirForStatement loop:
                    if (loop.Initializer is not null) ValidateRecordStatements([loop.Initializer], records, diagnostics);
                    if (loop.Condition is not null) ValidateRecordExpression(loop.Condition, records, diagnostics);
                    if (loop.Increment is not null) ValidateRecordExpression(loop.Increment, records, diagnostics);
                    ValidateRecordStatements(loop.BodyStatements, records, diagnostics);
                    break;
            }
        }
    }

    private static void ValidateEnumStatements(
        IReadOnlyList<MirStatement> statements,
        IReadOnlyDictionary<string, MirEnum> enumsByName,
        List<MirValidationDiagnostic> diagnostics)
    {
        foreach (MirStatement statement in statements)
        {
            switch (statement)
            {
                case MirVariableDeclarationStatement declaration:
                    ValidateEnumExpression(declaration.Initializer, enumsByName, diagnostics);
                    break;
                case MirExpressionStatement expression:
                    ValidateEnumExpression(expression.Expression, enumsByName, diagnostics);
                    break;
                case MirReturnStatement { Expression: not null } returned:
                    ValidateEnumExpression(returned.Expression, enumsByName, diagnostics);
                    break;
                case MirIfStatement conditional:
                    ValidateEnumExpression(conditional.Condition, enumsByName, diagnostics);
                    ValidateEnumStatements(conditional.ThenStatements, enumsByName, diagnostics);
                    if (conditional.ElseStatements is not null)
                    {
                        ValidateEnumStatements(conditional.ElseStatements, enumsByName, diagnostics);
                    }
                    break;
                case MirWhileStatement loop:
                    ValidateEnumExpression(loop.Condition, enumsByName, diagnostics);
                    ValidateEnumStatements(loop.BodyStatements, enumsByName, diagnostics);
                    break;
                case MirForStatement loop:
                    if (loop.Initializer is not null)
                    {
                        ValidateEnumStatements([loop.Initializer], enumsByName, diagnostics);
                    }
                    if (loop.Condition is not null)
                    {
                        ValidateEnumExpression(loop.Condition, enumsByName, diagnostics);
                    }
                    if (loop.Increment is not null)
                    {
                        ValidateEnumExpression(loop.Increment, enumsByName, diagnostics);
                    }
                    ValidateEnumStatements(loop.BodyStatements, enumsByName, diagnostics);
                    break;
            }
        }
    }

    private static void ValidateEnumExpression(
        MirExpression expression,
        IReadOnlyDictionary<string, MirEnum> enumsByName,
        List<MirValidationDiagnostic> diagnostics)
    {
        switch (expression)
        {
            case MirAssignmentExpression assignment:
                ValidateEnumExpression(assignment.Expression, enumsByName, diagnostics);
                return;
            case MirUnaryExpression unary:
                ValidateEnumExpression(unary.Operand, enumsByName, diagnostics);
                return;
            case MirBinaryExpression binary:
                ValidateEnumExpression(binary.Left, enumsByName, diagnostics);
                ValidateEnumExpression(binary.Right, enumsByName, diagnostics);
                return;
            case MirCallExpression call:
                foreach (MirExpression argument in call.Arguments)
                {
                    ValidateEnumExpression(argument, enumsByName, diagnostics);
                }
                return;
            case MirArrayExpression array:
                foreach (MirExpression element in array.Elements)
                {
                    ValidateEnumExpression(element, enumsByName, diagnostics);
                }
                return;
            case MirRecordConstructionExpression record:
                foreach (MirRecordFieldValue initializer in record.Initializers)
                {
                    ValidateEnumExpression(initializer.Value, enumsByName, diagnostics);
                }
                return;
            case MirRecordFieldAccessExpression access:
                ValidateEnumExpression(access.Receiver, enumsByName, diagnostics);
                return;
            case MirRecordWithExpression update:
                ValidateEnumExpression(update.Source, enumsByName, diagnostics);
                foreach (MirRecordFieldValue replacement in update.Replacements)
                {
                    ValidateEnumExpression(replacement.Value, enumsByName, diagnostics);
                }
                return;
            case MirEnumValueExpression value:
                ValidateEnumValueExpression(value, enumsByName, diagnostics);
                foreach (MirExpression argument in value.Arguments)
                {
                    ValidateEnumExpression(argument, enumsByName, diagnostics);
                }
                return;
            case MirMatchExpression match:
                ValidateEnumExpression(match.Scrutinee, enumsByName, diagnostics);
                ValidateMatchExpression(match, enumsByName, diagnostics);
                foreach (MirMatchArm arm in match.Arms)
                {
                    ValidateEnumExpression(arm.Expression, enumsByName, diagnostics);
                }
                return;
            case MirResultMatchExpression match:
                ValidateEnumExpression(match.Scrutinee, enumsByName, diagnostics);
                ValidateEnumExpression(match.OkExpression, enumsByName, diagnostics);
                ValidateEnumExpression(match.ErrExpression, enumsByName, diagnostics);
                return;
            case MirIfExpression conditional:
                ValidateEnumExpression(conditional.Condition, enumsByName, diagnostics);
                ValidateEnumExpression(conditional.ThenExpression, enumsByName, diagnostics);
                ValidateEnumExpression(conditional.ElseExpression, enumsByName, diagnostics);
                return;
            case MirOkExpression ok:
                ValidateEnumExpression(ok.Payload, enumsByName, diagnostics);
                return;
            case MirErrExpression err:
                ValidateEnumExpression(err.Payload, enumsByName, diagnostics);
                return;
            case MirPropagateExpression propagate:
                ValidateEnumExpression(propagate.Operand, enumsByName, diagnostics);
                return;
            case MirUnwrapExpression unwrap:
                ValidateEnumExpression(unwrap.Operand, enumsByName, diagnostics);
                return;
            case MirTryExpression tryExpression:
                ValidateEnumStatements(tryExpression.Protected.PrefixStatements, enumsByName, diagnostics);
                ValidateEnumExpression(tryExpression.Protected.ValueExpression, enumsByName, diagnostics);
                ValidateEnumExpression(tryExpression.Handler.ValueExpression, enumsByName, diagnostics);
                return;
            default:
                return;
        }
    }

    private static void ValidateEnumValueExpression(
        MirEnumValueExpression value,
        IReadOnlyDictionary<string, MirEnum> enumsByName,
        List<MirValidationDiagnostic> diagnostics)
    {
        if (!enumsByName.TryGetValue(value.EnumName, out MirEnum? @enum))
        {
            diagnostics.Add(new MirValidationDiagnostic($"unknown enum '{value.EnumName}' for enum value"));
            return;
        }

        if (value.Type.Identifier != @enum.Name)
        {
            diagnostics.Add(new MirValidationDiagnostic($"enum value '{@enum.Name}.{value.CaseName}' result type does not match enum '{@enum.Name}'."));
        }

        MirEnumCase? @case = @enum.Cases.FirstOrDefault(candidate => candidate.Name == value.CaseName);
        if (@case is null)
        {
            diagnostics.Add(new MirValidationDiagnostic($"unknown case '{value.EnumName}.{value.CaseName}'"));
            return;
        }

        ValidatePayloadArguments(value.Arguments, @case.PayloadFields, $"enum value '{value.EnumName}.{value.CaseName}'", diagnostics);
    }

    private static void ValidateMatchExpression(
        MirMatchExpression match,
        IReadOnlyDictionary<string, MirEnum> enumsByName,
        List<MirValidationDiagnostic> diagnostics)
    {
        if (match.Scrutinee.Type is not MirType scrutineeType
            || match.Scrutinee.Type is MirArrayType or MirResultType
            || !enumsByName.TryGetValue(scrutineeType.Identifier, out MirEnum? @enum))
        {
            diagnostics.Add(new MirValidationDiagnostic($"match expression has non-enum scrutinee type '{match.Scrutinee.Type.Name}'."));
            return;
        }

        var seenCases = new HashSet<string>(StringComparer.Ordinal);
        foreach (MirMatchArm arm in match.Arms)
        {
            if (!seenCases.Add(arm.CaseName))
            {
                diagnostics.Add(new MirValidationDiagnostic($"duplicate match arm '{arm.CaseName}'"));
            }

            MirEnumCase? @case = @enum.Cases.FirstOrDefault(candidate => candidate.Name == arm.CaseName);
            if (@case is null)
            {
                diagnostics.Add(new MirValidationDiagnostic($"unknown match case '{arm.CaseName}' for enum '{@enum.Name}'"));
            }
            else
            {
                ValidatePayloadBindings(arm.PayloadBindings, @case.PayloadFields, $"match arm '{arm.CaseName}'", diagnostics);
            }

            if (!MirTypeFacts.AreEquivalent(arm.Expression.Type, match.Type))
            {
                diagnostics.Add(new MirValidationDiagnostic($"result of match arm '{arm.CaseName}' does not match the match result type."));
            }
        }

        foreach (MirEnumCase @case in @enum.Cases)
        {
            if (!seenCases.Contains(@case.Name))
            {
                diagnostics.Add(new MirValidationDiagnostic($"non-exhaustive match for enum '{@enum.Name}'; missing case '{@case.Name}'"));
            }
        }
    }

    private static void ValidatePayloadArguments(
        IReadOnlyList<MirExpression> arguments,
        IReadOnlyList<MirEnumPayloadField> fields,
        string context,
        List<MirValidationDiagnostic> diagnostics)
    {
        if (arguments.Count != fields.Count)
        {
            diagnostics.Add(new MirValidationDiagnostic($"{context} has {arguments.Count} payloads but case declares {fields.Count}"));
        }

        int sharedCount = Math.Min(arguments.Count, fields.Count);
        for (int index = 0; index < sharedCount; index++)
        {
            if (!MirTypeFacts.AreEquivalent(arguments[index].Type, fields[index].Type))
            {
                diagnostics.Add(new MirValidationDiagnostic($"payload {index + 1} of {context} does not match the declared payload type."));
            }
        }
    }

    private static void ValidatePayloadBindings(
        IReadOnlyList<MirMatchPayloadBinding> bindings,
        IReadOnlyList<MirEnumPayloadField> fields,
        string context,
        List<MirValidationDiagnostic> diagnostics)
    {
        if (bindings.Count != fields.Count)
        {
            diagnostics.Add(new MirValidationDiagnostic($"{context} has {bindings.Count} bindings but case declares {fields.Count} payloads"));
        }

        int sharedCount = Math.Min(bindings.Count, fields.Count);
        for (int index = 0; index < sharedCount; index++)
        {
            if (!MirTypeFacts.AreEquivalent(bindings[index].Type, fields[index].Type))
            {
                diagnostics.Add(new MirValidationDiagnostic($"binding {index + 1} of {context} does not match the declared payload type."));
            }
        }
    }

    private static void ValidateRecordExpression(MirExpression expression, IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records, List<MirValidationDiagnostic> diagnostics)
    {
        ValidateRecordTypeReference(expression.Type, records, diagnostics, "expression");
        switch (expression)
        {
            case MirRecordConstructionExpression construction:
                ValidateRecordFieldValues(construction.RecordTypeId, construction.Initializers, records, diagnostics, requireComplete: true, allowEmpty: true);
                if (construction.Type is not MirRecordType constructionType || constructionType.RecordTypeId != construction.RecordTypeId) diagnostics.Add(new MirValidationDiagnostic("Record construction result type does not match its record identity."));
                foreach (var value in construction.Initializers) ValidateRecordExpression(value.Value, records, diagnostics);
                return;
            case MirRecordFieldAccessExpression access:
                ValidateRecordExpression(access.Receiver, records, diagnostics);
                if (access.Receiver.Type is not MirRecordType receiverType || receiverType.RecordTypeId != access.RecordTypeId)
                    diagnostics.Add(new MirValidationDiagnostic("Record field access receiver type does not match its record identity."));
                if (!records.TryGetValue(access.RecordTypeId, out var accessRecord) || accessRecord.Fields.All(field => field.Id != access.FieldId))
                    diagnostics.Add(new MirValidationDiagnostic($"Record field access uses unknown field identity '{access.FieldId}'."));
                else
                {
                    var field = accessRecord.Fields.Single(candidate => candidate.Id == access.FieldId);
                    if (!MirTypeFacts.AreEquivalent(field.Type, access.Type)) diagnostics.Add(new MirValidationDiagnostic($"Record field access type does not match field '{access.FieldId}'."));
                }
                return;
            case MirRecordWithExpression withExpression:
                ValidateRecordExpression(withExpression.Source, records, diagnostics);
                if (withExpression.Source.Type is not MirRecordType sourceType || sourceType.RecordTypeId != withExpression.RecordTypeId || withExpression.Type is not MirRecordType resultType || resultType.RecordTypeId != withExpression.RecordTypeId)
                    diagnostics.Add(new MirValidationDiagnostic("Record 'with' source or result type does not match its record identity."));
                ValidateRecordFieldValues(withExpression.RecordTypeId, withExpression.Replacements, records, diagnostics, requireComplete: false, allowEmpty: false);
                foreach (var value in withExpression.Replacements) ValidateRecordExpression(value.Value, records, diagnostics);
                return;
            case MirAssignmentExpression assignment: ValidateRecordExpression(assignment.Expression, records, diagnostics); return;
            case MirUnaryExpression unary: ValidateRecordExpression(unary.Operand, records, diagnostics); return;
            case MirBinaryExpression binary: ValidateRecordExpression(binary.Left, records, diagnostics); ValidateRecordExpression(binary.Right, records, diagnostics); return;
            case MirCallExpression call: foreach (var item in call.Arguments) ValidateRecordExpression(item, records, diagnostics); return;
            case MirArrayExpression array: foreach (var item in array.Elements) ValidateRecordExpression(item, records, diagnostics); return;
            case MirEnumValueExpression value: foreach (var item in value.Arguments) ValidateRecordExpression(item, records, diagnostics); return;
            case MirMatchExpression match: ValidateRecordExpression(match.Scrutinee, records, diagnostics); foreach (var arm in match.Arms) ValidateRecordExpression(arm.Expression, records, diagnostics); return;
            case MirResultMatchExpression match: ValidateRecordExpression(match.Scrutinee, records, diagnostics); ValidateRecordExpression(match.OkExpression, records, diagnostics); ValidateRecordExpression(match.ErrExpression, records, diagnostics); return;
            case MirIfExpression conditional: ValidateRecordExpression(conditional.Condition, records, diagnostics); ValidateRecordExpression(conditional.ThenExpression, records, diagnostics); ValidateRecordExpression(conditional.ElseExpression, records, diagnostics); return;
            case MirOkExpression ok: ValidateRecordExpression(ok.Payload, records, diagnostics); return;
            case MirErrExpression err: ValidateRecordExpression(err.Payload, records, diagnostics); return;
            case MirPropagateExpression propagation: ValidateRecordExpression(propagation.Operand, records, diagnostics); return;
            case MirUnwrapExpression unwrap: ValidateRecordExpression(unwrap.Operand, records, diagnostics); return;
            case MirTryExpression tryExpression:
                ValidateRecordStatements(tryExpression.Protected.PrefixStatements, records, diagnostics);
                ValidateRecordExpression(tryExpression.Protected.ValueExpression, records, diagnostics);
                ValidateRecordStatements(tryExpression.Handler.PrefixStatements, records, diagnostics);
                ValidateRecordExpression(tryExpression.Handler.ValueExpression, records, diagnostics);
                return;
        }
    }

    private static void ValidateRecordFieldValues(MirRecordTypeId recordTypeId, IReadOnlyList<MirRecordFieldValue> values, IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records, List<MirValidationDiagnostic> diagnostics, bool requireComplete, bool allowEmpty)
    {
        if (!records.TryGetValue(recordTypeId, out var record))
        {
            diagnostics.Add(new MirValidationDiagnostic($"Record operation uses unknown record identity '{recordTypeId}'."));
            return;
        }
        if (!allowEmpty && values.Count == 0) diagnostics.Add(new MirValidationDiagnostic("Record 'with' replacements must not be empty."));
        var seen = new HashSet<MirRecordFieldId>();
        foreach (var value in values)
        {
            if (!seen.Add(value.FieldId)) diagnostics.Add(new MirValidationDiagnostic($"Record operation duplicates field identity '{value.FieldId}'."));
            var field = record.Fields.FirstOrDefault(candidate => candidate.Id == value.FieldId);
            if (field is null) diagnostics.Add(new MirValidationDiagnostic($"Record operation uses unknown field identity '{value.FieldId}'."));
            else if (!MirTypeFacts.AreEquivalent(field.Type, value.Value.Type)) diagnostics.Add(new MirValidationDiagnostic($"Record field value type does not match field '{value.FieldId}'."));
        }
        if (requireComplete)
        {
            foreach (var missing in record.Fields.Where(field => !seen.Contains(field.Id))) diagnostics.Add(new MirValidationDiagnostic($"Record construction is missing field identity '{missing.Id}'."));
        }
    }

    private static void ValidateStatements(IReadOnlyList<MirStatement> statements, List<HandlerScope> activeHandlers, HashSet<MirHandlerId> handlerIds, List<MirValidationDiagnostic> diagnostics, int loopDepth)
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
                    RequireBooleanCondition(conditional.Condition, "if statement", diagnostics);
                    ValidateStatements(conditional.ThenStatements, activeHandlers, handlerIds, diagnostics, loopDepth);
                    if (conditional.ElseStatements is not null)
                    {
                        ValidateStatements(conditional.ElseStatements, activeHandlers, handlerIds, diagnostics, loopDepth);
                    }
                    break;
                case MirWhileStatement loop:
                    ValidateExpression(loop.Condition, activeHandlers, handlerIds, diagnostics);
                    RequireBooleanCondition(loop.Condition, "while loop", diagnostics);
                    ValidateStatements(loop.BodyStatements, activeHandlers, handlerIds, diagnostics, loopDepth + 1);
                    break;
                case MirForStatement loop:
                    if (loop.Initializer is not null)
                    {
                        ValidateStatements([loop.Initializer], activeHandlers, handlerIds, diagnostics, loopDepth);
                    }
                    if (loop.Condition is not null)
                    {
                        ValidateExpression(loop.Condition, activeHandlers, handlerIds, diagnostics);
                        RequireBooleanCondition(loop.Condition, "for loop", diagnostics);
                    }
                    if (loop.Increment is not null)
                    {
                        ValidateExpression(loop.Increment, activeHandlers, handlerIds, diagnostics);
                    }
                    ValidateStatements(loop.BodyStatements, activeHandlers, handlerIds, diagnostics, loopDepth + 1);
                    break;
                case MirBreakStatement when loopDepth == 0:
                    diagnostics.Add(new MirValidationDiagnostic("Break statement is outside a loop."));
                    break;
                case MirContinueStatement when loopDepth == 0:
                    diagnostics.Add(new MirValidationDiagnostic("Continue statement is outside a loop."));
                    break;
            }
        }
    }

    private static void RequireBooleanCondition(MirExpression condition, string context, List<MirValidationDiagnostic> diagnostics)
    {
        if (condition.Type.Identifier != "boolean")
        {
            diagnostics.Add(new MirValidationDiagnostic($"{context} condition must have type 'boolean'."));
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
            case MirRecordConstructionExpression construction:
                foreach (var initializer in construction.Initializers) ValidateExpression(initializer.Value, activeHandlers, handlerIds, diagnostics);
                return;
            case MirRecordFieldAccessExpression access:
                ValidateExpression(access.Receiver, activeHandlers, handlerIds, diagnostics);
                return;
            case MirRecordWithExpression withExpression:
                ValidateExpression(withExpression.Source, activeHandlers, handlerIds, diagnostics);
                foreach (var replacement in withExpression.Replacements) ValidateExpression(replacement.Value, activeHandlers, handlerIds, diagnostics);
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
            case MirTsonEncodeExpression encode:
                ValidateExpression(encode.Operand, activeHandlers, handlerIds, diagnostics);
                return;
            case MirTsonTransportExpression transport:
                ValidateExpression(transport.Operation, activeHandlers, handlerIds, diagnostics);
                ValidateExpression(transport.Request, activeHandlers, handlerIds, diagnostics);
                return;
            case MirNpmCallExpression npm:
                foreach (MirExpression argument in npm.Arguments)
                {
                    ValidateExpression(argument, activeHandlers, handlerIds, diagnostics);
                }
                ValidateExpression(npm.ArgumentTuple, activeHandlers, handlerIds, diagnostics);
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

            ValidateStatements([statement], activeHandlers, handlerIds, diagnostics, loopDepth: 0);
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
                case MirWhileStatement loop:
                    ValidateFunctionPropagationTarget(loop.Condition, functionReturnType, diagnostics);
                    ValidateFunctionPropagationTargets(loop.BodyStatements, functionReturnType, diagnostics);
                    break;
                case MirForStatement loop:
                    if (loop.Initializer is not null)
                    {
                        ValidateFunctionPropagationTargets([loop.Initializer], functionReturnType, diagnostics);
                    }
                    if (loop.Condition is not null)
                    {
                        ValidateFunctionPropagationTarget(loop.Condition, functionReturnType, diagnostics);
                    }
                    if (loop.Increment is not null)
                    {
                        ValidateFunctionPropagationTarget(loop.Increment, functionReturnType, diagnostics);
                    }
                    ValidateFunctionPropagationTargets(loop.BodyStatements, functionReturnType, diagnostics);
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
            case MirRecordConstructionExpression construction:
                foreach (var initializer in construction.Initializers) ValidateFunctionPropagationTarget(initializer.Value, functionReturnType, diagnostics);
                return;
            case MirRecordFieldAccessExpression access:
                ValidateFunctionPropagationTarget(access.Receiver, functionReturnType, diagnostics);
                return;
            case MirRecordWithExpression withExpression:
                ValidateFunctionPropagationTarget(withExpression.Source, functionReturnType, diagnostics);
                foreach (var replacement in withExpression.Replacements) ValidateFunctionPropagationTarget(replacement.Value, functionReturnType, diagnostics);
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
            case MirTsonEncodeExpression encode:
                ValidateFunctionPropagationTarget(encode.Operand, functionReturnType, diagnostics);
                return;
            case MirTsonTransportExpression transport:
                ValidateFunctionPropagationTarget(transport.Operation, functionReturnType, diagnostics);
                ValidateFunctionPropagationTarget(transport.Request, functionReturnType, diagnostics);
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
