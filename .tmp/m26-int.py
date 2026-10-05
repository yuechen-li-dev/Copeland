from pathlib import Path
def edit(path,old,new):
 p=Path(path);s=p.read_text(encoding='utf-8-sig');assert old in s,(path,old[:60]);p.write_text(s.replace(old,new),encoding='utf-8',newline='\n')
b='src/Copeland/Copeland.TS/Semantics/Binder.cs'
edit(b,'new BoundAssignmentExpression(RewriteVariable(assignment.Variable), RewriteExpression(assignment.Expression))','new BoundAssignmentExpression(RewriteVariable(assignment.Variable), RewriteExpression(assignment.Expression)) { ReturnsPreviousValue = assignment.ReturnsPreviousValue }')
mir='src/Copeland/Copeland.TS.Mir/MirNodes.cs'
edit(mir,'public sealed record MirAssignmentExpression(string Name, MirExpression Expression, MirType Type) : MirExpression(Type);','''public sealed record MirAssignmentExpression(string Name, MirExpression Expression, MirType Type) : MirExpression(Type)
{
    public bool ReturnsPreviousValue { get; init; }
}''')
lower='src/Copeland/Copeland.TS/Lowering/MirLowerer.cs'
edit(lower,'new MirAssignmentExpression(assignment.Name, value, assignment.Type)','assignment with { Expression = value }')
edit(lower,'new MirAssignmentExpression(a.Variable.Name, LowerExpression(a.Expression), ToMirType(a.Type))','new MirAssignmentExpression(a.Variable.Name, LowerExpression(a.Expression), ToMirType(a.Type)) { ReturnsPreviousValue = a.ReturnsPreviousValue }')
edit(lower,'        SyntaxKind.PlusToken => "+",','''        SyntaxKind.AmpersandToken => "&",
        SyntaxKind.PipeToken => "|",
        SyntaxKind.CaretToken => "^",
        SyntaxKind.ShiftLeftToken => "<<",
        SyntaxKind.ShiftRightToken => ">>",
        SyntaxKind.PlusToken => "+",''')
c='src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs'
j='src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs'
edit(c,'    private static readonly AsyncLocal<bool> UsesNativeStrings = new();','    private static readonly AsyncLocal<bool> UsesNativeStrings = new();\n    private static readonly AsyncLocal<bool> UsesIntegerRuntime = new();')
edit(c,'        UsesNativeStrings.Value = false;','        UsesNativeStrings.Value = false;\n        bool previousIntegerRuntime = UsesIntegerRuntime.Value;\n        UsesIntegerRuntime.Value = false;')
edit(c,'        if (UsesNativeStrings.Value) NativeStringRuntime.Emit(writer);','        if (UsesNativeStrings.Value) NativeStringRuntime.Emit(writer);\n        if (UsesIntegerRuntime.Value) IntegerRuntime.Emit(writer);')
edit(c,'        UsesNativeStrings.Value = previousNativeStrings;','        UsesNativeStrings.Value = previousNativeStrings;\n        UsesIntegerRuntime.Value = previousIntegerRuntime;')
edit(c,'MirAssignmentExpression assignment => $"{CSharpNameMangler.Mangle(assignment.Name)} = {EmitExpression(writer, assignment.Expression, function, enumNames, ref tempIndex, diagnostics)}",','MirAssignmentExpression assignment => AssignmentText(assignment, CSharpNameMangler.Mangle(assignment.Name), EmitExpression(writer, assignment.Expression, function, enumNames, ref tempIndex, diagnostics)),')
edit(c,'MirAssignmentExpression assignment => $"frame.{CSharpNameMangler.Mangle(assignment.Name)} = {EmitAsyncExpression(assignment.Expression, function)}",','MirAssignmentExpression assignment => AssignmentText(assignment, "frame." + CSharpNameMangler.Mangle(assignment.Name), EmitAsyncExpression(assignment.Expression, function)),')
edit(c,'MirBinaryExpression binary => $"({EmitAsyncExpression(binary.Left, function)} {binary.Operator} {EmitAsyncExpression(binary.Right, function)})",','MirBinaryExpression binary => BinaryText(binary, EmitAsyncExpression(binary.Left, function), EmitAsyncExpression(binary.Right, function)),')
edit(c,'            return $"({simpleLeft} {binaryOperator} {simpleRight})";','            return BinaryText(binary, simpleLeft, simpleRight);')
edit(c,'        return $"({leftTemporary} {binaryOperator} {rightTemporary})";','        return BinaryText(binary, leftTemporary, rightTemporary);')
insert='''    private static string AssignmentText(MirAssignmentExpression assignment, string target, string value)
    {
        if (!assignment.ReturnsPreviousValue) return target + " = " + value;
        UsesIntegerRuntime.Value = true;
        return $"__cope_AssignPrevious(ref {target}, {value})";
    }

    private static string BinaryText(MirBinaryExpression binary, string left, string right)
    {
        string expression = $"({left} {binary.Operator} {right})";
        if (binary.Type.Identifier != "int") return expression;
        if (binary.Operator is "/" or "%")
        {
            UsesIntegerRuntime.Value = true;
            string operation = binary.Operator == "/" ? "Divide" : "Remainder";
            return $"__cope_int_{operation}({left}, {right})";
        }
        return binary.Operator is "+" or "-" or "*" ? $"unchecked({expression})" : expression;
    }

'''
edit(c,'    private static string? NativeCallName(',insert+'    private static string? NativeCallName(')
# Explicit unary normalization also survives checked host compilation.
edit(c,'MirUnaryExpression unary => unary.Operator + ParenthesizeAssignmentOperand(unary.Operand, EmitExpression(writer, unary.Operand, function, enumNames, ref tempIndex, diagnostics)),','MirUnaryExpression unary => UnaryText(unary, ParenthesizeAssignmentOperand(unary.Operand, EmitExpression(writer, unary.Operand, function, enumNames, ref tempIndex, diagnostics))),')
edit(c,'MirUnaryExpression unary => $"({unary.Operator}{EmitAsyncExpression(unary.Operand, function)})",','MirUnaryExpression unary => UnaryText(unary, EmitAsyncExpression(unary.Operand, function)),')
edit(c,'    private static string AssignmentText(','''    private static string UnaryText(MirUnaryExpression unary, string operand)
    {
        string value = $"({unary.Operator}{operand})";
        return unary.Type.Identifier == "int" && unary.Operator == "-" ? $"unchecked({value})" : value;
    }

    private static string AssignmentText(''')
edit(j,'    private static readonly AsyncLocal<bool> UsesNativeStrings = new();','    private static readonly AsyncLocal<bool> UsesNativeStrings = new();\n    private static readonly AsyncLocal<bool> UsesIntegerRuntime = new();')
edit(j,'        UsesNativeStrings.Value = false;','        UsesNativeStrings.Value = false;\n        bool previousIntegerRuntime = UsesIntegerRuntime.Value;\n        UsesIntegerRuntime.Value = false;')
edit(j,'        if (UsesNativeStrings.Value) NativeStringRuntime.Emit(writer);','        if (UsesNativeStrings.Value) NativeStringRuntime.Emit(writer);\n        if (UsesIntegerRuntime.Value) IntegerRuntime.Emit(writer);')
edit(j,'            UsesNativeStrings.Value = previousNativeStrings;','            UsesNativeStrings.Value = previousNativeStrings;\n            UsesIntegerRuntime.Value = previousIntegerRuntime;')
edit(j,'bool isSupportedArithmetic = (binary.Operator is "+" or "-" or "*" or "/" or "%")','bool isSupportedArithmetic = (binary.Operator is "+" or "-" or "*" or "/" or "%" or "&" or "|" or "^" or "<<" or ">>")')
edit(j,'            values => $"({values[0]} {MapBinaryOperator(binary.Operator)} {values[1]})");','            values => BinaryText(binary, values[0], values[1]));')
edit(j,'MirBinaryExpression binary => $"({EmitAsyncExpression(binary.Left, catalog, results, names)} {binary.Operator} {EmitAsyncExpression(binary.Right, catalog, results, names)})",','MirBinaryExpression binary => BinaryText(binary, EmitAsyncExpression(binary.Left, catalog, results, names), EmitAsyncExpression(binary.Right, catalog, results, names)),')
edit(j,'        return new EmittedExpression(value.Prelude, $"({JavaScriptIdentifierEncoder.Encode(assignment.Name)} = {value.Value})");','''        string target = JavaScriptIdentifierEncoder.Encode(assignment.Name);
        if (!assignment.ReturnsPreviousValue) return new EmittedExpression(value.Prelude, $"({target} = {value.Value})");
        string previous = names.NextTemporary("previous_value");
        var prelude = new List<EmittedLine> { new($"const {previous} = {target};", 0) };
        prelude.AddRange(value.Prelude);
        prelude.Add(new EmittedLine($"{target} = {value.Value};", 0));
        return new EmittedExpression(prelude, previous);''')
# Async assignment uses a scope-local IIFE to preserve the previous value.
edit(j,'MirAssignmentExpression assignment => $"({JavaScriptIdentifierEncoder.Encode(assignment.Name)} = {EmitAsyncExpression(assignment.Expression, catalog, results, names)})",','MirAssignmentExpression assignment => AsyncAssignmentText(assignment, EmitAsyncExpression(assignment.Expression, catalog, results, names)),')
insert='''    private static string AsyncAssignmentText(MirAssignmentExpression assignment, string value)
    {
        string target = JavaScriptIdentifierEncoder.Encode(assignment.Name);
        if (!assignment.ReturnsPreviousValue) return $"({target} = {value})";
        return $"(() => {{ const previous = {target}; {target} = {value}; return previous; }})()";
    }

    private static string BinaryText(MirBinaryExpression binary, string left, string right)
    {
        string expression = $"({left} {MapBinaryOperator(binary.Operator)} {right})";
        if (binary.Type.Identifier != "int") return expression;
        if (binary.Operator == "*") return $"Math.imul({left}, {right})";
        if (binary.Operator is "/" or "%")
        {
            UsesIntegerRuntime.Value = true;
            string operation = binary.Operator == "/" ? "Divide" : "Remainder";
            return $"__cope_int_{operation}({left}, {right})";
        }
        return binary.Operator is "+" or "-" ? $"({expression} | 0)" : expression;
    }

'''
edit(j,'    private static string? NativeCallName(',insert+'    private static string? NativeCallName(')
