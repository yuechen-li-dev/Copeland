using Copeland.TS.Mir;

namespace Copeland.TS.Backend.JavaScript;

public static class JavaScriptBackend
{
    private const string UnsupportedDiagnosticId = "COPE-JS-0001";
    private const string InvalidDiagnosticId = "COPE-JS-0002";

    public static JavaScriptCompilation Emit(MirProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var diagnostics = new List<JavaScriptDiagnostic>();
        ValidateProgram(program, diagnostics);
        if (diagnostics.Count > 0)
        {
            return new JavaScriptCompilation(null, diagnostics);
        }

        var writer = new JavaScriptTextWriter();
        writer.WriteLine("\"use strict\";");

        foreach (MirFunction function in program.Functions)
        {
            writer.WriteLine();
            EmitFunction(writer, function);
        }

        return new JavaScriptCompilation(writer.ToString(), []);
    }

    private static void ValidateProgram(MirProgram program, List<JavaScriptDiagnostic> diagnostics)
    {
        foreach (MirEnum mirEnum in program.Enums)
        {
            AddUnsupported(diagnostics, $"enum '{mirEnum.Name}'");
        }

        var functions = new Dictionary<string, MirFunction>(StringComparer.Ordinal);
        foreach (MirFunction function in program.Functions)
        {
            if (!functions.TryAdd(function.Name, function))
            {
                AddInvalid(diagnostics, $"duplicate function '{function.Name}'");
            }
        }

        foreach (MirFunction function in program.Functions)
        {
            string context = $"function '{function.Name}'";
            if (function.IsFallible)
            {
                AddUnsupported(diagnostics, $"fallible {context}");
            }

            ValidateValueType(function.ReturnType, context, diagnostics, allowVoid: true);
            foreach (MirParameter parameter in function.Parameters)
            {
                ValidateValueType(parameter.Type, $"parameter '{parameter.Name}' in {context}", diagnostics, allowVoid: false);
            }

            foreach (MirLocal local in function.Locals)
            {
                if (!local.IsReadOnly)
                {
                    AddUnsupported(diagnostics, $"mutable local '{local.Name}' in {context}");
                }

                ValidateValueType(local.Type, $"local '{local.Name}' in {context}", diagnostics, allowVoid: false);
            }

            foreach (MirStatement statement in function.Body)
            {
                ValidateStatement(statement, function.ReturnType, context, functions, diagnostics);
            }
        }
    }

    private static void ValidateStatement(
        MirStatement statement,
        MirType functionReturnType,
        string context,
        IReadOnlyDictionary<string, MirFunction> functions,
        List<JavaScriptDiagnostic> diagnostics)
    {
        switch (statement)
        {
            case MirVariableDeclarationStatement declaration:
                if (!declaration.Local.IsReadOnly)
                {
                    AddUnsupported(diagnostics, $"mutable declaration '{declaration.Local.Name}' in {context}");
                }

                ValidateExpression(declaration.Initializer, context, functions, diagnostics);
                RequireMatchingType(
                    declaration.Initializer.Type,
                    declaration.Local.Type,
                    $"initializer for local '{declaration.Local.Name}' in {context}",
                    diagnostics);
                break;
            case MirReturnStatement returnStatement:
                if (returnStatement.Expression is not null)
                {
                    ValidateExpression(returnStatement.Expression, context, functions, diagnostics);
                    RequireMatchingType(returnStatement.Expression.Type, functionReturnType, $"return expression in {context}", diagnostics);
                }

                break;
            case MirExpressionStatement expressionStatement:
                ValidateExpression(expressionStatement.Expression, context, functions, diagnostics);
                break;
            case MirIfStatement:
                AddUnsupported(diagnostics, $"if statement in {context}; CTS-M1 supports if expressions only");
                break;
            case MirWhileStatement:
                AddUnsupported(diagnostics, $"while loop in {context}");
                break;
            case MirForStatement:
                AddUnsupported(diagnostics, $"for loop in {context}");
                break;
            default:
                AddUnsupported(diagnostics, $"unknown MIR statement '{statement.GetType().Name}' in {context}");
                break;
        }
    }

    private static void ValidateExpression(
        MirExpression expression,
        string context,
        IReadOnlyDictionary<string, MirFunction> functions,
        List<JavaScriptDiagnostic> diagnostics)
    {
        switch (expression)
        {
            case MirLiteralExpression literal:
                ValidateLiteral(literal, context, diagnostics);
                break;
            case MirVariableExpression variable:
                ValidateValueType(variable.Type, $"variable '{variable.Name}' in {context}", diagnostics, allowVoid: false);
                break;
            case MirBinaryExpression binary:
                bool isSupportedArithmetic = binary.Operator is "+" or "-" or "*" or "/" or "%";
                bool isEquality = binary.Operator is "==" or "!=";
                if (!isSupportedArithmetic && !isEquality)
                {
                    AddUnsupported(diagnostics, $"binary operator '{binary.Operator}' in {context}");
                }

                if (isSupportedArithmetic)
                {
                    RequireType(binary.Type, "number", $"binary expression in {context}", diagnostics);
                    RequireType(binary.Left.Type, "number", $"left operand of binary expression in {context}", diagnostics);
                    RequireType(binary.Right.Type, "number", $"right operand of binary expression in {context}", diagnostics);
                }

                if (isEquality)
                {
                    ValidatePrimitiveEquality(binary, context, diagnostics);
                }

                ValidateExpression(binary.Left, context, functions, diagnostics);
                ValidateExpression(binary.Right, context, functions, diagnostics);
                break;
            case MirCallExpression call:
                ValidateCall(call, context, functions, diagnostics);
                break;
            case MirIfExpression conditional:
                RequireType(conditional.Condition.Type, "boolean", $"if-expression condition in {context}", diagnostics);
                ValidateValueType(conditional.Type, $"if expression in {context}", diagnostics, allowVoid: false);
                ValidateExpression(conditional.Condition, context, functions, diagnostics);
                ValidateExpression(conditional.ThenExpression, context, functions, diagnostics);
                ValidateExpression(conditional.ElseExpression, context, functions, diagnostics);
                RequireMatchingType(conditional.ThenExpression.Type, conditional.Type, $"then branch of if expression in {context}", diagnostics);
                RequireMatchingType(conditional.ElseExpression.Type, conditional.Type, $"else branch of if expression in {context}", diagnostics);
                break;
            case MirAssignmentExpression assignment:
                AddUnsupported(diagnostics, $"assignment to '{assignment.Name}' in {context}");
                break;
            case MirUnaryExpression unary:
                AddUnsupported(diagnostics, $"unary operator '{unary.Operator}' in {context}");
                break;
            case MirArrayExpression:
                AddUnsupported(diagnostics, $"array expression in {context}");
                break;
            case MirEnumValueExpression enumValue:
                AddUnsupported(diagnostics, $"enum value '{enumValue.EnumName}.{enumValue.CaseName}' in {context}");
                break;
            case MirMatchExpression:
                AddUnsupported(diagnostics, $"match expression in {context}");
                break;
            default:
                AddUnsupported(diagnostics, $"unknown MIR expression '{expression.GetType().Name}' in {context}");
                break;
        }
    }

    private static void ValidateLiteral(MirLiteralExpression literal, string context, List<JavaScriptDiagnostic> diagnostics)
    {
        if (literal.Type.Name == "boolean" && literal.Value is bool)
        {
            return;
        }

        if (literal.Type.Name == "number" && literal.Value is int or long or float or double)
        {
            return;
        }

        if (literal.Type.Name == "string" && literal.Value is string)
        {
            return;
        }

        AddUnsupported(diagnostics, $"literal of type '{literal.Type.Name}' in {context}");
    }

    private static void ValidateCall(
        MirCallExpression call,
        string context,
        IReadOnlyDictionary<string, MirFunction> functions,
        List<JavaScriptDiagnostic> diagnostics)
    {
        if (call.IsFallible)
        {
            AddUnsupported(diagnostics, $"fallible call '{call.FunctionName}' in {context}");
        }

        if (call.IsPropagated)
        {
            AddUnsupported(diagnostics, $"propagated call '{call.FunctionName}' in {context}");
        }

        if (!functions.TryGetValue(call.FunctionName, out MirFunction? target))
        {
            AddInvalid(diagnostics, $"unknown call target '{call.FunctionName}' in {context}");
        }
        else
        {
            if (target.IsFallible)
            {
                AddUnsupported(diagnostics, $"call to fallible function '{call.FunctionName}' in {context}");
            }

            if (target.Parameters.Count != call.Arguments.Count)
            {
                AddInvalid(diagnostics, $"call '{call.FunctionName}' has {call.Arguments.Count} arguments but target expects {target.Parameters.Count} in {context}");
            }

            RequireMatchingType(call.Type, target.ReturnType, $"call '{call.FunctionName}' in {context}", diagnostics);
            int sharedArgumentCount = Math.Min(target.Parameters.Count, call.Arguments.Count);
            for (int index = 0; index < sharedArgumentCount; index += 1)
            {
                RequireMatchingType(
                    call.Arguments[index].Type,
                    target.Parameters[index].Type,
                    $"argument {index + 1} of call '{call.FunctionName}' in {context}",
                    diagnostics);
            }
        }

        ValidateValueType(call.Type, $"call '{call.FunctionName}' in {context}", diagnostics, allowVoid: true);
        foreach (MirExpression argument in call.Arguments)
        {
            ValidateExpression(argument, context, functions, diagnostics);
        }
    }

    private static void ValidateValueType(MirType type, string context, List<JavaScriptDiagnostic> diagnostics, bool allowVoid)
    {
        if (type.Name is "number" or "boolean" or "string" || (allowVoid && type.Name == "void"))
        {
            return;
        }

        AddUnsupported(diagnostics, $"type '{type.Name}' in {context}");
    }

    private static void RequireType(MirType type, string expected, string context, List<JavaScriptDiagnostic> diagnostics)
    {
        if (!string.Equals(type.Name, expected, StringComparison.Ordinal))
        {
            AddInvalid(diagnostics, $"expected {expected} type for {context}, found '{type.Name}'");
        }
    }

    private static void RequireMatchingType(MirType actual, MirType expected, string context, List<JavaScriptDiagnostic> diagnostics)
    {
        if (!string.Equals(actual.Name, expected.Name, StringComparison.Ordinal))
        {
            AddInvalid(diagnostics, $"expected type '{expected.Name}' for {context}, found '{actual.Name}'");
        }
    }

    private static void ValidatePrimitiveEquality(
        MirBinaryExpression binary,
        string context,
        List<JavaScriptDiagnostic> diagnostics)
    {
        RequireType(binary.Type, "boolean", $"equality expression in {context}", diagnostics);
        RequireMatchingType(binary.Left.Type, binary.Right.Type, $"operands of equality expression in {context}", diagnostics);

        if (binary.Left.Type.Name is "boolean" or "number" or "string")
        {
            return;
        }

        AddUnsupported(diagnostics, $"equality for type '{binary.Left.Type.Name}' in {context}");
    }

    private static void AddUnsupported(List<JavaScriptDiagnostic> diagnostics, string feature)
    {
        diagnostics.Add(new JavaScriptDiagnostic(UnsupportedDiagnosticId, $"Unsupported MIR for JavaScript backend: {feature}."));
    }

    private static void AddInvalid(List<JavaScriptDiagnostic> diagnostics, string message)
    {
        diagnostics.Add(new JavaScriptDiagnostic(InvalidDiagnosticId, $"Invalid MIR for JavaScript backend: {message}."));
    }

    private static void EmitFunction(JavaScriptTextWriter writer, MirFunction function)
    {
        string parameters = string.Join(", ", function.Parameters.Select(parameter => JavaScriptIdentifierEncoder.Encode(parameter.Name)));
        writer.WriteLine($"function {JavaScriptIdentifierEncoder.Encode(function.Name)}({parameters}) {{");
        writer.Indent();
        foreach (MirStatement statement in function.Body)
        {
            EmitStatement(writer, statement);
        }

        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitStatement(JavaScriptTextWriter writer, MirStatement statement)
    {
        switch (statement)
        {
            case MirVariableDeclarationStatement declaration:
                writer.WriteLine($"const {JavaScriptIdentifierEncoder.Encode(declaration.Local.Name)} = {EmitExpression(declaration.Initializer)};");
                break;
            case MirReturnStatement returnStatement when returnStatement.Expression is null:
                writer.WriteLine("return;");
                break;
            case MirReturnStatement returnStatement:
                writer.WriteLine($"return {EmitExpression(returnStatement.Expression!)};");
                break;
            case MirExpressionStatement expressionStatement:
                writer.WriteLine($"{EmitExpression(expressionStatement.Expression)};");
                break;
            default:
                throw new InvalidOperationException($"Validated JavaScript emission received unsupported statement {statement.GetType().Name}.");
        }
    }

    private static string EmitExpression(MirExpression expression)
    {
        return expression switch
        {
            MirLiteralExpression { Value: bool boolean } => boolean ? "true" : "false",
            MirLiteralExpression { Value: string text } => JavaScriptLiteralWriter.WriteString(text),
            MirLiteralExpression { Value: not null } literal => JavaScriptLiteralWriter.WriteNumber(literal.Value),
            MirVariableExpression variable => JavaScriptIdentifierEncoder.Encode(variable.Name),
            MirBinaryExpression binary => $"({EmitExpression(binary.Left)} {MapBinaryOperator(binary.Operator)} {EmitExpression(binary.Right)})",
            MirCallExpression call => $"{JavaScriptIdentifierEncoder.Encode(call.FunctionName)}({string.Join(", ", call.Arguments.Select(EmitExpression))})",
            MirIfExpression conditional => $"({EmitExpression(conditional.Condition)} ? {EmitExpression(conditional.ThenExpression)} : {EmitExpression(conditional.ElseExpression)})",
            _ => throw new InvalidOperationException($"Validated JavaScript emission received unsupported expression {expression.GetType().Name}.")
        };
    }

    private static string MapBinaryOperator(string @operator)
    {
        return @operator switch
        {
            "==" => "===",
            "!=" => "!==",
            _ => @operator,
        };
    }
}
