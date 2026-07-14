using Copeland.TS.Syntax;

namespace Copeland.TS.Semantics.Bound;

public abstract class BoundNode;
public abstract class BoundStatement : BoundNode;
public abstract class BoundExpression : BoundNode { public abstract TypeSymbol Type { get; } }

public sealed class BoundProgram
{
    public BoundProgram(
        IReadOnlyList<BoundFunctionDeclaration> functions,
        IReadOnlyList<BoundEnumDeclaration> enums,
        IReadOnlyList<BoundRecordDeclaration> records,
        IReadOnlyList<BoundStatement> globalStatements,
        IReadOnlyList<BoundTableDefinition>? tables = null,
        IReadOnlyList<BoundTsonEncodingPlan>? tsonEncodingPlans = null)
    {
        Functions = functions;
        Enums = enums;
        Records = records;
        GlobalStatements = globalStatements;
        Tables = tables ?? [];
        TsonEncodingPlans = tsonEncodingPlans ?? [];
    }
    public IReadOnlyList<BoundFunctionDeclaration> Functions { get; }
    public IReadOnlyList<BoundEnumDeclaration> Enums { get; }
    public IReadOnlyList<BoundRecordDeclaration> Records { get; }
    public IReadOnlyList<BoundStatement> GlobalStatements { get; }
    public IReadOnlyList<BoundTableDefinition> Tables { get; }
    public IReadOnlyList<BoundTsonEncodingPlan> TsonEncodingPlans { get; }
}

public sealed class BoundTsonEncodingPlan(
    string id,
    string schemaIdentity,
    TypeSymbol rootType,
    IReadOnlyList<TypeSymbol> definitions,
    BoundTsonTablePlan? tablePlan = null)
{
    public string Id { get; } = id;
    public string SchemaIdentity { get; } = schemaIdentity;
    public TypeSymbol RootType { get; } = rootType;
    public IReadOnlyList<TypeSymbol> Definitions { get; } = definitions;
    public BoundTsonTablePlan? TablePlan { get; } = tablePlan;
}

public sealed class BoundTsonTablePlan(
    TableTypeSymbol tableType,
    int expectedRowCount,
    IReadOnlyList<BoundTsonTableColumnPlan> columns)
{
    public TableTypeSymbol TableType { get; } = tableType;
    public int ExpectedRowCount { get; } = expectedRowCount;
    public IReadOnlyList<BoundTsonTableColumnPlan> Columns { get; } = columns.ToArray();
}

public sealed class BoundTsonTableColumnPlan(
    TableColumnSymbol column,
    int expectedElementCount)
{
    public TableColumnSymbol Column { get; } = column;
    public int ExpectedElementCount { get; } = expectedElementCount;
}
public sealed class BoundCompilation
{
    public BoundCompilation(SyntaxTree syntaxTree, BoundProgram program, IReadOnlyList<Diagnostics.Diagnostic> diagnostics) { SyntaxTree = syntaxTree; Program = program; Diagnostics = diagnostics; }
    public SyntaxTree SyntaxTree { get; }
    public BoundProgram Program { get; }
    public IReadOnlyList<Diagnostics.Diagnostic> Diagnostics { get; }
}

public sealed class BoundFunctionDeclaration : BoundNode { public BoundFunctionDeclaration(FunctionSymbol symbol, BoundBlockStatement body) { Symbol = symbol; Body = body; } public FunctionSymbol Symbol { get; } public BoundBlockStatement Body { get; } }
public sealed class BoundEnumDeclaration : BoundNode { public BoundEnumDeclaration(EnumTypeSymbol enumType) => EnumType = enumType; public EnumTypeSymbol EnumType { get; } }
public sealed class BoundRecordDeclaration : BoundNode { public BoundRecordDeclaration(RecordTypeSymbol recordType) => RecordType = recordType; public RecordTypeSymbol RecordType { get; } }
public sealed class BoundTableDefinition(TableTypeSymbol tableType, IReadOnlyList<BoundTableColumnDefinition> columns, int rowCount) : BoundNode
{ public TableTypeSymbol TableType { get; } = tableType; public IReadOnlyList<BoundTableColumnDefinition> Columns { get; } = columns; public int RowCount { get; } = rowCount; }
public abstract class BoundTableConstant(TypeSymbol type) : BoundNode
{
    public TypeSymbol Type { get; } = type;
}

public sealed class BoundTableLiteralConstant(object value, TypeSymbol type) : BoundTableConstant(type)
{
    public object Value { get; } = value;
}

public sealed class BoundTableArrayConstant(
    ArrayTypeSymbol arrayType,
    IReadOnlyList<BoundTableConstant> elements) : BoundTableConstant(arrayType)
{
    public ArrayTypeSymbol ArrayType { get; } = arrayType;
    public IReadOnlyList<BoundTableConstant> Elements { get; } = Array.AsReadOnly(elements.ToArray());
}

public sealed class BoundTableRecordConstant(RecordTypeSymbol recordType, IReadOnlyList<BoundTableRecordFieldConstant> fields) : BoundTableConstant(recordType)
{
    public RecordTypeSymbol RecordType { get; } = recordType;
    public IReadOnlyList<BoundTableRecordFieldConstant> Fields { get; } = fields;
}

public sealed class BoundTableRecordFieldConstant(RecordFieldSymbol field, BoundTableConstant value) : BoundNode
{
    public RecordFieldSymbol Field { get; } = field;
    public BoundTableConstant Value { get; } = value;
}

public sealed class BoundTableEnumConstant(EnumCaseSymbol @case, IReadOnlyList<BoundTableConstant> payloads) : BoundTableConstant(@case.EnumType)
{
    public EnumCaseSymbol Case { get; } = @case;
    public IReadOnlyList<BoundTableConstant> Payloads { get; } = payloads;
}

public sealed class BoundTableResultConstant(bool isOk, BoundTableConstant payload, ResultTypeSymbol type) : BoundTableConstant(type)
{
    public bool IsOk { get; } = isOk;
    public BoundTableConstant Payload { get; } = payload;
}

public sealed class BoundTableColumnDefinition(TableColumnSymbol column, IReadOnlyList<BoundTableConstant> cells) : BoundNode
{ public TableColumnSymbol Column { get; } = column; public IReadOnlyList<BoundTableConstant> Cells { get; } = cells; }
public sealed class BoundBlockStatement : BoundStatement { public BoundBlockStatement(IReadOnlyList<BoundStatement> statements) => Statements = statements; public IReadOnlyList<BoundStatement> Statements { get; } }
public sealed class BoundVariableDeclaration : BoundStatement { public BoundVariableDeclaration(VariableSymbol variable, BoundExpression initializer) { Variable = variable; Initializer = initializer; } public VariableSymbol Variable { get; } public BoundExpression Initializer { get; } }
public sealed class BoundExpressionStatement : BoundStatement { public BoundExpressionStatement(BoundExpression expression) => Expression = expression; public BoundExpression Expression { get; } }
public sealed class BoundIfStatement : BoundStatement { public BoundIfStatement(BoundExpression condition, BoundStatement thenStatement, BoundStatement? elseStatement) { Condition = condition; ThenStatement = thenStatement; ElseStatement = elseStatement; } public BoundExpression Condition { get; } public BoundStatement ThenStatement { get; } public BoundStatement? ElseStatement { get; } }
public sealed class BoundWhileStatement : BoundStatement { public BoundWhileStatement(BoundExpression condition, BoundStatement body) { Condition = condition; Body = body; } public BoundExpression Condition { get; } public BoundStatement Body { get; } }
public sealed class BoundForStatement : BoundStatement { public BoundForStatement(BoundStatement? initializer, BoundExpression? condition, BoundExpression? increment, BoundStatement body) { Initializer = initializer; Condition = condition; Increment = increment; Body = body; } public BoundStatement? Initializer { get; } public BoundExpression? Condition { get; } public BoundExpression? Increment { get; } public BoundStatement Body { get; } }
public sealed class BoundReturnStatement : BoundStatement { public BoundReturnStatement(BoundExpression? expression) => Expression = expression; public BoundExpression? Expression { get; } }

public sealed class BoundLiteralExpression : BoundExpression { public BoundLiteralExpression(object? value, TypeSymbol type) { Value = value; TypeImpl = type; } public object? Value { get; } private TypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundVariableExpression : BoundExpression { public BoundVariableExpression(VariableSymbol variable) => Variable = variable; public VariableSymbol Variable { get; } public override TypeSymbol Type => Variable.Type; }
public sealed class BoundAssignmentExpression : BoundExpression { public BoundAssignmentExpression(VariableSymbol variable, BoundExpression expression) { Variable = variable; Expression = expression; } public VariableSymbol Variable { get; } public BoundExpression Expression { get; } public override TypeSymbol Type => Expression.Type; }
public sealed class BoundUnaryExpression : BoundExpression { public BoundUnaryExpression(SyntaxKind op, BoundExpression operand, TypeSymbol type) { OperatorKind = op; Operand = operand; TypeImpl = type; } public SyntaxKind OperatorKind { get; } public BoundExpression Operand { get; } private TypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundBinaryExpression : BoundExpression { public BoundBinaryExpression(BoundExpression left, SyntaxKind op, BoundExpression right, TypeSymbol type) { Left = left; OperatorKind = op; Right = right; TypeImpl = type; } public BoundExpression Left { get; } public SyntaxKind OperatorKind { get; } public BoundExpression Right { get; } private TypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundCallExpression : BoundExpression { public BoundCallExpression(FunctionSymbol function, IReadOnlyList<BoundExpression> arguments) { Function = function; Arguments = arguments; } public FunctionSymbol Function { get; } public IReadOnlyList<BoundExpression> Arguments { get; } public override TypeSymbol Type => Function.ReturnType; }
public sealed class BoundEnumValueExpression : BoundExpression
{
    public BoundEnumValueExpression(EnumCaseSymbol @case, IReadOnlyList<BoundExpression> arguments)
    {
        Case = @case;
        Arguments = arguments;
    }
    public EnumCaseSymbol Case { get; }
    public IReadOnlyList<BoundExpression> Arguments { get; }
    public bool IsConstructor => Arguments.Count > 0;
    public override TypeSymbol Type => Case.EnumType;
}
public readonly record struct BoundHandlerId(int Value)
{
    public override string ToString() => $"h{Value}";
}

public abstract record BoundPropagationTarget
{
    public sealed record FunctionReturn : BoundPropagationTarget;
    public sealed record LexicalExcept(BoundHandlerId HandlerId) : BoundPropagationTarget;
}
public sealed class BoundPropagateExpression : BoundExpression
{
    public BoundPropagateExpression(BoundExpression operand, ResultTypeSymbol resultType, BoundPropagationTarget target)
    {
        Operand = operand;
        ResultType = resultType;
        Target = target;
    }

    public BoundExpression Operand { get; }
    public ResultTypeSymbol ResultType { get; }
    public BoundPropagationTarget Target { get; }
    public override TypeSymbol Type => ResultType.SuccessType;
}
public sealed class BoundUnwrapExpression : BoundExpression
{
    public BoundUnwrapExpression(BoundExpression operand, ResultTypeSymbol resultType)
    {
        Operand = operand;
        ResultType = resultType;
    }

    public BoundExpression Operand { get; }
    public ResultTypeSymbol ResultType { get; }
    public override TypeSymbol Type => ResultType.SuccessType;
}
public sealed class BoundValueBlock
{
    public BoundValueBlock(IReadOnlyList<BoundStatement> prefixStatements, BoundExpression valueExpression)
    {
        PrefixStatements = prefixStatements;
        ValueExpression = valueExpression;
    }

    public IReadOnlyList<BoundStatement> PrefixStatements { get; }
    public BoundExpression ValueExpression { get; }
    public TypeSymbol Type => ValueExpression.Type;
}

public sealed class BoundTryExceptExpression : BoundExpression
{
    public BoundTryExceptExpression(
        BoundHandlerId handlerId,
        BoundValueBlock protectedBlock,
        VariableSymbol handlerBinding,
        TypeSymbol handledErrorType,
        BoundValueBlock handlerBlock,
        TypeSymbol type)
    {
        HandlerId = handlerId;
        Protected = protectedBlock;
        HandlerBinding = handlerBinding;
        HandledErrorType = handledErrorType;
        Handler = handlerBlock;
        TypeImpl = type;
    }

    public BoundHandlerId HandlerId { get; }
    public BoundValueBlock Protected { get; }
    public VariableSymbol HandlerBinding { get; }
    public TypeSymbol HandledErrorType { get; }
    public BoundValueBlock Handler { get; }
    private TypeSymbol TypeImpl { get; }
    public override TypeSymbol Type => TypeImpl;
}
public sealed class BoundOkExpression : BoundExpression { public BoundOkExpression(BoundExpression payload, ResultTypeSymbol type) { Payload = payload; TypeImpl = type; } public BoundExpression Payload { get; } private ResultTypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundErrExpression : BoundExpression { public BoundErrExpression(BoundExpression payload, ResultTypeSymbol type) { Payload = payload; TypeImpl = type; } public BoundExpression Payload { get; } private ResultTypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundUnitExpression : BoundExpression { public override TypeSymbol Type => PrimitiveTypeSymbol.Void; }
public sealed class BoundMatchArm
{
    public BoundMatchArm(EnumCaseSymbol @case, IReadOnlyList<VariableSymbol> payloadVariables, BoundExpression expression)
    {
        Case = @case;
        PayloadVariables = payloadVariables;
        Expression = expression;
    }
    public EnumCaseSymbol Case { get; }
    public IReadOnlyList<VariableSymbol> PayloadVariables { get; }
    public BoundExpression Expression { get; }
}
public sealed class BoundIfExpression : BoundExpression { public BoundIfExpression(BoundExpression condition, BoundExpression thenExpression, BoundExpression elseExpression, TypeSymbol type) { Condition = condition; ThenExpression = thenExpression; ElseExpression = elseExpression; TypeImpl = type; } public BoundExpression Condition { get; } public BoundExpression ThenExpression { get; } public BoundExpression ElseExpression { get; } private TypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundTsonEncodeExpression(BoundExpression operand, BoundTsonEncodingPlan plan, ResultTypeSymbol resultType) : BoundExpression
{
    public BoundExpression Operand { get; } = operand;
    public BoundTsonEncodingPlan Plan { get; } = plan;
    public ResultTypeSymbol ResultType { get; } = resultType;
    public override TypeSymbol Type => ResultType;
}
public sealed class BoundMatchExpression : BoundExpression
{
    public BoundMatchExpression(BoundExpression scrutinee, EnumTypeSymbol enumType, IReadOnlyList<BoundMatchArm> arms, TypeSymbol type)
    {
        Scrutinee = scrutinee;
        EnumType = enumType;
        Arms = arms;
        TypeImpl = type;
    }
    public BoundExpression Scrutinee { get; }
    public EnumTypeSymbol EnumType { get; }
    public IReadOnlyList<BoundMatchArm> Arms { get; }
    private TypeSymbol TypeImpl { get; }
    public override TypeSymbol Type => TypeImpl;
}
public sealed class BoundResultMatchExpression : BoundExpression
{
    public BoundResultMatchExpression(BoundExpression scrutinee, VariableSymbol okVariable, BoundExpression okExpression, VariableSymbol errVariable, BoundExpression errExpression, TypeSymbol type)
    {
        Scrutinee = scrutinee;
        OkVariable = okVariable;
        OkExpression = okExpression;
        ErrVariable = errVariable;
        ErrExpression = errExpression;
        TypeImpl = type;
    }

    public BoundExpression Scrutinee { get; }
    public VariableSymbol OkVariable { get; }
    public BoundExpression OkExpression { get; }
    public VariableSymbol ErrVariable { get; }
    public BoundExpression ErrExpression { get; }
    private TypeSymbol TypeImpl { get; }
    public override TypeSymbol Type => TypeImpl;
}
public sealed class BoundArrayExpression : BoundExpression { public BoundArrayExpression(IReadOnlyList<BoundExpression> elements, TypeSymbol type) { Elements = elements; TypeImpl = type; } public IReadOnlyList<BoundExpression> Elements { get; } private TypeSymbol TypeImpl { get; } public override TypeSymbol Type => TypeImpl; }
public sealed class BoundRecordFieldInitializer(RecordFieldSymbol field, BoundExpression value)
{
    public RecordFieldSymbol Field { get; } = field;
    public BoundExpression Value { get; } = value;
}
public sealed class BoundRecordConstructionExpression(RecordTypeSymbol recordType, IReadOnlyList<BoundRecordFieldInitializer> initializers) : BoundExpression
{
    public RecordTypeSymbol RecordType { get; } = recordType;
    public IReadOnlyList<BoundRecordFieldInitializer> Initializers { get; } = initializers;
    public override TypeSymbol Type => RecordType;
}
public sealed class BoundRecordFieldAccessExpression(BoundExpression receiver, RecordTypeSymbol recordType, RecordFieldSymbol field) : BoundExpression
{
    public BoundExpression Receiver { get; } = receiver;
    public RecordTypeSymbol RecordType { get; } = recordType;
    public RecordFieldSymbol Field { get; } = field;
    public override TypeSymbol Type => Field.Type;
}
public sealed class BoundTableReferenceExpression(TableTypeSymbol tableType) : BoundExpression { public TableTypeSymbol TableType { get; } = tableType; public override TypeSymbol Type => TableType; }
public sealed class BoundTableColumnAccessExpression(BoundExpression receiver, TableTypeSymbol tableType, TableColumnSymbol column) : BoundExpression { public BoundExpression Receiver { get; } = receiver; public TableTypeSymbol TableType { get; } = tableType; public TableColumnSymbol Column { get; } = column; public override TypeSymbol Type => new ColumnTypeSymbol(Column.Type); }
public sealed class BoundTableRowAccessExpression(BoundExpression receiver, BoundExpression index, TableTypeSymbol tableType, ResultTypeSymbol type) : BoundExpression { public BoundExpression Receiver { get; } = receiver; public BoundExpression Index { get; } = index; public TableTypeSymbol TableType { get; } = tableType; private ResultTypeSymbol TypeImpl { get; } = type; public override TypeSymbol Type => TypeImpl; }
public sealed class BoundColumnElementAccessExpression(BoundExpression receiver, BoundExpression index, ResultTypeSymbol type) : BoundExpression { public BoundExpression Receiver { get; } = receiver; public BoundExpression Index { get; } = index; private ResultTypeSymbol TypeImpl { get; } = type; public override TypeSymbol Type => TypeImpl; }
public sealed class BoundTableRowFieldAccessExpression(BoundExpression receiver, TableRowTypeSymbol rowType, TableRowFieldSymbol field) : BoundExpression { public BoundExpression Receiver { get; } = receiver; public TableRowTypeSymbol RowType { get; } = rowType; public TableRowFieldSymbol Field { get; } = field; public override TypeSymbol Type => Field.Type; }
public sealed class BoundRecordWithExpression(BoundExpression source, RecordTypeSymbol recordType, IReadOnlyList<BoundRecordFieldInitializer> replacements) : BoundExpression
{
    public BoundExpression Source { get; } = source;
    public RecordTypeSymbol RecordType { get; } = recordType;
    public IReadOnlyList<BoundRecordFieldInitializer> Replacements { get; } = replacements;
    public override TypeSymbol Type => RecordType;
}
public sealed class BoundErrorExpression : BoundExpression { public override TypeSymbol Type => PrimitiveTypeSymbol.Error; }
