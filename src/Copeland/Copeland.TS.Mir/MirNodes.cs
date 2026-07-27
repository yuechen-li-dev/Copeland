namespace Copeland.TS.Mir;

public sealed class MirProgram
{
    public MirProgram(IReadOnlyList<MirEnum> enums, IReadOnlyList<MirFunction> functions)
        : this(enums, [], functions)
    {
    }

    public MirProgram(IReadOnlyList<MirEnum> enums, IReadOnlyList<MirRecordDefinition> records, IReadOnlyList<MirFunction> functions)
        : this(enums, records, [], functions)
    {
    }

    public MirProgram(IReadOnlyList<MirEnum> enums, IReadOnlyList<MirRecordDefinition> records, IReadOnlyList<MirTableDefinition> tables, IReadOnlyList<MirFunction> functions)
        : this(enums, records, tables, [], functions)
    {
    }

    public MirProgram(IReadOnlyList<MirEnum> enums, IReadOnlyList<MirRecordDefinition> records, IReadOnlyList<MirTableDefinition> tables, IReadOnlyList<MirTsonEncodingPlan> tsonEncodingPlans, IReadOnlyList<MirFunction> functions)
        : this(enums, records, tables, tsonEncodingPlans, [], functions)
    {
    }

    public MirProgram(IReadOnlyList<MirEnum> enums, IReadOnlyList<MirRecordDefinition> records, IReadOnlyList<MirTableDefinition> tables, IReadOnlyList<MirTsonEncodingPlan> tsonEncodingPlans, IReadOnlyList<MirNpmImport> npmImports, IReadOnlyList<MirFunction> functions, IReadOnlyList<string>? csharpUsings = null, string? csharpSourcePath = null, IReadOnlyList<MirFlowDefinition>? flows = null, IReadOnlyList<MirJavaScriptHostImport>? javaScriptHostImports = null, IReadOnlyList<MirPackageImport>? packageImports = null)
    {
        Enums = enums;
        Records = records;
        Tables = tables;
        TsonEncodingPlans = tsonEncodingPlans;
        NpmImports = npmImports;
        JavaScriptHostImports = javaScriptHostImports ?? [];
        PackageImports = packageImports ?? [];
        Functions = functions;
        CSharpUsings = csharpUsings ?? [];
        CSharpSourcePath = csharpSourcePath;
        Flows = flows ?? [];
    }

    public IReadOnlyList<MirEnum> Enums { get; }
    public IReadOnlyList<MirRecordDefinition> Records { get; }
    public IReadOnlyList<MirTableDefinition> Tables { get; }
    public IReadOnlyList<MirTsonEncodingPlan> TsonEncodingPlans { get; }
    public IReadOnlyList<MirNpmImport> NpmImports { get; }
    public IReadOnlyList<MirJavaScriptHostImport> JavaScriptHostImports { get; }
    public IReadOnlyList<MirPackageImport> PackageImports { get; }
    public IReadOnlyList<MirFunction> Functions { get; }
    public IReadOnlyList<string> CSharpUsings { get; }
    public string? CSharpSourcePath { get; }
    public IReadOnlyList<MirFlowDefinition> Flows { get; }
}

/// <summary>
/// Backend-neutral, normalized event automaton. This remains distinct from the
/// async suspension automaton because event delivery owns durable progression.
/// </summary>
public sealed class MirFlowDefinition(
    string name,
    string stableIdentity,
    MirRecordType boardType,
    IReadOnlyList<MirFlowBoardField> boardFields,
    IReadOnlyList<MirFlowEvent> events,
    IReadOnlyList<MirFlowState> states,
    string initialState,
    MirType resultType,
    MirType? failureType)
{
    public string Name { get; } = name;
    public string StableIdentity { get; } = stableIdentity;
    public MirRecordType BoardType { get; } = boardType;
    public IReadOnlyList<MirFlowBoardField> BoardFields { get; } = boardFields;
    public IReadOnlyList<MirFlowEvent> Events { get; } = events;
    public IReadOnlyList<MirFlowState> States { get; } = states;
    public string InitialState { get; } = initialState;
    public MirType ResultType { get; } = resultType;
    public MirType? FailureType { get; } = failureType;
}

public sealed class MirFlowBoardField(MirRecordFieldId id, string name, MirType type, MirExpression initializer)
{
    public MirRecordFieldId Id { get; } = id;
    public string Name { get; } = name;
    public MirType Type { get; } = type;
    public MirExpression Initializer { get; } = initializer;
}

public sealed class MirFlowEvent(string name, string stableIdentity, IReadOnlyList<MirParameter> payloads)
{
    public string Name { get; } = name;
    public string StableIdentity { get; } = stableIdentity;
    public IReadOnlyList<MirParameter> Payloads { get; } = payloads;
}

public sealed class MirFlowState(string name, string stableIdentity, bool isInitial, IReadOnlyList<MirFlowTransition> transitions, MirFlowTerminal? terminal)
{
    public string Name { get; } = name;
    public string StableIdentity { get; } = stableIdentity;
    public bool IsInitial { get; } = isInitial;
    public IReadOnlyList<MirFlowTransition> Transitions { get; } = transitions;
    public MirFlowTerminal? Terminal { get; } = terminal;
}

public sealed class MirFlowTransition(string eventName, string targetState, MirExpression? guard, IReadOnlyList<MirParameter> bindings, IReadOnlyList<MirFlowBoardUpdate> updates)
{
    public string EventName { get; } = eventName;
    public string TargetState { get; } = targetState;
    public MirExpression? Guard { get; } = guard;
    public IReadOnlyList<MirParameter> Bindings { get; } = bindings;
    public IReadOnlyList<MirFlowBoardUpdate> Updates { get; } = updates;
}

public sealed class MirFlowBoardUpdate(MirRecordFieldId fieldId, MirExpression value)
{
    public MirRecordFieldId FieldId { get; } = fieldId;
    public MirExpression Value { get; } = value;
}

public sealed class MirFlowTerminal(bool isFailure, MirExpression? expression)
{
    public bool IsFailure { get; } = isFailure;
    public MirExpression? Expression { get; } = expression;
}

public readonly record struct MirTsonEncodingPlanId(string Value)
{
    public override string ToString() => Value;
}

public sealed class MirTsonEncodingLimits(
    int maximumUtf8Bytes,
    int maximumStringCodeUnits,
    int maximumArrayLength = 100_000,
    int maximumColumns = 256,
    int maximumRows = 100_000,
    int maximumCells = 100_000,
    int maximumValueNodes = 100_000,
    int maximumNestingDepth = 64)
{
    public int MaximumUtf8Bytes { get; } = maximumUtf8Bytes;
    public int MaximumStringCodeUnits { get; } = maximumStringCodeUnits;
    public int MaximumArrayLength { get; } = maximumArrayLength;
    public int MaximumColumns { get; } = maximumColumns;
    public int MaximumRows { get; } = maximumRows;
    public int MaximumCells { get; } = maximumCells;
    public int MaximumValueNodes { get; } = maximumValueNodes;
    public int MaximumNestingDepth { get; } = maximumNestingDepth;
}

public abstract record MirTsonValuePlan;
public sealed record MirTsonBooleanPlan : MirTsonValuePlan;
public sealed record MirTsonNumberPlan : MirTsonValuePlan;
public sealed record MirTsonStringPlan : MirTsonValuePlan;
public sealed record MirTsonRecordValuePlan(MirRecordTypeId RecordTypeId) : MirTsonValuePlan;
public sealed record MirTsonEnumValuePlan(string EnumName) : MirTsonValuePlan;
public sealed record MirTsonArrayPlan(MirTsonValuePlan ElementPlan) : MirTsonValuePlan;
public sealed record MirTsonTableValuePlan(MirTableId TableId) : MirTsonValuePlan;

public abstract class MirTsonNominalPlan(string name, string stableIdentity)
{
    public string Name { get; } = name;
    public string StableIdentity { get; } = stableIdentity;
}

public sealed class MirTsonRecordPlan(
    MirRecordTypeId recordTypeId,
    string name,
    string stableIdentity,
    IReadOnlyList<MirTsonRecordFieldPlan> fields) : MirTsonNominalPlan(name, stableIdentity)
{
    public MirRecordTypeId RecordTypeId { get; } = recordTypeId;
    public IReadOnlyList<MirTsonRecordFieldPlan> Fields { get; } = Array.AsReadOnly(fields.ToArray());
}

public sealed class MirTsonRecordFieldPlan(
    MirRecordFieldId fieldId,
    string name,
    string stableIdentity,
    MirTsonValuePlan valuePlan)
{
    public MirRecordFieldId FieldId { get; } = fieldId;
    public string Name { get; } = name;
    public string StableIdentity { get; } = stableIdentity;
    public MirTsonValuePlan ValuePlan { get; } = valuePlan;
}

public sealed class MirTsonEnumPlan(
    string name,
    string stableIdentity,
    IReadOnlyList<MirTsonEnumCasePlan> cases) : MirTsonNominalPlan(name, stableIdentity)
{
    public IReadOnlyList<MirTsonEnumCasePlan> Cases { get; } = Array.AsReadOnly(cases.ToArray());
}

public sealed class MirTsonEnumCasePlan(
    string name,
    string stableIdentity,
    IReadOnlyList<MirTsonEnumPayloadPlan> payloads)
{
    public string Name { get; } = name;
    public string StableIdentity { get; } = stableIdentity;
    public IReadOnlyList<MirTsonEnumPayloadPlan> Payloads { get; } = Array.AsReadOnly(payloads.ToArray());
}

public sealed class MirTsonEnumPayloadPlan(
    string name,
    string stableIdentity,
    MirTsonValuePlan valuePlan)
{
    public string Name { get; } = name;
    public string StableIdentity { get; } = stableIdentity;
    public MirTsonValuePlan ValuePlan { get; } = valuePlan;
}

public sealed class MirTsonEncodingPlan(
    MirTsonEncodingPlanId id,
    string schemaIdentity,
    MirType rootType,
    MirTsonValuePlan rootValuePlan,
    IReadOnlyList<MirTsonNominalPlan> definitions,
    MirTsonEncodingLimits limits,
    MirTsonTablePlan? tablePlan = null)
{
    public MirTsonEncodingPlanId Id { get; } = id;
    public string SchemaIdentity { get; } = schemaIdentity;
    public MirType RootType { get; } = rootType;
    public MirTsonValuePlan RootValuePlan { get; } = rootValuePlan;
    public IReadOnlyList<MirTsonNominalPlan> Definitions { get; } = Array.AsReadOnly(definitions.ToArray());
    public MirTsonEncodingLimits Limits { get; } = limits;
    public MirTsonTablePlan? TablePlan { get; } = tablePlan;
}

public sealed class MirTsonTablePlan(
    MirTableId tableId,
    string name,
    string stableIdentity,
    int expectedRowCount,
    IReadOnlyList<MirTsonTableColumnPlan> columns)
{
    public MirTableId TableId { get; } = tableId;
    public string Name { get; } = name;
    public string StableIdentity { get; } = stableIdentity;
    public int ExpectedRowCount { get; } = expectedRowCount;
    public IReadOnlyList<MirTsonTableColumnPlan> Columns { get; } = Array.AsReadOnly(columns.ToArray());
}

public sealed class MirTsonTableColumnPlan(
    MirTableColumnId columnId,
    string name,
    string stableIdentity,
    MirTsonValuePlan elementPlan,
    int expectedElementCount)
{
    public MirTableColumnId ColumnId { get; } = columnId;
    public string Name { get; } = name;
    public string StableIdentity { get; } = stableIdentity;
    public MirTsonValuePlan ElementPlan { get; } = elementPlan;
    public int ExpectedElementCount { get; } = expectedElementCount;
}

public readonly record struct MirTableId(string Value) { public override string ToString() => Value; }
public readonly record struct MirTableColumnId(string Value) { public override string ToString() => Value; }
public sealed class MirTableDefinition(MirTableId id, string name, string rowTypeId, IReadOnlyList<MirTableColumnDefinition> columns, int rowCount)
{ public MirTableId Id { get; } = id; public string Name { get; } = name; public string RowTypeId { get; } = rowTypeId; public IReadOnlyList<MirTableColumnDefinition> Columns { get; } = columns; public int RowCount { get; } = rowCount; }
public abstract record MirTableConstant(MirType Type);
public sealed record MirTableLiteralConstant(object Value, MirType Type) : MirTableConstant(Type);
public sealed record MirTableArrayConstant : MirTableConstant
{
    public MirTableArrayConstant(
        MirArrayType arrayType,
        IReadOnlyList<MirTableConstant> elements)
        : base(arrayType)
    {
        ArrayType = arrayType;
        Elements = Array.AsReadOnly(elements.ToArray());
    }

    public MirArrayType ArrayType { get; }
    public IReadOnlyList<MirTableConstant> Elements { get; }
}
public sealed class MirTableRecordFieldConstant(MirRecordFieldId fieldId, MirTableConstant value)
{ public MirRecordFieldId FieldId { get; } = fieldId; public MirTableConstant Value { get; } = value; }
public sealed record MirTableRecordConstant(MirRecordTypeId RecordTypeId, IReadOnlyList<MirTableRecordFieldConstant> Fields, MirType Type) : MirTableConstant(Type);
public sealed record MirTableEnumConstant(string EnumName, string CaseName, IReadOnlyList<MirTableConstant> Payloads, MirType Type) : MirTableConstant(Type);
public sealed record MirTableResultConstant : MirTableConstant
{
    public MirTableResultConstant(bool isOk, MirTableConstant payload, MirResultType type)
        : base(type)
    {
        IsOk = isOk;
        Payload = payload;
    }

    public bool IsOk { get; }
    public MirTableConstant Payload { get; }
    public new MirResultType Type => (MirResultType)base.Type;
}
public sealed class MirTableColumnDefinition(MirTableColumnId id, string name, MirType elementType, IReadOnlyList<MirTableConstant> constants)
{ public MirTableColumnId Id { get; } = id; public string Name { get; } = name; public MirType ElementType { get; } = elementType; public IReadOnlyList<MirTableConstant> Constants { get; } = constants; }

public readonly record struct MirRecordTypeId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct MirRecordFieldId(string Value)
{
    public override string ToString() => Value;
}

public sealed class MirRecordDefinition(
    MirRecordTypeId id,
    string name,
    IReadOnlyList<MirRecordFieldDefinition> fields,
    bool isClass = false)
{
    public MirRecordTypeId Id { get; } = id;
    public string Name { get; } = name;
    public IReadOnlyList<MirRecordFieldDefinition> Fields { get; } = fields;
    public bool IsClass { get; } = isClass;
}

public sealed class MirRecordFieldDefinition(
    MirRecordFieldId id,
    string name,
    MirType type,
    bool isPublic = true)
{
    public MirRecordFieldId Id { get; } = id;
    public string Name { get; } = name;
    public MirType Type { get; } = type;
    public bool IsPublic { get; } = isPublic;
}

public sealed class MirEnum(string name, IReadOnlyList<MirEnumCase> cases)
{
    public string Name { get; } = name;
    public IReadOnlyList<MirEnumCase> Cases { get; } = cases;
}

public sealed class MirEnumCase(string name, IReadOnlyList<MirEnumPayloadField> payloadFields)
{
    public string Name { get; } = name;
    public IReadOnlyList<MirEnumPayloadField> PayloadFields { get; } = payloadFields;
}

public sealed class MirEnumPayloadField(string name, MirType type)
{
    public string Name { get; } = name;
    public MirType Type { get; } = type;
}

public sealed class MirFunction(
    string name,
    IReadOnlyList<MirParameter> parameters,
    MirType returnType,
    IReadOnlyList<MirLocal> locals,
    IReadOnlyList<MirStatement> body,
    bool isAsync = false,
    bool isGenerator = false,
    MirSuspensionAutomaton? suspensionAutomaton = null)
{
    public string Name { get; } = name;
    public IReadOnlyList<MirParameter> Parameters { get; } = parameters;
    public MirType ReturnType { get; } = returnType;
    public bool IsFallible => ReturnType is MirResultType;
    public bool IsAsync { get; } = isAsync;
    public bool IsGenerator { get; } = isGenerator;
    public MirSuspensionAutomaton? SuspensionAutomaton { get; } = suspensionAutomaton;
    public IReadOnlyList<MirLocal> Locals { get; } = locals;
    public IReadOnlyList<MirStatement> Body { get; } = body;
}

public sealed class MirParameter(string name, MirType type) { public string Name { get; } = name; public MirType Type { get; } = type; }
public sealed class MirLocal(string name, MirType type, bool isReadOnly) { public string Name { get; } = name; public MirType Type { get; } = type; public bool IsReadOnly { get; } = isReadOnly; }

public abstract record MirStatement;
public sealed record MirVariableDeclarationStatement(MirLocal Local, MirExpression Initializer) : MirStatement;
public sealed record MirExpressionStatement(MirExpression Expression) : MirStatement;
public sealed record MirReturnStatement(MirExpression? Expression) : MirStatement;
public sealed record MirIfStatement(MirExpression Condition, IReadOnlyList<MirStatement> ThenStatements, IReadOnlyList<MirStatement>? ElseStatements) : MirStatement;
public sealed record MirWhileStatement(MirExpression Condition, IReadOnlyList<MirStatement> BodyStatements) : MirStatement;
public sealed record MirForStatement(MirStatement? Initializer, MirExpression? Condition, MirExpression? Increment, IReadOnlyList<MirStatement> BodyStatements) : MirStatement;
public sealed record MirForOfStatement(MirLocal Local, MirExpression Iterable, IReadOnlyList<MirStatement> BodyStatements) : MirStatement;
public sealed record MirBreakStatement : MirStatement;
public sealed record MirContinueStatement : MirStatement;
public sealed record MirYieldStatement(MirExpression? Expression, bool IsDelegating = false) : MirStatement;
public sealed record MirResourceUsingDeclarationStatement(MirLocal Local, MirExpression Initializer) : MirStatement;
public sealed record MirCSharpCapture(string Name, MirType Type);
public sealed record MirCSharpBlockStatement(string BodyText, int SourceLine, MirType ExpectedResultType, IReadOnlyList<MirCSharpCapture> Captures) : MirStatement;

public record MirType(string Identifier)
{
    public virtual string Name => Identifier;
}

public sealed record MirNamedType(string Identifier) : MirType(Identifier);
public sealed record MirClrType(string AssemblyIdentity, string Namespace, string MetadataName) : MirType(MetadataName)
{
    public override string Name => MetadataName;
}
public sealed record MirRecordType(MirRecordTypeId RecordTypeId, string DisplayName) : MirType(RecordTypeId.Value)
{
    public override string Name => DisplayName;
}
public sealed record MirTableType(MirTableId TableId, string DisplayName) : MirType(TableId.Value) { public override string Name => DisplayName; }
public sealed record MirTableRowType(string RowTypeId, string DisplayName) : MirType(RowTypeId) { public override string Name => DisplayName; }
public sealed record MirColumnType(MirType ElementType) : MirType("column") { public override string Name => "column " + ElementType.Name; }
public sealed record MirArrayType(MirType ElementType) : MirType("array") { public override string Name => MirTypeText.FormatArrayElement(ElementType) + "[]"; }
public sealed record MirResultType(MirType SuccessType, MirType ErrorType) : MirType("result") { public override string Name => $"{MirTypeText.FormatResultComponent(SuccessType)} ! {ErrorType.Name}"; }
/// <summary>
/// A compiler-owned asynchronous computation. This is deliberately not a host
/// Task or Promise type: its eventual value is completed by a Copeland
/// suspension automaton.
/// </summary>
public sealed record MirAsyncType(MirType EventualType) : MirType("async")
{
    public override string Name => $"Async<{EventualType.Name}>";
}
public sealed record MirIterableType(MirType ElementType) : MirType("iterable")
{
    public override string Name => $"Iterable<{ElementType.Name}>";
}
public sealed record MirCallableParameter(string Name, MirType Type);
public sealed record MirCallableType(IReadOnlyList<MirCallableParameter> Parameters, MirType ReturnType) : MirType("callable")
{
    public override string Name => "(" + string.Join(", ", Parameters.Select(parameter => parameter.Name + ": " + parameter.Type.Name)) + ") => " + ReturnType.Name;
}
public sealed record MirAsyncCallableType(IReadOnlyList<MirCallableParameter> Parameters, MirType EventualReturnType) : MirType("async-callable")
{
    public override string Name => "async (" + string.Join(", ", Parameters.Select(parameter => parameter.Name + ": " + parameter.Type.Name)) + ") => " + EventualReturnType.Name;
}

public static class MirTypeFacts
{
    public static bool AreEquivalent(MirType left, MirType right)
        => (left, right) switch
        {
            (MirRecordType leftRecord, MirRecordType rightRecord) => leftRecord.RecordTypeId == rightRecord.RecordTypeId,
            (MirTableType leftTable, MirTableType rightTable) => leftTable.TableId == rightTable.TableId,
            (MirTableRowType leftRow, MirTableRowType rightRow) => leftRow.RowTypeId == rightRow.RowTypeId,
            (MirColumnType leftColumn, MirColumnType rightColumn) => AreEquivalent(leftColumn.ElementType, rightColumn.ElementType),
            (MirRecordType, _) or (_, MirRecordType) => false,
            (MirCallableType leftCallable, MirCallableType rightCallable) => leftCallable.Parameters.Count == rightCallable.Parameters.Count
                && leftCallable.Parameters.Zip(rightCallable.Parameters).All(pair => AreEquivalent(pair.First.Type, pair.Second.Type))
                && AreEquivalent(leftCallable.ReturnType, rightCallable.ReturnType),
            (MirAsyncCallableType leftCallable, MirAsyncCallableType rightCallable) => leftCallable.Parameters.Count == rightCallable.Parameters.Count
                && leftCallable.Parameters.Zip(rightCallable.Parameters).All(pair => AreEquivalent(pair.First.Type, pair.Second.Type))
                && AreEquivalent(leftCallable.EventualReturnType, rightCallable.EventualReturnType),
            (MirIterableType leftIterable, MirIterableType rightIterable) => AreEquivalent(leftIterable.ElementType, rightIterable.ElementType),
            (MirType leftNamed, MirType rightNamed) when left is not MirArrayType and not MirResultType and not MirAsyncType and not MirIterableType && right is not MirArrayType and not MirResultType and not MirAsyncType and not MirIterableType
                => leftNamed.Identifier == rightNamed.Identifier
                    || (leftNamed.Identifier is "float" or "number" && rightNamed.Identifier is "float" or "number"),
            (MirArrayType leftArray, MirArrayType rightArray) => AreEquivalent(leftArray.ElementType, rightArray.ElementType),
            (MirResultType leftResult, MirResultType rightResult) => AreEquivalent(leftResult.SuccessType, rightResult.SuccessType) && AreEquivalent(leftResult.ErrorType, rightResult.ErrorType),
            (MirAsyncType leftAsync, MirAsyncType rightAsync) => AreEquivalent(leftAsync.EventualType, rightAsync.EventualType),
            _ => false
        };

    public static bool ContainsResult(MirType type)
        => type switch
        {
            MirResultType => true,
            MirAsyncType async => ContainsResult(async.EventualType),
            MirIterableType iterable => ContainsResult(iterable.ElementType),
            MirArrayType array => ContainsResult(array.ElementType),
            MirCallableType callable => callable.Parameters.Any(parameter => ContainsResult(parameter.Type)) || ContainsResult(callable.ReturnType),
            MirAsyncCallableType callable => callable.Parameters.Any(parameter => ContainsResult(parameter.Type)) || ContainsResult(callable.EventualReturnType),
            _ => false
        };
}

public static class MirTypeText
{
    public static string FormatArrayElement(MirType type) => type is MirResultType or MirCallableType or MirAsyncCallableType ? $"({type.Name})" : type.Name;
    public static string FormatResultComponent(MirType type) => type is MirResultType or MirCallableType or MirAsyncCallableType ? $"({type.Name})" : type.Name;
}

public abstract record MirExpression(MirType Type);
public sealed record MirLiteralExpression(object? Value, MirType Type) : MirExpression(Type);
public sealed record MirUnitExpression() : MirExpression(new MirNamedType("void"));
public sealed record MirVariableExpression(string Name, MirType Type) : MirExpression(Type);
public sealed record MirAssignmentExpression(string Name, MirExpression Expression, MirType Type) : MirExpression(Type);
public sealed record MirUnaryExpression(string Operator, MirExpression Operand, MirType Type) : MirExpression(Type);
public sealed record MirAwaitExpression(MirExpression Operand, MirType Type) : MirExpression(Type);
public sealed record MirBinaryExpression(string Operator, MirExpression Left, MirExpression Right, MirType Type) : MirExpression(Type);
public enum MirNumericConversionKind { StringFrom, IntToFloat, IntFloor, IntCeil, IntRound, IntTruncate }
public sealed record MirNumericConversionExpression(MirNumericConversionKind Kind, MirExpression Operand, MirType Type) : MirExpression(Type);
public sealed record MirCallExpression(string FunctionName, IReadOnlyList<MirExpression> Arguments, MirType Type) : MirExpression(Type);
public sealed record MirFunctionReferenceExpression(string FunctionName, MirCallableType CallableType) : MirExpression(CallableType);
public sealed record MirCallableConstructionExpression(string CodeFunctionName, IReadOnlyList<MirExpression> Captures, MirCallableType CallableType) : MirExpression(CallableType);
public sealed record MirInvokeExpression(MirExpression Callee, IReadOnlyList<MirExpression> Arguments, MirType Type) : MirExpression(Type);
public sealed record MirArrayExpression(IReadOnlyList<MirExpression> Elements, MirType Type) : MirExpression(Type);
public sealed record MirArrayLengthExpression(MirExpression Receiver) : MirExpression(new MirNamedType("int"));
public sealed record MirArrayElementAccessExpression(MirExpression Receiver, MirExpression Index, MirType Type) : MirExpression(Type);
public sealed record MirArrayIterableExpression(MirExpression Receiver, MirIterableType IterableType) : MirExpression(IterableType);
public sealed record MirBatchExpression(
    MirExpression Input,
    MirLocal Item,
    MirValueBlock Body,
    MirArrayType ArrayType) : MirExpression(ArrayType);
public sealed class MirRecordFieldValue(MirRecordFieldId fieldId, MirExpression value)
{
    public MirRecordFieldId FieldId { get; } = fieldId;
    public MirExpression Value { get; } = value;
}
public sealed record MirRecordConstructionExpression(MirRecordTypeId RecordTypeId, IReadOnlyList<MirRecordFieldValue> Initializers, MirType Type) : MirExpression(Type);
public sealed record MirRecordFieldAccessExpression(MirExpression Receiver, MirRecordTypeId RecordTypeId, MirRecordFieldId FieldId, MirType Type) : MirExpression(Type);
public sealed record MirTableReferenceExpression(MirTableId TableId, MirType Type) : MirExpression(Type);
public sealed record MirTableColumnAccessExpression(MirExpression Receiver, MirTableId TableId, MirTableColumnId ColumnId, MirType Type) : MirExpression(Type);
public sealed record MirTableRowAccessExpression(MirExpression Receiver, MirExpression Index, MirTableId TableId, MirType Type) : MirExpression(Type);
public sealed record MirColumnElementAccessExpression(MirExpression Receiver, MirExpression Index, MirType Type) : MirExpression(Type);
public sealed record MirTableRowFieldAccessExpression(MirExpression Receiver, string RowTypeId, string FieldId, MirType Type) : MirExpression(Type);
public sealed record MirRecordWithExpression(MirExpression Source, MirRecordTypeId RecordTypeId, IReadOnlyList<MirRecordFieldValue> Replacements, MirType Type) : MirExpression(Type);
public sealed record MirEnumValueExpression(string EnumName, string CaseName, IReadOnlyList<MirExpression> Arguments, MirType Type) : MirExpression(Type);
public sealed record MirMatchExpression(MirExpression Scrutinee, IReadOnlyList<MirMatchArm> Arms, MirType Type) : MirExpression(Type);
public sealed record MirIfExpression(MirExpression Condition, MirExpression ThenExpression, MirExpression ElseExpression, MirType Type) : MirExpression(Type);
public sealed record MirTsonEncodeExpression(MirExpression Operand, MirTsonEncodingPlanId PlanId, MirResultType ResultType) : MirExpression(ResultType);
public sealed record MirTsonTransportExpression(
    MirExpression Operation,
    MirExpression Request,
    MirTsonEncodingPlanId RequestPlanId,
    MirTsonEncodingPlanId ResponsePlanId,
    MirTsonEncodingPlanId RemoteErrorPlanId,
    MirAsyncType AsyncType) : MirExpression(AsyncType);
public sealed record MirNpmImport(
    string PackageName,
    string PackageVersion,
    string ExportName,
    string LocalBinding,
    bool IsPromise,
    bool IsAvailableToJavaScript,
    bool IsAvailableToClrSidecar);
public sealed record MirNpmCallExpression(
    string LocalBinding,
    string PackageName,
    string PackageVersion,
    string ExportName,
    IReadOnlyList<MirExpression> Arguments,
    MirExpression ArgumentTuple,
    MirTsonEncodingPlanId RequestPlanId,
    MirTsonEncodingPlanId ResponsePlanId,
    MirTsonEncodingPlanId RemoteErrorPlanId,
    MirRecordFieldId ResponseValueFieldId,
    MirRecordFieldId RemoteErrorValueFieldId,
    MirAsyncType AsyncType) : MirExpression(AsyncType);
public sealed record MirNpmDirectCallExpression(
    string LocalBinding,
    string PackageName,
    string PackageVersion,
    string ExportName,
    IReadOnlyList<MirExpression> Arguments,
    MirType ResultType) : MirExpression(ResultType);
public sealed record MirJavaScriptHostImport(
    string ModuleSpecifier,
    string ExportName,
    string LocalBinding);
public sealed record MirPackageImport(
    string PackageId,
    string ModuleSpecifier,
    string NominalScope,
    string ExportName,
    string AssemblyIdentity,
    string ClrType,
    string ClrMethod,
    string LocalBinding);
public sealed record MirJavaScriptHostCallExpression(
    string LocalBinding,
    string ModuleSpecifier,
    string ExportName,
    IReadOnlyList<MirExpression> Arguments,
    MirType Type) : MirExpression(Type);
public sealed record MirClrMemberIdentity(
    string AssemblyIdentity,
    string Namespace,
    string DeclaringType,
    string MemberName,
    bool IsStatic,
    bool IsConstructor,
    IReadOnlyList<MirType> ParameterTypes,
    MirType ResultType,
    IReadOnlyList<MirType> GenericArguments);
public sealed record MirClrInvocationExpression(
    MirClrMemberIdentity Member,
    MirExpression? Receiver,
    IReadOnlyList<MirExpression> Arguments,
    MirType Type) : MirExpression(Type);
public sealed record MirClrPropertyAccessExpression(
    MirClrMemberIdentity Property,
    MirExpression? Receiver,
    MirType Type) : MirExpression(Type);

public sealed record MirOkExpression : MirExpression
{
    public MirOkExpression(MirExpression payload, MirResultType type) : base(type)
    {
        if (!MirTypeFacts.AreEquivalent(payload.Type, type.SuccessType)) throw new ArgumentException("Result success payload type does not match the Result success type.", nameof(payload));
        Payload = payload;
    }
    public MirExpression Payload { get; }
}

public sealed record MirErrExpression : MirExpression
{
    public MirErrExpression(MirExpression payload, MirResultType type) : base(type)
    {
        if (!MirTypeFacts.AreEquivalent(payload.Type, type.ErrorType)) throw new ArgumentException("Result error payload type does not match the Result error type.", nameof(payload));
        Payload = payload;
    }
    public MirExpression Payload { get; }
}

public sealed class MirResultBinding(string name, MirType type) { public string Name { get; } = name; public MirType Type { get; } = type; }
public sealed record MirResultMatchExpression : MirExpression
{
    public MirResultMatchExpression(MirExpression scrutinee, MirResultBinding okBinding, MirExpression okExpression, MirResultBinding errBinding, MirExpression errExpression, MirType type) : base(type)
    {
        if (scrutinee.Type is not MirResultType resultType) throw new ArgumentException("Result match scrutinee must have a Result type.", nameof(scrutinee));
        if (!MirTypeFacts.AreEquivalent(okBinding.Type, resultType.SuccessType) || !MirTypeFacts.AreEquivalent(errBinding.Type, resultType.ErrorType)) throw new ArgumentException("Result match bindings do not match the Result type.");
        if (!MirTypeFacts.AreEquivalent(okExpression.Type, type) || !MirTypeFacts.AreEquivalent(errExpression.Type, type)) throw new ArgumentException("Result match arm types do not match the match result type.");
        Scrutinee = scrutinee; OkBinding = okBinding; OkExpression = okExpression; ErrBinding = errBinding; ErrExpression = errExpression;
    }
    public MirExpression Scrutinee { get; }
    public MirResultBinding OkBinding { get; }
    public MirExpression OkExpression { get; }
    public MirResultBinding ErrBinding { get; }
    public MirExpression ErrExpression { get; }
}

public readonly record struct MirHandlerId(int Value)
{
    public override string ToString() => $"h{Value}";
}

public abstract record MirPropagationTarget
{
    public sealed record FunctionReturn : MirPropagationTarget;
    public sealed record LexicalExcept(MirHandlerId HandlerId) : MirPropagationTarget;
}
public sealed record MirPropagateExpression : MirExpression
{
    public MirPropagateExpression(MirExpression operand, MirPropagationTarget target, MirType type) : base(type)
    {
        if (operand.Type is not MirResultType resultType || !MirTypeFacts.AreEquivalent(resultType.SuccessType, type)) throw new ArgumentException("Propagation must consume a Result and yield its success type.", nameof(operand));
        Operand = operand; Target = target;
    }
    public MirExpression Operand { get; }
    public MirPropagationTarget Target { get; }
}

public sealed record MirUnwrapExpression : MirExpression
{
    public MirUnwrapExpression(MirExpression operand, MirType type) : base(type)
    {
        if (operand.Type is not MirResultType resultType || !MirTypeFacts.AreEquivalent(resultType.SuccessType, type))
        {
            throw new ArgumentException("Unwrap must consume a Result and yield its success type.", nameof(operand));
        }

        Operand = operand;
    }

    public MirExpression Operand { get; }
    public MirResultType ResultType => (MirResultType)Operand.Type;
}

public sealed class MirValueBlock
{
    public MirValueBlock(IReadOnlyList<MirStatement> prefixStatements, MirExpression valueExpression)
    {
        PrefixStatements = prefixStatements;
        ValueExpression = valueExpression;
    }

    public IReadOnlyList<MirStatement> PrefixStatements { get; }
    public MirExpression ValueExpression { get; }
    public MirType Type => ValueExpression.Type;
}

public sealed class MirTryBinding(string name, MirType type)
{
    public string Name { get; } = name;
    public MirType Type { get; } = type;
}

public sealed record MirTryExpression : MirExpression
{
    public MirTryExpression(
        MirHandlerId handlerId,
        MirValueBlock protectedBlock,
        MirTryBinding handlerBinding,
        MirType handledErrorType,
        MirValueBlock handlerBlock,
        MirType type) : base(type)
    {
        if (!MirTypeFacts.AreEquivalent(protectedBlock.Type, type)
            || !MirTypeFacts.AreEquivalent(handlerBlock.Type, type))
        {
            throw new ArgumentException("Try protected and handler value blocks must match the try expression type.");
        }

        if (!MirTypeFacts.AreEquivalent(handlerBinding.Type, handledErrorType))
        {
            throw new ArgumentException("Try handler binding type must match the handled error type.");
        }

        HandlerId = handlerId;
        Protected = protectedBlock;
        HandlerBinding = handlerBinding;
        HandledErrorType = handledErrorType;
        Handler = handlerBlock;
    }

    public MirHandlerId HandlerId { get; }
    public MirValueBlock Protected { get; }
    public MirTryBinding HandlerBinding { get; }
    public MirType HandledErrorType { get; }
    public MirValueBlock Handler { get; }
}

public sealed class MirMatchArm(string caseName, IReadOnlyList<MirMatchPayloadBinding> payloadBindings, MirExpression expression)
{
    public string CaseName { get; } = caseName; public IReadOnlyList<MirMatchPayloadBinding> PayloadBindings { get; } = payloadBindings; public MirExpression Expression { get; } = expression;
}
public sealed class MirMatchPayloadBinding(string name, MirType type) { public string Name { get; } = name; public MirType Type { get; } = type; }
