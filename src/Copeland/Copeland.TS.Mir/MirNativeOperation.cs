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
    StringFromCode,
    MutableListCreate,
    MutableListAppend,
    MatchCase,
    MatchPayload,
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
            MirNativeOperation.StringFromCode => ([integer], text),
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
            return call.Type is MirMutableArrayType { IsGrowable: false } array && call.Arguments.Count == 2
                && call.Arguments[0].Type.Identifier == "int"
                && MirTypeFacts.AreEquivalent(array.ElementType, call.Arguments[1].Type);
        }
        if (operation == MirNativeOperation.MutableListCreate)
        {
            return call.Type is MirMutableArrayType { IsGrowable: true } && call.Arguments.Count == 0;
        }
        if (operation == MirNativeOperation.MutableListAppend)
        {
            return call.Type.Identifier == "void" && call.Arguments.Count == 2
                && call.Arguments[0].Type is MirMutableArrayType { IsGrowable: true } list
                && MirTypeFacts.AreEquivalent(list.ElementType, call.Arguments[1].Type);
        }
        if (operation is MirNativeOperation.MatchCase or MirNativeOperation.MatchPayload)
        {
            if (call.Arguments.Count != (operation == MirNativeOperation.MatchCase ? 2 : 4)
                || call.Arguments[0].Type is not (MirNamedType or MirResultType)
                || call.Arguments[1] is not MirLiteralExpression { Value: string caseName, Type.Identifier: "string" })
            {
                return false;
            }
            if (call.Arguments[0].Type is MirResultType && caseName is not ("ok" or "err"))
            {
                return false;
            }
            if (operation == MirNativeOperation.MatchCase)
            {
                return call.Type.Identifier == "boolean";
            }
            if (call.Arguments[2] is not MirLiteralExpression { Value: string, Type.Identifier: "string" }
                || call.Arguments[3] is not MirLiteralExpression { Value: int index, Type.Identifier: "int" } || index < 0)
            {
                return false;
            }
            return call.Arguments[0].Type is not MirResultType result
                || index == 0 && MirTypeFacts.AreEquivalent(call.Type, caseName == "ok" ? result.SuccessType : result.ErrorType);
        }
        var signature = Signature(operation);
        return MirTypeFacts.AreEquivalent(call.Type, signature.Result)
            && call.Arguments.Count == signature.Parameters.Count
            && call.Arguments.Select((argument, index) =>
                MirTypeFacts.AreEquivalent(argument.Type, signature.Parameters[index])).All(matches => matches);
    }
}
