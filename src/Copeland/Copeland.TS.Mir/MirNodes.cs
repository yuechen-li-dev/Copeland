namespace Copeland.TS.Mir;

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

public sealed class MirFunction(string name, IReadOnlyList<MirParameter> parameters, MirType returnType, IReadOnlyList<MirLocal> locals, IReadOnlyList<MirStatement> body)
{
    public string Name { get; } = name;
    public IReadOnlyList<MirParameter> Parameters { get; } = parameters;
    public MirType ReturnType { get; } = returnType;
    public bool IsFallible => ReturnType is MirResultType;
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

public record MirType(string Identifier)
{
    public virtual string Name => Identifier;
}

public sealed record MirNamedType(string Identifier) : MirType(Identifier);
public sealed record MirArrayType(MirType ElementType) : MirType("array") { public override string Name => MirTypeText.FormatArrayElement(ElementType) + "[]"; }
public sealed record MirResultType(MirType SuccessType, MirType ErrorType) : MirType("result") { public override string Name => $"{MirTypeText.FormatResultComponent(SuccessType)} ! {ErrorType.Name}"; }

public static class MirTypeFacts
{
    public static bool AreEquivalent(MirType left, MirType right)
        => (left, right) switch
        {
            (MirType leftNamed, MirType rightNamed) when left is not MirArrayType and not MirResultType && right is not MirArrayType and not MirResultType => leftNamed.Identifier == rightNamed.Identifier,
            (MirArrayType leftArray, MirArrayType rightArray) => AreEquivalent(leftArray.ElementType, rightArray.ElementType),
            (MirResultType leftResult, MirResultType rightResult) => AreEquivalent(leftResult.SuccessType, rightResult.SuccessType) && AreEquivalent(leftResult.ErrorType, rightResult.ErrorType),
            _ => false
        };

    public static bool ContainsResult(MirType type)
        => type switch
        {
            MirResultType => true,
            MirArrayType array => ContainsResult(array.ElementType),
            _ => false
        };
}

public static class MirTypeText
{
    public static string FormatArrayElement(MirType type) => type is MirResultType ? $"({type.Name})" : type.Name;
    public static string FormatResultComponent(MirType type) => type is MirResultType ? $"({type.Name})" : type.Name;
}

public abstract record MirExpression(MirType Type);
public sealed record MirLiteralExpression(object? Value, MirType Type) : MirExpression(Type);
public sealed record MirUnitExpression() : MirExpression(new MirNamedType("void"));
public sealed record MirVariableExpression(string Name, MirType Type) : MirExpression(Type);
public sealed record MirAssignmentExpression(string Name, MirExpression Expression, MirType Type) : MirExpression(Type);
public sealed record MirUnaryExpression(string Operator, MirExpression Operand, MirType Type) : MirExpression(Type);
public sealed record MirBinaryExpression(string Operator, MirExpression Left, MirExpression Right, MirType Type) : MirExpression(Type);
public sealed record MirCallExpression(string FunctionName, IReadOnlyList<MirExpression> Arguments, MirType Type) : MirExpression(Type);
public sealed record MirArrayExpression(IReadOnlyList<MirExpression> Elements, MirType Type) : MirExpression(Type);
public sealed record MirEnumValueExpression(string EnumName, string CaseName, IReadOnlyList<MirExpression> Arguments, MirType Type) : MirExpression(Type);
public sealed record MirMatchExpression(MirExpression Scrutinee, IReadOnlyList<MirMatchArm> Arms, MirType Type) : MirExpression(Type);
public sealed record MirIfExpression(MirExpression Condition, MirExpression ThenExpression, MirExpression ElseExpression, MirType Type) : MirExpression(Type);

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
