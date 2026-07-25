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
        return new MirFunction(
            function.Symbol.Name,
            function.Symbol.Parameters.Select(p => new MirParameter(p.Name, ToMirType(p.Type))).ToArray(),
            ToMirType(function.Symbol.ReturnType),
            locals.Values.OrderBy(l => l.Name, StringComparer.Ordinal).ToArray(),
            body,
            function.Symbol.IsAsync);
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
