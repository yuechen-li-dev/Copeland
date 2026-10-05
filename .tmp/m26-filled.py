from pathlib import Path
def edit(path,old,new):
 p=Path(path);s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old[:60]);p.write_text(s.replace(old,new),encoding='utf-8',newline='\n')
f='src/Copeland/Copeland.TS.Mir/MirNativeOperation.cs';edit(f,'    StringJoin,','    StringJoin,\n    MutableArrayFilled,')
edit(f,'        var signature = Signature(operation);','''        if (operation == MirNativeOperation.MutableArrayFilled)
        {
            return call.Type is MirMutableArrayType array && call.Arguments.Count == 2
                && call.Arguments[0].Type.Identifier == "int"
                && MirTypeFacts.AreEquivalent(array.ElementType, call.Arguments[1].Type);
        }
        var signature = Signature(operation);''')
b='src/Copeland/Copeland.TS/Semantics/Binder.cs'
edit(b,'call.TypeArguments.Count != 1 || call.Arguments.Count != 1','call.TypeArguments.Count != 1 || call.Arguments.Count is not 1 and not 2')
edit(b,'MutableArray<T>(length) expects one element type and one int length.','MutableArray<T>(length[, initializer]) expects one element type, an int length, and optionally an immutable initializer value.')
edit(b,'if (!TypeFacts.IsNumeric(elementType) && elementType != PrimitiveTypeSymbol.Boolean)','if (call.Arguments.Count == 1 && !TypeFacts.IsNumeric(elementType) && elementType != PrimitiveTypeSymbol.Boolean)')
edit(b,'                return new BoundMutableArrayConstructionExpression(length, new MutableArrayTypeSymbol(elementType));','''                var arrayType = new MutableArrayTypeSymbol(elementType);
                if (call.Arguments.Count == 2)
                {
                    if (!IsBatchPortableType(elementType) || elementType is ArrayTypeSymbol)
                    {
                        Report("COPE-ARRAY-0008", "MutableArray initializer elements must be primitive values, strings, or immutable records. Use columnar arrays for computational storage.", call.LessToken);
                        return new BoundErrorExpression();
                    }
                    BoundExpression initializer = BindExpression(call.Arguments[1], elementType);
                    if (!IsAssignable(elementType, initializer.Type))
                    {
                        ReportTypeMismatch("COPE-TYPE-0005", elementType, initializer.Type, InferenceAnchor(call.Arguments[1]));
                        return new BoundErrorExpression();
                    }
                    var function = new FunctionSymbol("MutableArray", [new ParameterSymbol("length", PrimitiveTypeSymbol.Int), new ParameterSymbol("initializer", elementType)], arrayType)
                    {
                        NativeOperation = Copeland.TS.Mir.MirNativeOperation.MutableArrayFilled,
                    };
                    return new BoundCallExpression(function, [length, initializer]);
                }
                return new BoundMutableArrayConstructionExpression(length, arrayType);''')
c='src/Copeland/Copeland.TS.Backend.CSharp/CSharp/NativeStringRuntime.cs';j='src/Copeland/Copeland.TS.Backend.JavaScript/NativeStringRuntime.cs'
edit(c,'private static string[] __cope_native_StringSplit','''private static T[] __cope_native_MutableArrayFilled<T>(int length, T initializer)
{
    if (length < 0) throw new global::System.ArgumentOutOfRangeException(nameof(length), "Copeland mutable array length cannot be negative.");
    var result = new T[length];
    global::System.Array.Fill(result, initializer);
    return result;
}

private static string[] __cope_native_StringSplit''')
edit(j,'function __cope_native_StringSplit','''function __cope_native_MutableArrayFilled(length, initializer) {
    if (length < 0) throw new Error("Copeland mutable array length cannot be negative.");
    return new Array(length).fill(initializer);
}

function __cope_native_StringSplit''')
# Clamp integer unary overflow in both synchronous and async JavaScript emission.
j='src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs'
edit(j,'        return new EmittedExpression(operand.Prelude, $"({unary.Operator}{operand.Value})");','        string value = $"({unary.Operator}{operand.Value})";\n        if (unary.Type.Identifier == "int" && unary.Operator == "-") value = $"({value} | 0)";\n        return new EmittedExpression(operand.Prelude, value);')
# Actual authored file is retained in repair diagnostics as well as spans.
b='src/Copeland/Copeland.TS/Semantics/Binder.cs'
edit(b,'_diagnostics.Report(id, message, token.Position, token.Text.Length, suggestedReplacement:', '_diagnostics.Report(id, message, token.Position, token.Text.Length, _sourcePath, suggestedReplacement:')
edit(b,'_diagnostics.Report(id, msg, at.Position, at.Text.Length);','_diagnostics.Report(id, msg, at.Position, at.Text.Length, _sourcePath);')
# Canonical MIR text uses LF regardless of host.
f='src/Copeland/Copeland.TS.Mir/MirTextWriter.cs';edit(f,'        return sb.ToString();','        return sb.ToString().Replace("\\r\\n", "\\n", StringComparison.Ordinal);')
