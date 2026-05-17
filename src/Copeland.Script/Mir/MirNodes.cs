using Copeland.Script.Semantics;

namespace Copeland.Script.Mir;

public sealed class MirCompilation
{
    public MirCompilation(MirProgram? program, IReadOnlyList<Diagnostics.Diagnostic> diagnostics)
    {
        Program = program;
        Diagnostics = diagnostics;
    }

    public MirProgram? Program { get; }
    public IReadOnlyList<Diagnostics.Diagnostic> Diagnostics { get; }
}

public sealed class MirProgram(IReadOnlyList<MirEnum> enums, IReadOnlyList<MirFunction> functions)
{
    public IReadOnlyList<MirEnum> Enums { get; } = enums;
    public IReadOnlyList<MirFunction> Functions { get; } = functions;
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
    MirType? errorType,
    IReadOnlyList<MirLocal> locals,
    IReadOnlyList<MirStatement> body)
{
    public string Name { get; } = name;
    public IReadOnlyList<MirParameter> Parameters { get; } = parameters;
    public MirType ReturnType { get; } = returnType;
    public MirType? ErrorType { get; } = errorType;
    public bool IsFallible => ErrorType is not null;
    public IReadOnlyList<MirLocal> Locals { get; } = locals;
    public IReadOnlyList<MirStatement> Body { get; } = body;
}

public sealed class MirParameter(string name, MirType type)
{
    public string Name { get; } = name;
    public MirType Type { get; } = type;
}

public sealed class MirLocal(string name, MirType type, bool isReadOnly)
{
    public string Name { get; } = name;
    public MirType Type { get; } = type;
    public bool IsReadOnly { get; } = isReadOnly;
}

public abstract record MirStatement;
public sealed record MirVariableDeclarationStatement(MirLocal Local, MirExpression Initializer) : MirStatement;
public sealed record MirExpressionStatement(MirExpression Expression) : MirStatement;
public sealed record MirReturnStatement(MirExpression? Expression) : MirStatement;
public sealed record MirIfStatement(MirExpression Condition, IReadOnlyList<MirStatement> ThenStatements, IReadOnlyList<MirStatement>? ElseStatements) : MirStatement;
public sealed record MirWhileStatement(MirExpression Condition, IReadOnlyList<MirStatement> BodyStatements) : MirStatement;
public sealed record MirForStatement(MirStatement? Initializer, MirExpression? Condition, MirExpression? Increment, IReadOnlyList<MirStatement> BodyStatements) : MirStatement;

public abstract record MirExpression(MirType Type);
public sealed record MirLiteralExpression(object? Value, MirType Type) : MirExpression(Type);
public sealed record MirVariableExpression(string Name, MirType Type) : MirExpression(Type);
public sealed record MirAssignmentExpression(string Name, MirExpression Expression, MirType Type) : MirExpression(Type);
public sealed record MirUnaryExpression(string Operator, MirExpression Operand, MirType Type) : MirExpression(Type);
public sealed record MirBinaryExpression(string Operator, MirExpression Left, MirExpression Right, MirType Type) : MirExpression(Type);
public sealed record MirCallExpression(string FunctionName, IReadOnlyList<MirExpression> Arguments, MirType Type, bool IsFallible, MirType? ErrorType, bool IsPropagated) : MirExpression(Type);
public sealed record MirArrayExpression(IReadOnlyList<MirExpression> Elements, MirType Type) : MirExpression(Type);
public sealed record MirEnumValueExpression(string EnumName, string CaseName, IReadOnlyList<MirExpression> Arguments, MirType Type) : MirExpression(Type);
public sealed record MirMatchExpression(MirExpression Scrutinee, IReadOnlyList<MirMatchArm> Arms, MirType Type) : MirExpression(Type);
public sealed record MirIfExpression(MirExpression Condition, MirExpression ThenExpression, MirExpression ElseExpression, MirType Type) : MirExpression(Type);

public sealed class MirMatchArm(string caseName, IReadOnlyList<MirMatchPayloadBinding> payloadBindings, MirExpression expression)
{
    public string CaseName { get; } = caseName;
    public IReadOnlyList<MirMatchPayloadBinding> PayloadBindings { get; } = payloadBindings;
    public MirExpression Expression { get; } = expression;
}

public sealed class MirMatchPayloadBinding(string name, MirType type)
{
    public string Name { get; } = name;
    public MirType Type { get; } = type;
}

public sealed class MirType(string name)
{
    public string Name { get; } = name;

    public static MirType From(TypeSymbol type) => new(type.Name);
}
