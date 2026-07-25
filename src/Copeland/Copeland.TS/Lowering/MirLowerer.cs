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
        return new MirProgram(enums, records, tables, tsonEncodingPlans, functions);
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
        for (int index = 0; index < awaits.Length; index++)
        {
            MirFrameSlotId slotId = new("await_" + index);
            awaitSlots.Add(awaits[index], slotId);
            frameSlots.Add(new MirFrameSlot(
                slotId,
                awaits[index].Operand.Type,
                "await operand " + index,
                isReadOnly: true));
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
            BuildAsyncExecutionPlan(body, awaitSlots));
    }

    private static MirAsyncExecutionPlan BuildAsyncExecutionPlan(
        IReadOnlyList<MirStatement> body,
        IReadOnlyDictionary<MirAwaitExpression, MirFrameSlotId> awaitSlots)
    {
        var states = new List<MirAsyncExecutionState>();
        int nextId = 0;

        MirAsyncExecutionStateId Add(MirAsyncExecutionState state)
        {
            states.Add(state);
            return state.Id;
        }

        MirAsyncExecutionStateId NewId() => new("exec_" + nextId++);

        MirFrameSlotId? GetAwaitSlot(MirStatement statement)
        {
            MirAwaitExpression? awaited = statement switch
            {
                MirVariableDeclarationStatement { Initializer: MirAwaitExpression direct } => direct,
                MirVariableDeclarationStatement { Initializer: MirPropagateExpression { Operand: MirAwaitExpression propagated } } => propagated,
                MirReturnStatement { Expression: MirAwaitExpression direct } => direct,
                MirExpressionStatement { Expression: MirAssignmentExpression { Expression: MirAwaitExpression direct } } => direct,
                _ => null,
            };
            return awaited is not null && awaitSlots.TryGetValue(awaited, out MirFrameSlotId slotId)
                ? slotId
                : null;
        }

        MirAsyncStatementExecutionState CreateStatementState(
            MirAsyncExecutionStateId id,
            MirStatement statement,
            MirAsyncExecutionStateId nextStateId)
        {
            return new MirAsyncStatementExecutionState(id, statement, nextStateId, GetAwaitSlot(statement));
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
                        MirAsyncExecutionStateId id = NewId();
                        Add(new MirAsyncReturnExecutionState(id, returned, GetAwaitSlot(returned)));
                        current = id;
                        break;
                    }
                    case MirIfStatement conditional:
                    {
                        MirAsyncExecutionStateId thenState = Build(conditional.ThenStatements, current, breakTarget, continueTarget);
                        MirAsyncExecutionStateId elseState = conditional.ElseStatements is null
                            ? current
                            : Build(conditional.ElseStatements, current, breakTarget, continueTarget);
                        MirAsyncExecutionStateId id = NewId();
                        Add(new MirAsyncBranchExecutionState(id, conditional.Condition, thenState, elseState));
                        current = id;
                        break;
                    }
                    case MirWhileStatement loop:
                    {
                        MirAsyncExecutionStateId id = NewId();
                        MirAsyncExecutionStateId bodyState = Build(loop.BodyStatements, id, current, id);
                        Add(new MirAsyncBranchExecutionState(id, loop.Condition, bodyState, current));
                        current = id;
                        break;
                    }
                    case MirForStatement loop:
                    {
                        MirExpression condition = loop.Condition ?? new MirLiteralExpression(true, new MirNamedType("boolean"));
                        MirAsyncExecutionStateId conditionId = NewId();
                        MirAsyncExecutionStateId incrementId = conditionId;
                        if (loop.Increment is not null)
                        {
                            incrementId = NewId();
                            Add(CreateStatementState(incrementId, new MirExpressionStatement(loop.Increment), conditionId));
                        }
                        MirAsyncExecutionStateId bodyState = Build(loop.BodyStatements, incrementId, current, incrementId);
                        Add(new MirAsyncBranchExecutionState(conditionId, condition, bodyState, current));
                        current = loop.Initializer is null
                            ? conditionId
                            : Build([loop.Initializer], conditionId, breakTarget, continueTarget);
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
                        MirAsyncExecutionStateId id = NewId();
                        Add(CreateStatementState(id, statement, current));
                        current = id;
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
        TableTypeSymbol table => new MirTableType(new MirTableId(table.Id.ToString()), table.Name),
        TableRowTypeSymbol row => new MirTableRowType(row.TableId + ".row", row.Name),
        ColumnTypeSymbol column => new MirColumnType(ToMirType(column.ElementType)),
        _ => new MirNamedType(type.Name)
    };

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
