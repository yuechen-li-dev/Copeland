from pathlib import Path
p=Path('src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs');s=p.read_text()
s=s.replace('private static string BinaryText(MirBinaryExpression binary, string left, string right)','private static string BinaryText(MirBinaryExpression binary, string left, string right, bool flow = false)')
s=s.replace('return $"__cope_int_{operation}({left}, {right})";', 'string owner = flow ? ModuleClassName + "." : string.Empty;\n            return $"{owner}__cope_int_{operation}({left}, {right})";')
s=s.replace('BinaryText(binary, simpleLeft, simpleRight)', 'BinaryText(binary, simpleLeft, simpleRight, function.Name == "<flow>")').replace('BinaryText(binary, leftTemporary, rightTemporary)','BinaryText(binary, leftTemporary, rightTemporary, function.Name == "<flow>")')
# Only remove conversions for the typed int array length/index paths, retaining numeric conversion checks.
for x in ['access.Index','construction.Length','assignment.Index']:
 s=s.replace('checked((int){EmitExpression(writer, '+x+', function, enumNames, ref tempIndex, diagnostics)})','{EmitExpression(writer, '+x+', function, enumNames, ref tempIndex, diagnostics)}')
old='''            writer.WriteLine(@case.PayloadFields.Count == 0
                ? $"public sealed record {caseName} : {enumName};"
                : $"public sealed record {caseName}({string.Join(", ", @case.PayloadFields.Select(field => $"{MapType(field.Type)} {CSharpNameMangler.Mangle(field.Name)}"))}) : {enumName};");'''
new='''            if (@case.PayloadFields.Count == 0)
            {
                writer.WriteLine($"public sealed record {caseName} : {enumName}");
                writer.WriteLine("{");
                writer.Indent();
                writer.WriteLine($"internal static {caseName} __Singleton {{ get; }} = new();");
                writer.Unindent();
                writer.WriteLine("}");
            }
            else
            {
                writer.WriteLine($"public sealed record {caseName}({string.Join(", ", @case.PayloadFields.Select(field => $"{MapType(field.Type)} {CSharpNameMangler.Mangle(field.Name)}"))}) : {enumName};");
            }'''
assert old in s;s=s.replace(old,new)
old='MirEnumValueExpression value => $"new {CSharpNameMangler.Mangle(value.EnumName)}.{CSharpNameMangler.Mangle(value.CaseName)}({string.Join(", ", EmitArguments(value.Arguments, writer, function, enumNames, ref tempIndex, diagnostics))})",'
new='MirEnumValueExpression value => value.Arguments.Count == 0\n                ? $"{CSharpNameMangler.Mangle(value.EnumName)}.{CSharpNameMangler.Mangle(value.CaseName)}.__Singleton"\n                : $"new {CSharpNameMangler.Mangle(value.EnumName)}.{CSharpNameMangler.Mangle(value.CaseName)}({string.Join(", ", EmitArguments(value.Arguments, writer, function, enumNames, ref tempIndex, diagnostics))})",'
assert old in s;s=s.replace(old,new)
s=s.replace('MirTableEnumConstant value => $"new', 'MirTableEnumConstant value when value.Payloads.Count == 0 => $"{CSharpNameMangler.Mangle(value.EnumName)}.{CSharpNameMangler.Mangle(value.CaseName)}.__Singleton",\n            MirTableEnumConstant value => $"new')
p.write_text(s)
for name in ['NativeStringRuntime','IntegerRuntime']:
 p=Path('src/Copeland/Copeland.TS.Backend.CSharp/CSharp/'+name+'.cs');s=p.read_text().replace('private static','internal static');p.write_text(s)
p=Path('src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs');s=p.read_text()
s=s.replace('MirUnaryExpression unary => "(" + unary.Operator + EmitFlowExpression(unary.Operand, fieldsById) + ")",','MirUnaryExpression unary => UnaryText(unary, EmitFlowExpression(unary.Operand, fieldsById)),')
s=s.replace('MirBinaryExpression binary => "(" + EmitFlowExpression(binary.Left, fieldsById) + " " + binary.Operator + " " + EmitFlowExpression(binary.Right, fieldsById) + ")",','MirBinaryExpression binary => BinaryText(binary, EmitFlowExpression(binary.Left, fieldsById), EmitFlowExpression(binary.Right, fieldsById)),')
s=s.replace('MirCallExpression call => JavaScriptIdentifierEncoder.Encode(call.FunctionName)\n', 'MirCallExpression call => (NativeCallName(call) ?? JavaScriptIdentifierEncoder.Encode(call.FunctionName))\n')
s=s.replace('MirUnaryExpression unary => $"({unary.Operator}{EmitAsyncExpression(unary.Operand, catalog, results, names)})",','MirUnaryExpression unary => UnaryText(unary, EmitAsyncExpression(unary.Operand, catalog, results, names)),')
pos=s.index('    private static string BinaryText(')
s=s[:pos]+'''    private static string UnaryText(MirUnaryExpression unary, string operand)
    {
        string expression = $"({unary.Operator}{operand})";
        return unary.Type.Identifier == "int" && unary.Operator == "-"
            ? $"({expression} | 0)"
            : expression;
    }

'''+s[pos:]
p.write_text(s)
# Build canonical LF hash directly through proof tool next.
p=Path('tests/Copeland/Copeland.TS.Tests/TableFeatureTests.cs');s=p.read_text().replace('Assert.Equal(1661, bytes.Length);','Assert.Equal(1606, bytes.Length);\n        Assert.DoesNotContain("\\r", first.MirText, StringComparison.Ordinal);');p.write_text(s)
