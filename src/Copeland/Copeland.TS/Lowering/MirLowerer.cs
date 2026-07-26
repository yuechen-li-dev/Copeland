using Copeland.TS.Mir;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;

namespace Copeland.TS.Lowering;

public static class MirLowerer
{
    public static MirCompilation Lower(SyntaxTree tree)
    {
        var bound = Binder.Bind(tree);
        return Lower(bound);
    }

    public static MirCompilation Lower(BoundCompilation bound)
    {
        if (bound.Diagnostics.Any(d => d.Id.StartsWith("COPE-", StringComparison.Ordinal)))
            return new MirCompilation(null, bound.Diagnostics);

        return new MirCompilation(LowerProgram(bound.Program), bound.Diagnostics);
    }

    public static MirProgram LowerProgram(BoundProgram program)
    {
        var enums = program.Enums.Select(LowerEnum).ToArray();
        var records = program.Records.Select(LowerRecord).ToArray();
        var tables = program.Tables.Select(LowerTable).ToArray();
        var tsonEncodingPlans = program.TsonEncodingPlans.Select(LowerTsonEncodingPlan).ToArray();
        var functions = program.Functions.Select(LowerFunction).ToArray();
        MirNpmImport[] npmImports = program.NpmImports.Select(import => new MirNpmImport(
            import.Function.PackageName,
            import.Function.PackageVersion,
            import.Function.ExportName,
            import.Function.Name,
            import.Function.IsPromise,
            import.Function.IsAvailableToJavaScript,
            import.Function.IsAvailableToClrSidecar)).ToArray();
        return new MirProgram(enums, records, tables, tsonEncodingPlans, npmImports, functions, program.CSharpUsings, program.CSharpSourcePath);
    }

    private static MirTsonEncodingPlan LowerTsonEncodingPlan(BoundTsonEncodingPlan plan)
    {
        MirTsonTablePlan? tablePlan = plan.TablePlan is null
            ? null
            : LowerTsonTablePlan(plan.TablePlan, plan.SchemaIdentity);
        return new MirTsonEncodingPlan(
            new MirTsonEncodingPlanId(plan.Id),
            plan.SchemaIdentity,
            ToMirType(plan.RootType),
            tablePlan is null
                ? LowerTsonValuePlan(plan.RootType)
                : new MirTsonTableValuePlan(tablePlan.TableId),
            plan.Definitions.Select(LowerTsonNominalPlan).ToArray(),
            new MirTsonEncodingLimits(1_048_576, 262_144),
            tablePlan);
    }

    private static MirTsonTablePlan LowerTsonTablePlan(BoundTsonTablePlan plan, string schemaIdentity)
    {
        var tableId = new MirTableId(plan.TableType.Id.ToString());
        return new MirTsonTablePlan(
            tableId,
            plan.TableType.Name,
            schemaIdentity + "#" + plan.TableType.Name,
            plan.ExpectedRowCount,
            plan.Columns.Select(column => new MirTsonTableColumnPlan(
                new MirTableColumnId(column.Column.Id.ToString()),
                column.Column.Name,
                schemaIdentity + "#" + plan.TableType.Name + "." + column.Column.Name,
                LowerTsonValuePlan(column.Column.Type),
                column.ExpectedElementCount)).ToArray());
    }

    private static MirTsonNominalPlan LowerTsonNominalPlan(TypeSymbol type)
    {
        return type switch
        {
            RecordTypeSymbol record => new MirTsonRecordPlan(
                ToMirRecordTypeId(record.Id),
                record.Name,
                record.StableIdentity ?? throw new InvalidOperationException("TSON record plan has no stable identity."),
                record.Fields.Select(field => new MirTsonRecordFieldPlan(
                    ToMirRecordFieldId(field.Id),
                    field.Name,
                    $"{record.StableIdentity}.{field.Name}",
                    LowerTsonValuePlan(field.Type))).ToArray()),
            EnumTypeSymbol @enum => new MirTsonEnumPlan(
                @enum.Name,
                @enum.StableIdentity ?? throw new InvalidOperationException("TSON enum plan has no stable identity."),
                @enum.Cases.Select(@case => new MirTsonEnumCasePlan(
                    @case.Name,
                    $"{@enum.StableIdentity}.{@case.Name}",
                    @case.PayloadFields.Select(field => new MirTsonEnumPayloadPlan(
                        field.Name,
                        $"{@enum.StableIdentity}.{@case.Name}.{field.Name}",
                        LowerTsonValuePlan(field.Type))).ToArray())).ToArray()),
            _ => throw new InvalidOperationException($"Unsupported TSON nominal plan type '{type.Name}'."),
        };
    }

    private static MirTsonValuePlan LowerTsonValuePlan(TypeSymbol type)
    {
        if (type == PrimitiveTypeSymbol.Boolean) return new MirTsonBooleanPlan();
        if (type == PrimitiveTypeSymbol.Number) return new MirTsonNumberPlan();
        if (type == PrimitiveTypeSymbol.String) return new MirTsonStringPlan();
        return type switch
        {
            RecordTypeSymbol record => new MirTsonRecordValuePlan(ToMirRecordTypeId(record.Id)),
            EnumTypeSymbol @enum => new MirTsonEnumValuePlan(@enum.Name),
            ArrayTypeSymbol array => new MirTsonArrayPlan(LowerTsonValuePlan(array.ElementType)),
            _ => throw new InvalidOperationException($"Unsupported TSON value plan type '{type.Name}'."),
        };
    }

    private static MirTableDefinition LowerTable(BoundTableDefinition definition)
        => new(
            new MirTableId(definition.TableType.Id.ToString()),
            definition.TableType.Name,
            definition.TableType.RowType.TableId + ".row",
            definition.Columns.Select(column => new MirTableColumnDefinition(
                new MirTableColumnId(column.Column.Id.ToString()),
                column.Column.Name,
                ToMirType(column.Column.Type),
                column.Cells.Select(LowerTableConstant).ToArray())).ToArray(),
            definition.RowCount);

    private static MirTableConstant LowerTableConstant(BoundTableConstant constant)
        => constant switch
        {
            BoundTableLiteralConstant literal => new MirTableLiteralConstant(literal.Value, ToMirType(literal.Type)),
            BoundTableArrayConstant array => new MirTableArrayConstant(
                (MirArrayType)ToMirType(array.ArrayType),
                array.Elements.Select(LowerTableConstant).ToArray()),
            BoundTableRecordConstant record => new MirTableRecordConstant(
                ToMirRecordTypeId(record.RecordType.Id),
                record.Fields.Select(field => new MirTableRecordFieldConstant(ToMirRecordFieldId(field.Field.Id), LowerTableConstant(field.Value))).ToArray(),
                ToMirType(record.Type)),
            BoundTableEnumConstant value => new MirTableEnumConstant(
                value.Case.EnumType.Name,
                value.Case.Name,
                value.Payloads.Select(LowerTableConstant).ToArray(),
                ToMirType(value.Type)),
            BoundTableResultConstant result => new MirTableResultConstant(
                result.IsOk,
                LowerTableConstant(result.Payload),
                (MirResultType)ToMirType(result.Type)),
            _ => throw new InvalidOperationException($"Unsupported table constant {constant.GetType().Name}."),
        };

    private static MirRecordDefinition LowerRecord(BoundRecordDeclaration declaration)
    {
        var recordType = declaration.RecordType;
        return new MirRecordDefinition(
            ToMirRecordTypeId(recordType.Id),
            recordType.Name,
            recordType.Fields.Select(field => new MirRecordFieldDefinition(
                ToMirRecordFieldId(field.Id),
                field.Name,
                ToMirType(field.Type),
                field.IsPublic)).ToArray(),
            recordType is ClassTypeSymbol);
    }

    private static MirEnum LowerEnum(BoundEnumDeclaration declaration)
        => new(
            declaration.EnumType.Name,
            declaration.EnumType.Cases.Select(@case =>
                new MirEnumCase(
                    @case.Name,
                    @case.PayloadFields.Select(field => new MirEnumPayloadField(field.Name, ToMirType(field.Type))).ToArray()))
                .ToArray());

    private static MirFunction LowerFunction(BoundFunctionDeclaration function)
    {
        var locals = new Dictionary<string, MirLocal>(StringComparer.Ordinal);
        var body = LowerStatements(function.Body.Statements, locals);
        MirSuspensionAutomaton? automaton = function.Symbol.IsAsync
            ? BuildSuspensionAutomaton(function.Symbol.Name, function.Symbol.Parameters, locals.Values, body, ToMirType(function.Symbol.ReturnType))
            : null;
        return new MirFunction(
            function.Symbol.Name,
            function.Symbol.Parameters.Select(p => new MirParameter(p.Name, ToMirType(p.Type))).ToArray(),
            ToMirType(function.Symbol.ReturnType),
            locals.Values.OrderBy(l => l.Name, StringComparer.Ordinal).ToArray(),
            body,
            function.Symbol.IsAsync,
            automaton);
    }

    /// <summary>
    /// Creates the backend-neutral control-flow skeleton used by the async
    /// emitters.  The authored statements remain structured MIR; the machine
    /// records every real suspension boundary and every value which must have
    /// frame storage when execution resumes.
    /// </summary>
    private static MirSuspensionAutomaton BuildSuspensionAutomaton(
        string functionName,
        IReadOnlyList<ParameterSymbol> parameters,
        IEnumerable<MirLocal> locals,
        IReadOnlyList<MirStatement> body,
        MirType returnType)
    {
        var frameSlots = new List<MirFrameSlot>();
        foreach (ParameterSymbol parameter in parameters)
        {
            frameSlots.Add(new MirFrameSlot(
                new MirFrameSlotId("parameter_" + parameter.Name),
                ToMirType(parameter.Type),
                "parameter " + parameter.Name,
                isReadOnly: true));
        }

        foreach (MirLocal local in locals.OrderBy(local => local.Name, StringComparer.Ordinal))
        {
            frameSlots.Add(new MirFrameSlot(
                new MirFrameSlotId("local_" + local.Name),
                local.Type,
                "local " + local.Name,
                local.IsReadOnly));
        }

        MirAwaitExpression[] awaits = EnumerateAwaits(body).ToArray();
        var awaitSlots = new Dictionary<MirAwaitExpression, MirFrameSlotId>(ReferenceEqualityComparer.Instance);
        var awaitValueSlots = new Dictionary<MirAwaitExpression, MirFrameSlotId>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < awaits.Length; index++)
        {
            MirFrameSlotId slotId = new("await_" + index);
            MirFrameSlotId valueSlotId = new("await_value_" + index);
            awaitSlots.Add(awaits[index], slotId);
            awaitValueSlots.Add(awaits[index], valueSlotId);
            frameSlots.Add(new MirFrameSlot(
                slotId,
                awaits[index].Operand.Type,
                "await operand " + index,
                isReadOnly: true));
            frameSlots.Add(new MirFrameSlot(
                valueSlotId,
                awaits[index].Type,
                "await result " + index));
        }

        var entry = new MirAutomatonStateId("entry");
        var complete = new MirAutomatonStateId("complete");
        var cancelled = new MirAutomatonStateId("cancelled");
        var states = new List<MirAutomatonState>
        {
            new MirTerminalAutomatonState(entry, MirAutomatonStateKind.Entry, "function entry"),
            new MirCompletionAutomatonState(complete, "function completion", returnType),
        };
        if (awaits.Length > 0)
        {
            states.Add(new MirTerminalAutomatonState(cancelled, MirAutomatonStateKind.Cancelled, "cancellation"));
        }
        var transitions = new List<MirAutomatonTransition>();
        MirAutomatonStateId previous = entry;
        for (int index = 0; index < awaits.Length; index++)
        {
            var suspension = new MirAutomatonStateId("await_" + index);
            var resumed = new MirAutomatonStateId("resume_" + index);
            states.Add(new MirAwaitSuspensionAutomatonState(
                suspension,
                "await " + index,
                new MirFrameSlotId("await_" + index),
                awaits[index].Type));
            states.Add(new MirExecutionAutomatonState(
                resumed,
                "resume " + index,
                frameSlots.Select(slot => slot.Id).ToArray(),
                []));
            transitions.Add(new MirAutomatonTransition(previous, suspension, MirAutomatonTransitionKind.Unconditional, "reach await " + index));
            transitions.Add(new MirAutomatonTransition(suspension, resumed, MirAutomatonTransitionKind.ResumeSuccess, "resume await " + index));
            transitions.Add(new MirAutomatonTransition(suspension, cancelled, MirAutomatonTransitionKind.Cancellation, "cancel await " + index));
            previous = resumed;
        }

        transitions.Add(new MirAutomatonTransition(previous, complete, MirAutomatonTransitionKind.Unconditional, "complete function"));
        return new MirSuspensionAutomaton(
            "async_" + functionName,
            functionName,
            entry,
            frameSlots,
            states,
            transitions,
            BuildAsyncExecutionPlan(body, awaitSlots, awaitValueSlots, frameSlots));
    }

    private static MirAsyncExecutionPlan BuildAsyncExecutionPlan(
        IReadOnlyList<MirStatement> body,
        IReadOnlyDictionary<MirAwaitExpression, MirFrameSlotId> awaitSlots,
        IReadOnlyDictionary<MirAwaitExpression, MirFrameSlotId> awaitValueSlots,
        ICollection<MirFrameSlot> frameSlots)
    {
        var states = new List<MirAsyncExecutionState>();
        var frameVariables = new Dictionary<string, MirFrameSlotId>(StringComparer.Ordinal);
        var activeHandlers = new Dictionary<MirHandlerId, AsyncHandlerContext>();
        int nextId = 0;
        int nextTemporarySlot = 0;

        MirAsyncExecutionStateId Add(MirAsyncExecutionState state)
        {
            states.Add(state);
            return state.Id;
        }

        MirAsyncExecutionStateId NewId() => new("exec_" + nextId++);

        MirFrameSlotId NewTemporarySlot(MirType type)
        {
            MirFrameSlotId id = new("expression_" + nextTemporarySlot++);
            frameSlots.Add(new MirFrameSlot(id, type, "async expression temporary " + id.Value));
            return id;
        }

        MirAsyncExecutionStateId AddFrameEvaluation(
            MirFrameSlotId targetSlot,
            MirExpression expression,
            MirAsyncExecutionStateId nextStateId)
        {
            MirAsyncExecutionStateId id = NewId();
            Add(new MirAsyncEvaluateExpressionState(id, targetSlot, expression, nextStateId));
            return id;
        }

        MirAsyncExecutionStateId AddStatement(MirStatement statement, MirAsyncExecutionStateId nextStateId)
        {
            MirAsyncExecutionStateId id = NewId();
            Add(new MirAsyncStatementExecutionState(id, statement, nextStateId));
            return id;
        }

        MirAsyncExecutionStateId AddReturn(MirReturnStatement statement)
        {
            MirAsyncExecutionStateId id = NewId();
            Add(new MirAsyncReturnExecutionState(id, statement));
            return id;
        }

        MirAsyncExecutionStateId LowerArguments(
            IReadOnlyList<MirExpression> arguments,
            int index,
            List<MirExpression> lowered,
            Func<IReadOnlyList<MirExpression>, MirAsyncExecutionStateId> continuation)
        {
            if (index == arguments.Count)
            {
                return continuation(lowered);
            }

            return LowerExpression(arguments[index], value =>
            {
                var nextLowered = new List<MirExpression>(lowered.Count + 1);
                nextLowered.AddRange(lowered);
                nextLowered.Add(value);
                return LowerArguments(arguments, index + 1, nextLowered, continuation);
            });
        }

        MirAsyncExecutionStateId LowerShortCircuit(
            MirBinaryExpression binary,
            Func<MirExpression, MirAsyncExecutionStateId> continuation)
        {
            return LowerExpression(binary.Left, left =>
            {
                MirFrameSlotId resultSlot = NewTemporarySlot(binary.Type);
                var result = new MirAsyncFrameSlotExpression(resultSlot, binary.Type);
                MirAsyncExecutionStateId nextState = continuation(result);
                MirExpression shortCircuitValue = new MirLiteralExpression(binary.Operator == "||", binary.Type);
                MirAsyncExecutionStateId shortCircuitState = NewId();
                Add(new MirAsyncEvaluateExpressionState(shortCircuitState, resultSlot, shortCircuitValue, nextState));
                MirAsyncExecutionStateId rightState = LowerExpression(binary.Right, right =>
                {
                    MirAsyncExecutionStateId evaluation = NewId();
                    Add(new MirAsyncEvaluateExpressionState(evaluation, resultSlot, right, nextState));
                    return evaluation;
                });
                MirAsyncExecutionStateId branch = NewId();
                bool evaluatesRightWhenTrue = binary.Operator == "&&";
                Add(new MirAsyncBranchExecutionState(
                    branch,
                    left,
                    evaluatesRightWhenTrue ? rightState : shortCircuitState,
                    evaluatesRightWhenTrue ? shortCircuitState : rightState));
                return branch;
            });
        }

        MirAsyncExecutionStateId LowerIfExpression(
            MirIfExpression conditional,
            Func<MirExpression, MirAsyncExecutionStateId> continuation)
        {
            return LowerExpression(conditional.Condition, condition =>
            {
                MirFrameSlotId resultSlot = NewTemporarySlot(conditional.Type);
                var result = new MirAsyncFrameSlotExpression(resultSlot, conditional.Type);
                MirAsyncExecutionStateId nextState = continuation(result);
                MirAsyncExecutionStateId thenState = LowerExpression(conditional.ThenExpression, value =>
                {
                    MirAsyncExecutionStateId evaluation = NewId();
                    Add(new MirAsyncEvaluateExpressionState(evaluation, resultSlot, value, nextState));
                    return evaluation;
                });
                MirAsyncExecutionStateId elseState = LowerExpression(conditional.ElseExpression, value =>
                {
                    MirAsyncExecutionStateId evaluation = NewId();
                    Add(new MirAsyncEvaluateExpressionState(evaluation, resultSlot, value, nextState));
                    return evaluation;
                });
                MirAsyncExecutionStateId branch = NewId();
                Add(new MirAsyncBranchExecutionState(branch, condition, thenState, elseState));
                return branch;
            });
        }

        MirAsyncExecutionStateId LowerExpression(
            MirExpression expression,
            Func<MirExpression, MirAsyncExecutionStateId> continuation)
        {
            return expression switch
            {
                MirAwaitExpression awaited => LowerExpression(awaited.Operand, operand =>
                {
                    MirAsyncExecutionStateId nextState = continuation(new MirAsyncFrameSlotExpression(awaitValueSlots[awaited], awaited.Type));
                    MirAsyncExecutionStateId id = NewId();
                    Add(new MirAsyncAwaitExecutionState(id, operand, awaitSlots[awaited], awaitValueSlots[awaited], nextState));
                    return id;
                }),
                MirVariableExpression variable when frameVariables.TryGetValue(variable.Name, out MirFrameSlotId slot) =>
                    continuation(new MirAsyncFrameSlotExpression(slot, variable.Type)),
                MirAssignmentExpression assignment when frameVariables.TryGetValue(assignment.Name, out MirFrameSlotId assignmentSlot) =>
                    LowerExpression(assignment.Expression, value =>
                    {
                        MirAsyncExecutionStateId nextState = continuation(new MirAsyncFrameSlotExpression(assignmentSlot, assignment.Type));
                        return AddFrameEvaluation(assignmentSlot, value, nextState);
                    }),
                MirAssignmentExpression assignment => LowerExpression(
                    assignment.Expression,
                    value => continuation(new MirAssignmentExpression(assignment.Name, value, assignment.Type))),
                MirUnaryExpression unary => LowerExpression(
                    unary.Operand,
                    value => continuation(new MirUnaryExpression(unary.Operator, value, unary.Type))),
                MirBinaryExpression { Operator: "&&" or "||" } binary => LowerShortCircuit(binary, continuation),
                MirBinaryExpression binary => LowerExpression(binary.Left, left => LowerExpression(
                    binary.Right,
                    right => continuation(new MirBinaryExpression(binary.Operator, left, right, binary.Type)))),
                MirCallExpression call => LowerArguments(call.Arguments, 0, [], arguments =>
                    continuation(new MirCallExpression(call.FunctionName, arguments, call.Type))),
                MirTsonTransportExpression transport => LowerExpression(transport.Operation, operation =>
                    LowerExpression(transport.Request, request => continuation(new MirTsonTransportExpression(
                        operation,
                        request,
                        transport.RequestPlanId,
                        transport.ResponsePlanId,
                        transport.RemoteErrorPlanId,
                        transport.AsyncType)))),
                MirCallableConstructionExpression construction => LowerArguments(construction.Captures, 0, [], captures =>
                    continuation(new MirCallableConstructionExpression(construction.CodeFunctionName, captures, construction.CallableType))),
                MirInvokeExpression invoke => LowerExpression(invoke.Callee, callee => LowerArguments(invoke.Arguments, 0, [], arguments =>
                    continuation(new MirInvokeExpression(callee, arguments, invoke.Type)))),
                MirArrayExpression array => LowerArguments(array.Elements, 0, [], elements =>
                    continuation(new MirArrayExpression(elements, array.Type))),
                MirOkExpression ok => LowerExpression(ok.Payload, value =>
                    continuation(new MirOkExpression(value, (MirResultType)ok.Type))),
                MirErrExpression err => LowerExpression(err.Payload, value =>
                    continuation(new MirErrExpression(value, (MirResultType)err.Type))),
                MirPropagateExpression propagation => LowerExpression(propagation.Operand, result =>
                {
                    MirFrameSlotId successSlot = NewTemporarySlot(propagation.Type);
                    MirAsyncExecutionStateId nextState = continuation(new MirAsyncFrameSlotExpression(successSlot, propagation.Type));
                    MirAsyncExecutionStateId id = NewId();
                    if (propagation.Target is MirPropagationTarget.LexicalExcept lexical)
                    {
                        if (!activeHandlers.TryGetValue(lexical.HandlerId, out AsyncHandlerContext? handler))
                        {
                            throw new InvalidOperationException($"Async propagation target '{lexical.HandlerId}' is not active during executable lowering.");
                        }

                        Add(new MirAsyncPropagateExecutionState(
                            id,
                            result,
                            propagation.Target,
                            successSlot,
                            nextState,
                            handler.EntryStateId,
                            handler.ErrorSlot));
                    }
                    else
                    {
                        Add(new MirAsyncPropagateExecutionState(id, result, propagation.Target, successSlot, nextState));
                    }
                    return id;
                }),
                MirIfExpression conditional => LowerIfExpression(conditional, continuation),
                MirTryExpression attempt => LowerTryExpression(attempt, continuation),
                _ => continuation(expression),
            };
        }

        MirAsyncExecutionStateId LowerValueBlock(
            MirValueBlock block,
            Func<MirExpression, MirAsyncExecutionStateId> continuation)
        {
            var previousBindings = new List<(string Name, MirFrameSlotId? Previous)>();
            foreach (MirVariableDeclarationStatement declaration in block.PrefixStatements.OfType<MirVariableDeclarationStatement>())
            {
                MirFrameSlotId? previous = frameVariables.TryGetValue(declaration.Local.Name, out MirFrameSlotId existing)
                    ? existing
                    : null;
                previousBindings.Add((declaration.Local.Name, previous));
                frameVariables[declaration.Local.Name] = NewTemporarySlot(declaration.Local.Type);
            }

            MirAsyncExecutionStateId current = LowerExpression(block.ValueExpression, continuation);
            for (int index = block.PrefixStatements.Count - 1; index >= 0; index--)
            {
                MirStatement prefix = block.PrefixStatements[index];
                MirAsyncExecutionStateId nextState = current;
                current = prefix switch
                {
                    MirVariableDeclarationStatement declaration => LowerExpression(
                        declaration.Initializer,
                        value => AddFrameEvaluation(frameVariables[declaration.Local.Name], value, nextState)),
                    MirExpressionStatement expression => LowerExpression(
                        expression.Expression,
                        value => AddStatement(new MirExpressionStatement(value), nextState)),
                    _ => throw new InvalidOperationException($"Async try value block contains unsupported prefix statement '{prefix.GetType().Name}'."),
                };
            }

            for (int index = previousBindings.Count - 1; index >= 0; index--)
            {
                (string name, MirFrameSlotId? previous) = previousBindings[index];
                if (previous is { } previousSlot)
                {
                    frameVariables[name] = previousSlot;
                }
                else
                {
                    frameVariables.Remove(name);
                }
            }

            return current;
        }

        MirAsyncExecutionStateId LowerTryExpression(
            MirTryExpression attempt,
            Func<MirExpression, MirAsyncExecutionStateId> continuation)
        {
            MirFrameSlotId resultSlot = NewTemporarySlot(attempt.Type);
            MirFrameSlotId errorSlot = NewTemporarySlot(attempt.HandledErrorType);
            var result = new MirAsyncFrameSlotExpression(resultSlot, attempt.Type);
            MirAsyncExecutionStateId nextState = continuation(result);
            MirAsyncExecutionStateId handlerEntry = NewId();

            bool hadPriorHandler = activeHandlers.TryGetValue(attempt.HandlerId, out AsyncHandlerContext? priorHandler);
            activeHandlers[attempt.HandlerId] = new AsyncHandlerContext(handlerEntry, errorSlot);
            MirAsyncExecutionStateId protectedEntry = LowerValueBlock(
                attempt.Protected,
                value => AddFrameEvaluation(resultSlot, value, nextState));
            if (hadPriorHandler)
            {
                activeHandlers[attempt.HandlerId] = priorHandler!;
            }
            else
            {
                activeHandlers.Remove(attempt.HandlerId);
            }

            MirFrameSlotId? previousBinding = frameVariables.TryGetValue(attempt.HandlerBinding.Name, out MirFrameSlotId previousSlot)
                ? previousSlot
                : null;
            frameVariables[attempt.HandlerBinding.Name] = errorSlot;
            MirAsyncExecutionStateId handlerBody = LowerValueBlock(
                attempt.Handler,
                value => AddFrameEvaluation(resultSlot, value, nextState));
            if (previousBinding is { } previous)
            {
                frameVariables[attempt.HandlerBinding.Name] = previous;
            }
            else
            {
                frameVariables.Remove(attempt.HandlerBinding.Name);
            }

            Add(new MirAsyncJumpExecutionState(handlerEntry, handlerBody));
            return protectedEntry;
        }

        MirAsyncExecutionStateId LowerStatement(MirStatement statement, MirAsyncExecutionStateId nextStateId)
        {
            return statement switch
            {
                MirVariableDeclarationStatement declaration => LowerExpression(
                    declaration.Initializer,
                    value => AddStatement(new MirVariableDeclarationStatement(declaration.Local, value), nextStateId)),
                MirExpressionStatement expression => LowerExpression(
                    expression.Expression,
                    value => AddStatement(new MirExpressionStatement(value), nextStateId)),
                MirReturnStatement { Expression: not null } returned => LowerExpression(
                    returned.Expression,
                    value => AddReturn(new MirReturnStatement(value))),
                MirReturnStatement returned => AddReturn(returned),
                _ => AddStatement(statement, nextStateId),
            };
        }

        MirAsyncExecutionStateId Build(IReadOnlyList<MirStatement> statements, MirAsyncExecutionStateId continuation, MirAsyncExecutionStateId? breakTarget = null, MirAsyncExecutionStateId? continueTarget = null)
        {
            MirAsyncExecutionStateId current = continuation;
            for (int index = statements.Count - 1; index >= 0; index--)
            {
                MirStatement statement = statements[index];
                switch (statement)
                {
                    case MirReturnStatement returned:
                    {
                        current = LowerStatement(returned, current);
                        break;
                    }
                    case MirIfStatement conditional:
                    {
                        MirAsyncExecutionStateId thenState = Build(conditional.ThenStatements, current, breakTarget, continueTarget);
                        MirAsyncExecutionStateId elseState = conditional.ElseStatements is null
                            ? current
                            : Build(conditional.ElseStatements, current, breakTarget, continueTarget);
                        current = LowerExpression(conditional.Condition, condition =>
                        {
                            MirAsyncExecutionStateId id = NewId();
                            Add(new MirAsyncBranchExecutionState(id, condition, thenState, elseState));
                            return id;
                        });
                        break;
                    }
                    case MirWhileStatement loop:
                    {
                        MirAsyncExecutionStateId conditionEntry = NewId();
                        MirAsyncExecutionStateId bodyState = Build(loop.BodyStatements, conditionEntry, current, conditionEntry);
                        MirAsyncExecutionStateId conditionState = LowerExpression(loop.Condition, condition =>
                        {
                            MirAsyncExecutionStateId branch = NewId();
                            Add(new MirAsyncBranchExecutionState(branch, condition, bodyState, current));
                            return branch;
                        });
                        Add(new MirAsyncJumpExecutionState(conditionEntry, conditionState));
                        current = conditionEntry;
                        break;
                    }
                    case MirForStatement loop:
                    {
                        MirExpression condition = loop.Condition ?? new MirLiteralExpression(true, new MirNamedType("boolean"));
                        MirAsyncExecutionStateId conditionEntry = NewId();
                        MirAsyncExecutionStateId incrementId = conditionEntry;
                        if (loop.Increment is not null)
                        {
                            incrementId = LowerStatement(new MirExpressionStatement(loop.Increment), conditionEntry);
                        }
                        MirAsyncExecutionStateId bodyState = Build(loop.BodyStatements, incrementId, current, incrementId);
                        MirAsyncExecutionStateId conditionState = LowerExpression(condition, value =>
                        {
                            MirAsyncExecutionStateId branch = NewId();
                            Add(new MirAsyncBranchExecutionState(branch, value, bodyState, current));
                            return branch;
                        });
                        Add(new MirAsyncJumpExecutionState(conditionEntry, conditionState));
                        current = loop.Initializer is null
                            ? conditionEntry
                            : Build([loop.Initializer], conditionEntry, breakTarget, continueTarget);
                        break;
                    }
                    case MirBreakStatement:
                    {
                        MirAsyncExecutionStateId id = NewId();
                        Add(new MirAsyncJumpExecutionState(id, breakTarget ?? current));
                        current = id;
                        break;
                    }
                    case MirContinueStatement:
                    {
                        MirAsyncExecutionStateId id = NewId();
                        Add(new MirAsyncJumpExecutionState(id, continueTarget ?? current));
                        current = id;
                        break;
                    }
                    default:
                    {
                        current = LowerStatement(statement, current);
                        break;
                    }
                }
            }
            return current;
        }

        MirAsyncExecutionStateId terminal = NewId();
        Add(new MirAsyncReturnExecutionState(terminal, new MirReturnStatement(null)));
        MirAsyncExecutionStateId entry = Build(body, terminal);
        return new MirAsyncExecutionPlan(entry, states);
    }

    private sealed class AsyncHandlerContext(MirAsyncExecutionStateId entryStateId, MirFrameSlotId errorSlot)
    {
        public MirAsyncExecutionStateId EntryStateId { get; } = entryStateId;
        public MirFrameSlotId ErrorSlot { get; } = errorSlot;
    }

    private static IEnumerable<MirAwaitExpression> EnumerateAwaits(IEnumerable<MirStatement> statements)
    {
        foreach (MirStatement statement in statements)
        {
            foreach (MirExpression expression in EnumerateStatementExpressions(statement))
            {
                foreach (MirAwaitExpression awaitExpression in EnumerateAwaits(expression))
                {
                    yield return awaitExpression;
                }
            }
        }
    }

    private static IEnumerable<MirExpression> EnumerateStatementExpressions(MirStatement statement)
    {
        switch (statement)
        {
            case MirVariableDeclarationStatement declaration:
                yield return declaration.Initializer;
                break;
            case MirExpressionStatement expression:
                yield return expression.Expression;
                break;
            case MirReturnStatement { Expression: not null } returned:
                yield return returned.Expression;
                break;
            case MirIfStatement conditional:
                yield return conditional.Condition;
                foreach (MirExpression nested in EnumerateStatements(conditional.ThenStatements)) yield return nested;
                if (conditional.ElseStatements is not null) foreach (MirExpression nested in EnumerateStatements(conditional.ElseStatements)) yield return nested;
                break;
            case MirWhileStatement loop:
                yield return loop.Condition;
                foreach (MirExpression nested in EnumerateStatements(loop.BodyStatements)) yield return nested;
                break;
            case MirForStatement loop:
                if (loop.Initializer is not null) foreach (MirExpression nested in EnumerateStatementExpressions(loop.Initializer)) yield return nested;
                if (loop.Condition is not null) yield return loop.Condition;
                if (loop.Increment is not null) yield return loop.Increment;
                foreach (MirExpression nested in EnumerateStatements(loop.BodyStatements)) yield return nested;
                break;
        }
    }

    private static IEnumerable<MirExpression> EnumerateStatements(IEnumerable<MirStatement> statements)
    {
        foreach (MirStatement statement in statements)
        {
            foreach (MirExpression expression in EnumerateStatementExpressions(statement)) yield return expression;
        }
    }

    private static IEnumerable<MirAwaitExpression> EnumerateAwaits(MirExpression expression)
    {
        if (expression is MirAwaitExpression awaitExpression)
        {
            yield return awaitExpression;
        }

        foreach (MirExpression child in EnumerateChildExpressions(expression))
        {
            foreach (MirAwaitExpression nested in EnumerateAwaits(child))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<MirExpression> EnumerateChildExpressions(MirExpression expression)
    {
        switch (expression)
        {
            case MirAssignmentExpression assignment:
                yield return assignment.Expression;
                break;
            case MirUnaryExpression unary:
                yield return unary.Operand;
                break;
            case MirAwaitExpression awaited:
                yield return awaited.Operand;
                break;
            case MirBinaryExpression binary:
                yield return binary.Left;
                yield return binary.Right;
                break;
            case MirCallExpression call:
                foreach (MirExpression argument in call.Arguments) yield return argument;
                break;
            case MirCallableConstructionExpression construction:
                foreach (MirExpression capture in construction.Captures) yield return capture;
                break;
            case MirInvokeExpression invoke:
                yield return invoke.Callee;
                foreach (MirExpression argument in invoke.Arguments) yield return argument;
                break;
            case MirArrayExpression array:
                foreach (MirExpression element in array.Elements) yield return element;
                break;
            case MirRecordConstructionExpression construction:
                foreach (MirRecordFieldValue initializer in construction.Initializers) yield return initializer.Value;
                break;
            case MirRecordFieldAccessExpression access:
                yield return access.Receiver;
                break;
            case MirTableColumnAccessExpression access:
                yield return access.Receiver;
                break;
            case MirTableRowAccessExpression access:
                yield return access.Receiver;
                yield return access.Index;
                break;
            case MirColumnElementAccessExpression access:
                yield return access.Receiver;
                yield return access.Index;
                break;
            case MirTableRowFieldAccessExpression access:
                yield return access.Receiver;
                break;
            case MirRecordWithExpression withExpression:
                yield return withExpression.Source;
                foreach (MirRecordFieldValue replacement in withExpression.Replacements) yield return replacement.Value;
                break;
            case MirEnumValueExpression value:
                foreach (MirExpression argument in value.Arguments) yield return argument;
                break;
            case MirMatchExpression match:
                yield return match.Scrutinee;
                foreach (MirMatchArm arm in match.Arms) yield return arm.Expression;
                break;
            case MirIfExpression conditional:
                yield return conditional.Condition;
                yield return conditional.ThenExpression;
                yield return conditional.ElseExpression;
                break;
            case MirTsonEncodeExpression encode:
                yield return encode.Operand;
                break;
            case MirTsonTransportExpression transport:
                yield return transport.Operation;
                yield return transport.Request;
                break;
            case MirOkExpression ok:
                yield return ok.Payload;
                break;
            case MirErrExpression err:
                yield return err.Payload;
                break;
            case MirResultMatchExpression match:
                yield return match.Scrutinee;
                yield return match.OkExpression;
                yield return match.ErrExpression;
                break;
            case MirPropagateExpression propagation:
                yield return propagation.Operand;
                break;
            case MirUnwrapExpression unwrap:
                yield return unwrap.Operand;
                break;
            case MirTryExpression tryExpression:
                foreach (MirExpression nested in EnumerateStatements(tryExpression.Protected.PrefixStatements)) yield return nested;
                yield return tryExpression.Protected.ValueExpression;
                foreach (MirExpression nested in EnumerateStatements(tryExpression.Handler.PrefixStatements)) yield return nested;
                yield return tryExpression.Handler.ValueExpression;
                break;
        }
    }

    private static IReadOnlyList<MirStatement> LowerStatements(IReadOnlyList<BoundStatement> statements, Dictionary<string, MirLocal> locals)
        => statements.SelectMany(s => LowerStatement(s, locals)).ToArray();

    private static IReadOnlyList<MirStatement> LowerStatement(BoundStatement statement, Dictionary<string, MirLocal> locals)
    {
        return statement switch
        {
            BoundBlockStatement b => LowerStatements(b.Statements, locals),
            BoundVariableDeclaration v => [LowerVariable(v, locals)],
            BoundResourceUsingDeclaration u => [LowerResourceUsing(u, locals)],
            BoundCSharpBlockStatement c => [new MirCSharpBlockStatement(
                c.BodyText,
                c.SourceLine,
                ToMirType(c.ExpectedResultType),
                c.Captures.Select(capture => new MirCSharpCapture(capture.Name, ToMirType(capture.Type))).ToArray())],
            BoundExpressionStatement e => [new MirExpressionStatement(LowerExpression(e.Expression))],
            BoundReturnStatement r => [new MirReturnStatement(r.Expression is null ? null : LowerExpression(r.Expression))],
            BoundIfStatement i => [new MirIfStatement(LowerExpression(i.Condition), LowerStatement(i.ThenStatement, locals), i.ElseStatement is null ? null : LowerStatement(i.ElseStatement, locals))],
            BoundWhileStatement w => [new MirWhileStatement(LowerExpression(w.Condition), LowerStatement(w.Body, locals))],
            BoundForStatement f => [new MirForStatement(f.Initializer is null ? null : LowerStatement(f.Initializer, locals).Single(), f.Condition is null ? null : LowerExpression(f.Condition), f.Increment is null ? null : LowerExpression(f.Increment), LowerStatement(f.Body, locals))],
            BoundBreakStatement => [new MirBreakStatement()],
            BoundContinueStatement => [new MirContinueStatement()],
            _ => []
        };
    }

    private static MirStatement LowerVariable(BoundVariableDeclaration v, Dictionary<string, MirLocal> locals)
    {
        var local = new MirLocal(v.Variable.Name, ToMirType(v.Variable.Type), v.Variable.IsReadOnly);
        locals.TryAdd(local.Name, local);
        return new MirVariableDeclarationStatement(local, LowerExpression(v.Initializer));
    }

    private static MirStatement LowerResourceUsing(BoundResourceUsingDeclaration declaration, Dictionary<string, MirLocal> locals)
    {
        var local = new MirLocal(declaration.Variable.Name, ToMirType(declaration.Variable.Type), true);
        locals.TryAdd(local.Name, local);
        return new MirResourceUsingDeclarationStatement(local, LowerExpression(declaration.Initializer));
    }

    private static MirExpression LowerExpression(BoundExpression expression)
        => expression switch
        {
            BoundLiteralExpression l => new MirLiteralExpression(l.Value, ToMirType(l.Type)),
            BoundVariableExpression v => new MirVariableExpression(v.Variable.Name, ToMirType(v.Type)),
            BoundAssignmentExpression a => new MirAssignmentExpression(a.Variable.Name, LowerExpression(a.Expression), ToMirType(a.Type)),
            BoundUnaryExpression u => new MirUnaryExpression(OperatorName(u.OperatorKind), LowerExpression(u.Operand), ToMirType(u.Type)),
            BoundAwaitExpression a => new MirAwaitExpression(LowerExpression(a.Operand), ToMirType(a.Type)),
            BoundBinaryExpression b => new MirBinaryExpression(OperatorName(b.OperatorKind), LowerExpression(b.Left), LowerExpression(b.Right), ToMirType(b.Type)),
            BoundCallExpression c => new MirCallExpression(c.Function.Name, c.Arguments.Select(LowerExpression).ToArray(), ToMirType(c.Type)),
            BoundFunctionReferenceExpression reference => new MirFunctionReferenceExpression(reference.Function.Name, (MirCallableType)ToMirType(reference.Type)),
            BoundCallableConstructionExpression construction => new MirCallableConstructionExpression(
                construction.Code.Name,
                construction.Captures.Select(LowerExpression).ToArray(),
                (MirCallableType)ToMirType(construction.CallableType)),
            BoundInvokeExpression invoke => new MirInvokeExpression(LowerExpression(invoke.Callee), invoke.Arguments.Select(LowerExpression).ToArray(), ToMirType(invoke.Type)),
            BoundEnumValueExpression e => new MirEnumValueExpression(e.Case.EnumType.Name, e.Case.Name, e.Arguments.Select(LowerExpression).ToArray(), ToMirType(e.Type)),
            BoundMatchExpression m => new MirMatchExpression(LowerExpression(m.Scrutinee), m.Arms.Select(arm => new MirMatchArm(arm.Case.Name, arm.PayloadVariables.Select(v => new MirMatchPayloadBinding(v.Name, ToMirType(v.Type))).ToArray(), LowerExpression(arm.Expression))).ToArray(), ToMirType(m.Type)),
            BoundResultMatchExpression m => new MirResultMatchExpression(LowerExpression(m.Scrutinee), new MirResultBinding(m.OkVariable.Name, ToMirType(m.OkVariable.Type)), LowerExpression(m.OkExpression), new MirResultBinding(m.ErrVariable.Name, ToMirType(m.ErrVariable.Type)), LowerExpression(m.ErrExpression), ToMirType(m.Type)),
            BoundIfExpression i => new MirIfExpression(LowerExpression(i.Condition), LowerExpression(i.ThenExpression), LowerExpression(i.ElseExpression), ToMirType(i.Type)),
            BoundTsonEncodeExpression encode => new MirTsonEncodeExpression(
                LowerExpression(encode.Operand),
                new MirTsonEncodingPlanId(encode.Plan.Id),
                (MirResultType)ToMirType(encode.ResultType)),
            BoundTsonTransportExpression transport => new MirTsonTransportExpression(
                LowerExpression(transport.Operation),
                LowerExpression(transport.Request),
                new MirTsonEncodingPlanId(transport.RequestPlan.Id),
                new MirTsonEncodingPlanId(transport.ResponsePlan.Id),
                new MirTsonEncodingPlanId(transport.RemoteErrorPlan.Id),
                (MirAsyncType)ToMirType(transport.Type)),
            BoundNpmCallExpression npm => new MirNpmCallExpression(
                npm.Function.Name,
                npm.Function.PackageName,
                npm.Function.PackageVersion,
                npm.Function.ExportName,
                npm.Arguments.Select(LowerExpression).ToArray(),
                LowerExpression(npm.ArgumentTuple),
                new MirTsonEncodingPlanId(npm.RequestPlan.Id),
                new MirTsonEncodingPlanId(npm.ResponsePlan.Id),
                new MirTsonEncodingPlanId(npm.RemoteErrorPlan.Id),
                ToMirRecordFieldId(npm.ResponseValueField.Id),
                ToMirRecordFieldId(npm.RemoteErrorValueField.Id),
                (MirAsyncType)ToMirType(npm.Type)),
            BoundClrInvocationExpression invocation => new MirClrInvocationExpression(
                LowerClrMemberIdentity(invocation.Member, invocation.GenericArguments, invocation.Type),
                invocation.Receiver is null ? null : LowerExpression(invocation.Receiver),
                invocation.Arguments.Select(LowerExpression).ToArray(),
                ToMirType(invocation.Type)),
            BoundClrPropertyAccessExpression property => new MirClrPropertyAccessExpression(
                LowerClrMemberIdentity(property.Property, [], property.Type),
                property.Receiver is null ? null : LowerExpression(property.Receiver),
                ToMirType(property.Type)),
            BoundPropagateExpression p => new MirPropagateExpression(LowerExpression(p.Operand), LowerPropagationTarget(p.Target), ToMirType(p.Type)),
            BoundUnwrapExpression u => new MirUnwrapExpression(LowerExpression(u.Operand), ToMirType(u.Type)),
            BoundTryExceptExpression t => new MirTryExpression(
                new MirHandlerId(t.HandlerId.Value),
                LowerValueBlock(t.Protected),
                new MirTryBinding(t.HandlerBinding.Name, ToMirType(t.HandlerBinding.Type)),
                ToMirType(t.HandledErrorType),
                LowerValueBlock(t.Handler),
                ToMirType(t.Type)),
            BoundOkExpression ok => new MirOkExpression(LowerExpression(ok.Payload), (MirResultType)ToMirType(ok.Type)),
            BoundErrExpression err => new MirErrExpression(LowerExpression(err.Payload), (MirResultType)ToMirType(err.Type)),
            BoundUnitExpression => new MirUnitExpression(),
            BoundArrayExpression a => new MirArrayExpression(a.Elements.Select(LowerExpression).ToArray(), ToMirType(a.Type)),
            BoundRecordConstructionExpression construction => new MirRecordConstructionExpression(
                ToMirRecordTypeId(construction.RecordType.Id),
                construction.Initializers.Select(LowerRecordFieldValue).ToArray(),
                (MirRecordType)ToMirType(construction.Type)),
            BoundRecordFieldAccessExpression access => new MirRecordFieldAccessExpression(
                LowerExpression(access.Receiver),
                ToMirRecordTypeId(access.RecordType.Id),
                ToMirRecordFieldId(access.Field.Id),
                ToMirType(access.Type)),
            BoundTableReferenceExpression table => new MirTableReferenceExpression(new MirTableId(table.TableType.Id.ToString()), ToMirType(table.Type)),
            BoundTableColumnAccessExpression access => new MirTableColumnAccessExpression(LowerExpression(access.Receiver), new MirTableId(access.TableType.Id.ToString()), new MirTableColumnId(access.Column.Id.ToString()), ToMirType(access.Type)),
            BoundTableRowAccessExpression access => new MirTableRowAccessExpression(LowerExpression(access.Receiver), LowerExpression(access.Index), new MirTableId(access.TableType.Id.ToString()), ToMirType(access.Type)),
            BoundColumnElementAccessExpression access => new MirColumnElementAccessExpression(LowerExpression(access.Receiver), LowerExpression(access.Index), ToMirType(access.Type)),
            BoundTableRowFieldAccessExpression access => new MirTableRowFieldAccessExpression(LowerExpression(access.Receiver), access.RowType.TableId + ".row", access.Field.Id.ToString(), ToMirType(access.Type)),
            BoundRecordWithExpression withExpression => new MirRecordWithExpression(
                LowerExpression(withExpression.Source),
                ToMirRecordTypeId(withExpression.RecordType.Id),
                withExpression.Replacements.Select(LowerRecordFieldValue).ToArray(),
                (MirRecordType)ToMirType(withExpression.Type)),
            _ => new MirLiteralExpression("<error>", new MirNamedType("error"))
        };

    private static MirRecordFieldValue LowerRecordFieldValue(BoundRecordFieldInitializer initializer)
        => new(ToMirRecordFieldId(initializer.Field.Id), LowerExpression(initializer.Value));

    private static MirValueBlock LowerValueBlock(BoundValueBlock block)
    {
        var locals = new Dictionary<string, MirLocal>(StringComparer.Ordinal);
        return new MirValueBlock(LowerStatements(block.PrefixStatements, locals), LowerExpression(block.ValueExpression));
    }

    private static MirPropagationTarget LowerPropagationTarget(BoundPropagationTarget target)
        => target switch
        {
            BoundPropagationTarget.FunctionReturn => new MirPropagationTarget.FunctionReturn(),
            BoundPropagationTarget.LexicalExcept lexical => new MirPropagationTarget.LexicalExcept(new MirHandlerId(lexical.HandlerId.Value)),
            _ => throw new InvalidOperationException($"Unsupported propagation target {target.GetType().Name}.")
        };

    private static MirType ToMirType(TypeSymbol type) => type switch
    {
        ArrayTypeSymbol array => new MirArrayType(ToMirType(array.ElementType)),
        AsyncTypeSymbol async => new MirAsyncType(ToMirType(async.EventualType)),
        ResultTypeSymbol result => new MirResultType(ToMirType(result.SuccessType), ToMirType(result.ErrorType)),
        CallableTypeSymbol callable => new MirCallableType(callable.Parameters.Select(parameter => new MirCallableParameter(parameter.Name, ToMirType(parameter.Type))).ToArray(), ToMirType(callable.ReturnType)),
        RecordTypeSymbol record => new MirRecordType(ToMirRecordTypeId(record.Id), record.Name),
        ClrTypeSymbol clr => new MirClrType(clr.AssemblyIdentity, clr.Namespace, clr.MetadataName),
        TableTypeSymbol table => new MirTableType(new MirTableId(table.Id.ToString()), table.Name),
        TableRowTypeSymbol row => new MirTableRowType(row.TableId + ".row", row.Name),
        ColumnTypeSymbol column => new MirColumnType(ToMirType(column.ElementType)),
        _ => new MirNamedType(type.Name)
    };

    private static MirClrMemberIdentity LowerClrMemberIdentity(System.Reflection.MethodBase member, IReadOnlyList<TypeSymbol> genericArguments, TypeSymbol resultType)
    {
        Type declaringType = member.DeclaringType ?? throw new InvalidOperationException("CLR member has no declaring type.");
        return new MirClrMemberIdentity(
            declaringType.Assembly.FullName ?? declaringType.Assembly.GetName().Name ?? "<unknown>",
            declaringType.Namespace ?? string.Empty,
            declaringType.FullName?.Replace('+', '.') ?? declaringType.Name,
            member is System.Reflection.ConstructorInfo ? ".ctor" : member.Name,
            member.IsStatic,
            member is System.Reflection.ConstructorInfo,
            member.GetParameters().Select(parameter => ToMirTypeFromRuntimeType(parameter.ParameterType)).ToArray(),
            ToMirType(resultType),
            genericArguments.Select(ToMirType).ToArray());
    }

    private static MirClrMemberIdentity LowerClrMemberIdentity(System.Reflection.PropertyInfo property, IReadOnlyList<TypeSymbol> genericArguments, TypeSymbol resultType)
    {
        Type declaringType = property.DeclaringType ?? throw new InvalidOperationException("CLR property has no declaring type.");
        return new MirClrMemberIdentity(
            declaringType.Assembly.FullName ?? declaringType.Assembly.GetName().Name ?? "<unknown>",
            declaringType.Namespace ?? string.Empty,
            declaringType.FullName?.Replace('+', '.') ?? declaringType.Name,
            property.Name,
            property.GetMethod?.IsStatic == true,
            false,
            [],
            ToMirType(resultType),
            genericArguments.Select(ToMirType).ToArray());
    }

    private static MirType ToMirTypeFromRuntimeType(Type type)
    {
        if (type == typeof(void)) return new MirNamedType("void");
        if (type == typeof(string)) return new MirNamedType("string");
        if (type == typeof(bool)) return new MirNamedType("boolean");
        if (type == typeof(double) || type == typeof(float) || type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte)) return new MirNamedType("number");
        if (type.IsArray && type.GetArrayRank() == 1) return new MirArrayType(ToMirTypeFromRuntimeType(type.GetElementType()!));
        return new MirClrType(type.Assembly.FullName ?? type.Assembly.GetName().Name ?? "<unknown>", type.Namespace ?? string.Empty, type.FullName?.Replace('+', '.') ?? type.Name);
    }

    private static MirRecordTypeId ToMirRecordTypeId(RecordTypeId id) => new(id.ToString());

    private static MirRecordFieldId ToMirRecordFieldId(RecordFieldId id) => new(id.ToString());

    private static string OperatorName(SyntaxKind kind) => kind switch
    {
        SyntaxKind.PlusToken => "+",
        SyntaxKind.MinusToken => "-",
        SyntaxKind.StarToken => "*",
        SyntaxKind.SlashToken => "/",
        SyntaxKind.PercentToken => "%",
        SyntaxKind.BangToken => "!",
        SyntaxKind.LessToken => "<",
        SyntaxKind.LessOrEqualsToken => "<=",
        SyntaxKind.GreaterToken => ">",
        SyntaxKind.GreaterOrEqualsToken => ">=",
        SyntaxKind.EqualsEqualsToken => "==",
        SyntaxKind.BangEqualsToken => "!=",
        SyntaxKind.EqualsEqualsEqualsToken => "===",
        SyntaxKind.BangEqualsEqualsToken => "!==",
        SyntaxKind.AmpersandAmpersandToken => "&&",
        SyntaxKind.PipePipeToken => "||",
        _ => kind.ToString()
    };
}
