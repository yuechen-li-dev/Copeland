namespace Copeland.TS.Mir;

/// <summary>Typed native calls retain ordinary call argument traversal and ordering.</summary>
public enum MirNativeOperation
{
    StringSplit,
    StringIndexOf,
    StringCodeAt,
    StringSlice,
    StringJoin,
    MutableArrayFilled,
}

public static class MirNativeOperations
{
    public static (IReadOnlyList<MirType> Parameters, MirType Result) Signature(MirNativeOperation operation)
    {
        MirType text = new MirNamedType("string");
        MirType integer = new MirNamedType("int");
        MirType texts = new MirArrayType(text);
        return operation switch
        {
            MirNativeOperation.StringSplit => ([text, text], texts),
            MirNativeOperation.StringIndexOf => ([text, text], integer),
            MirNativeOperation.StringCodeAt => ([text, integer], integer),
            MirNativeOperation.StringSlice => ([text, integer, integer], text),
            MirNativeOperation.StringJoin => ([texts, text], text),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    public static bool IsValid(MirCallExpression call)
    {
        if (call.NativeOperation is not MirNativeOperation operation || !Enum.IsDefined(operation))
        {
            return false;
        }
        if (operation == MirNativeOperation.MutableArrayFilled)
        {
            return call.Type is MirMutableArrayType array && call.Arguments.Count == 2
                && call.Arguments[0].Type.Identifier == "int"
                && MirTypeFacts.AreEquivalent(array.ElementType, call.Arguments[1].Type);
        }
        var signature = Signature(operation);
        return MirTypeFacts.AreEquivalent(call.Type, signature.Result)
            && call.Arguments.Count == signature.Parameters.Count
            && call.Arguments.Select((argument, index) =>
                MirTypeFacts.AreEquivalent(argument.Type, signature.Parameters[index])).All(matches => matches);
    }
}
