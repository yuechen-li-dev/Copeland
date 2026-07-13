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
        EnumCatalog catalog = ValidateProgram(program, diagnostics);
        if (diagnostics.Count > 0)
        {
            return new JavaScriptCompilation(null, diagnostics);
        }

        ResultCatalog results = ResultCatalog.Create(program);
        GeneratedNames names = GeneratedNames.Create(program, catalog, results);
        var writer = new JavaScriptTextWriter();
        writer.WriteLine("\"use strict\";");

        if (catalog.Enums.Count > 0 || results.Types.Count > 0)
        {
            writer.WriteLine();
            EmitValueRuntime(writer, catalog, results, names);
        }

        foreach (MirFunction function in program.Functions)
        {
            writer.WriteLine();
            EmitFunction(writer, function, catalog, results, names);
        }

        return new JavaScriptCompilation(writer.ToString(), []);
    }

    private static EnumCatalog ValidateProgram(MirProgram program, List<JavaScriptDiagnostic> diagnostics)
    {
        var catalog = new EnumCatalog(program.Enums, diagnostics);
        catalog.ValidateDefinitions(diagnostics);

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
            ValidateValueType(function.ReturnType, context, catalog, diagnostics, allowVoid: true);
            foreach (MirParameter parameter in function.Parameters)
            {
                ValidateValueType(parameter.Type, $"parameter '{parameter.Name}' in {context}", catalog, diagnostics, allowVoid: false);
            }

            foreach (MirLocal local in function.Locals)
            {
                if (!local.IsReadOnly)
                {
                    AddUnsupported(diagnostics, $"mutable local '{local.Name}' in {context}");
                }

                ValidateValueType(local.Type, $"local '{local.Name}' in {context}", catalog, diagnostics, allowVoid: false);
            }

            foreach (MirStatement statement in function.Body)
            {
                ValidateStatement(statement, function.ReturnType, context, functions, catalog, diagnostics);
            }
        }

        return catalog;
    }

    private static void ValidateStatement(
        MirStatement statement,
        MirType functionReturnType,
        string context,
        IReadOnlyDictionary<string, MirFunction> functions,
        EnumCatalog catalog,
        List<JavaScriptDiagnostic> diagnostics)
    {
        switch (statement)
        {
            case MirVariableDeclarationStatement declaration:
                if (!declaration.Local.IsReadOnly)
                {
                    AddUnsupported(diagnostics, $"mutable declaration '{declaration.Local.Name}' in {context}");
                }

                ValidateExpression(declaration.Initializer, functionReturnType, context, functions, catalog, diagnostics);
                RequireMatchingType(declaration.Initializer.Type, declaration.Local.Type, $"initializer for local '{declaration.Local.Name}' in {context}", diagnostics);
                break;
            case MirReturnStatement returnStatement when returnStatement.Expression is not null:
                ValidateExpression(returnStatement.Expression, functionReturnType, context, functions, catalog, diagnostics);
                RequireMatchingType(returnStatement.Expression.Type, functionReturnType, $"return expression in {context}", diagnostics);
                break;
            case MirReturnStatement when functionReturnType is MirResultType { SuccessType: MirType { Identifier: "void" } }:
                break;
            case MirReturnStatement:
                RequireType(functionReturnType, "void", $"empty return in {context}", diagnostics);
                break;
            case MirExpressionStatement expressionStatement:
                ValidateExpression(expressionStatement.Expression, functionReturnType, context, functions, catalog, diagnostics);
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
        MirType functionReturnType,
        string context,
        IReadOnlyDictionary<string, MirFunction> functions,
        EnumCatalog catalog,
        List<JavaScriptDiagnostic> diagnostics)
    {
        switch (expression)
        {
            case MirLiteralExpression literal:
                ValidateLiteral(literal, context, diagnostics);
                break;
            case MirVariableExpression variable:
                ValidateValueType(variable.Type, $"variable '{variable.Name}' in {context}", catalog, diagnostics, allowVoid: false);
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

                ValidateExpression(binary.Left, functionReturnType, context, functions, catalog, diagnostics);
                ValidateExpression(binary.Right, functionReturnType, context, functions, catalog, diagnostics);
                break;
            case MirCallExpression call:
                ValidateCall(call, functionReturnType, context, functions, catalog, diagnostics);
                break;
            case MirIfExpression conditional:
                RequireType(conditional.Condition.Type, "boolean", $"if-expression condition in {context}", diagnostics);
                ValidateValueType(conditional.Type, $"if expression in {context}", catalog, diagnostics, allowVoid: false);
                ValidateExpression(conditional.Condition, functionReturnType, context, functions, catalog, diagnostics);
                ValidateExpression(conditional.ThenExpression, functionReturnType, context, functions, catalog, diagnostics);
                ValidateExpression(conditional.ElseExpression, functionReturnType, context, functions, catalog, diagnostics);
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
                ValidateEnumValue(enumValue, functionReturnType, context, functions, catalog, diagnostics);
                break;
            case MirMatchExpression match:
                ValidateMatch(match, functionReturnType, context, functions, catalog, diagnostics);
                break;
            case MirOkExpression ok:
                ValidateValueType(ok.Type, $"Result success construction in {context}", catalog, diagnostics, allowVoid: false);
                ValidateExpression(ok.Payload, functionReturnType, context, functions, catalog, diagnostics);
                break;
            case MirErrExpression err:
                ValidateValueType(err.Type, $"Result error construction in {context}", catalog, diagnostics, allowVoid: false);
                ValidateExpression(err.Payload, functionReturnType, context, functions, catalog, diagnostics);
                break;
            case MirResultMatchExpression resultMatch:
                ValidateResultMatch(resultMatch, functionReturnType, context, functions, catalog, diagnostics);
                break;
            case MirPropagateExpression propagate:
                ValidatePropagation(propagate, functionReturnType, context, functions, catalog, diagnostics);
                break;
            case MirUnitExpression:
                break;
            default:
                AddUnsupported(diagnostics, $"unknown MIR expression '{expression.GetType().Name}' in {context}");
                break;
        }
    }

    private static void ValidateEnumValue(MirEnumValueExpression value, MirType functionReturnType, string context, IReadOnlyDictionary<string, MirFunction> functions, EnumCatalog catalog, List<JavaScriptDiagnostic> diagnostics)
    {
        if (!catalog.TryGetEnum(value.EnumName, out EnumInfo enumInfo))
        {
            AddInvalid(diagnostics, $"unknown enum '{value.EnumName}' for enum value in {context}");
        }
        else
        {
            RequireType(value.Type, enumInfo.Definition.Name, $"enum value '{value.EnumName}.{value.CaseName}' in {context}", diagnostics);
            if (!enumInfo.TryGetCase(value.CaseName, out MirEnumCase enumCase))
            {
                AddInvalid(diagnostics, $"unknown case '{value.EnumName}.{value.CaseName}' in {context}");
            }
            else
            {
                ValidatePayloadArguments(value.Arguments, enumCase.PayloadFields, $"enum value '{value.EnumName}.{value.CaseName}' in {context}", diagnostics);
            }
        }

        foreach (MirExpression argument in value.Arguments)
        {
            ValidateExpression(argument, functionReturnType, context, functions, catalog, diagnostics);
        }
    }

    private static void ValidateMatch(MirMatchExpression match, MirType functionReturnType, string context, IReadOnlyDictionary<string, MirFunction> functions, EnumCatalog catalog, List<JavaScriptDiagnostic> diagnostics)
    {
        ValidateExpression(match.Scrutinee, functionReturnType, context, functions, catalog, diagnostics);
        ValidateValueType(match.Type, $"match result in {context}", catalog, diagnostics, allowVoid: false);

        if (match.Scrutinee.Type is not MirType scrutineeType || match.Scrutinee.Type is MirArrayType or MirResultType || !catalog.TryGetEnum(scrutineeType.Identifier, out EnumInfo enumInfo))
        {
            AddUnsupported(diagnostics, $"match expression with non-enum scrutinee type '{match.Scrutinee.Type.Name}' in {context}");
            return;
        }

        var seenCases = new HashSet<string>(StringComparer.Ordinal);
        foreach (MirMatchArm arm in match.Arms)
        {
            if (!seenCases.Add(arm.CaseName))
            {
                AddInvalid(diagnostics, $"duplicate match arm '{arm.CaseName}' in {context}");
            }

            if (!enumInfo.TryGetCase(arm.CaseName, out MirEnumCase enumCase))
            {
                AddInvalid(diagnostics, $"unknown match case '{arm.CaseName}' for enum '{enumInfo.Definition.Name}' in {context}");
            }
            else
            {
                ValidatePayloadBindings(arm.PayloadBindings, enumCase.PayloadFields, $"match arm '{arm.CaseName}' in {context}", diagnostics);
            }

            ValidateExpression(arm.Expression, functionReturnType, context, functions, catalog, diagnostics);
            RequireMatchingType(arm.Expression.Type, match.Type, $"result of match arm '{arm.CaseName}' in {context}", diagnostics);
        }

        foreach (MirEnumCase enumCase in enumInfo.Definition.Cases)
        {
            if (!seenCases.Contains(enumCase.Name))
            {
                AddInvalid(diagnostics, $"non-exhaustive match for enum '{enumInfo.Definition.Name}' in {context}; missing case '{enumCase.Name}'");
            }
        }
    }

    private static void ValidatePayloadArguments(IReadOnlyList<MirExpression> arguments, IReadOnlyList<MirEnumPayloadField> fields, string context, List<JavaScriptDiagnostic> diagnostics)
    {
        if (arguments.Count != fields.Count)
        {
            AddInvalid(diagnostics, $"{context} has {arguments.Count} payloads but case declares {fields.Count}");
        }

        int sharedCount = Math.Min(arguments.Count, fields.Count);
        for (int index = 0; index < sharedCount; index += 1)
        {
            RequireMatchingType(arguments[index].Type, fields[index].Type, $"payload {index + 1} of {context}", diagnostics);
        }
    }

    private static void ValidatePayloadBindings(IReadOnlyList<MirMatchPayloadBinding> bindings, IReadOnlyList<MirEnumPayloadField> fields, string context, List<JavaScriptDiagnostic> diagnostics)
    {
        if (bindings.Count != fields.Count)
        {
            AddInvalid(diagnostics, $"{context} has {bindings.Count} bindings but case declares {fields.Count} payloads");
        }

        int sharedCount = Math.Min(bindings.Count, fields.Count);
        for (int index = 0; index < sharedCount; index += 1)
        {
            RequireMatchingType(bindings[index].Type, fields[index].Type, $"binding {index + 1} of {context}", diagnostics);
        }
    }

    private static void ValidateLiteral(MirLiteralExpression literal, string context, List<JavaScriptDiagnostic> diagnostics)
    {
        if (literal.Type is MirType { Identifier: "boolean" } && literal.Value is bool)
        {
            return;
        }

        if (literal.Type is MirType { Identifier: "number" } && literal.Value is int or long or float or double)
        {
            return;
        }

        if (literal.Type is MirType { Identifier: "string" } && literal.Value is string)
        {
            return;
        }

        AddUnsupported(diagnostics, $"literal of type '{literal.Type.Name}' in {context}");
    }

    private static void ValidateCall(MirCallExpression call, MirType functionReturnType, string context, IReadOnlyDictionary<string, MirFunction> functions, EnumCatalog catalog, List<JavaScriptDiagnostic> diagnostics)
    {
        if (!functions.TryGetValue(call.FunctionName, out MirFunction? target))
        {
            AddInvalid(diagnostics, $"unknown call target '{call.FunctionName}' in {context}");
        }
        else
        {
            if (target.Parameters.Count != call.Arguments.Count)
            {
                AddInvalid(diagnostics, $"call '{call.FunctionName}' has {call.Arguments.Count} arguments but target expects {target.Parameters.Count} in {context}");
            }

            RequireMatchingType(call.Type, target.ReturnType, $"call '{call.FunctionName}' in {context}", diagnostics);
            int sharedArgumentCount = Math.Min(target.Parameters.Count, call.Arguments.Count);
            for (int index = 0; index < sharedArgumentCount; index += 1)
            {
                RequireMatchingType(call.Arguments[index].Type, target.Parameters[index].Type, $"argument {index + 1} of call '{call.FunctionName}' in {context}", diagnostics);
            }
        }

        ValidateValueType(call.Type, $"call '{call.FunctionName}' in {context}", catalog, diagnostics, allowVoid: true);
        foreach (MirExpression argument in call.Arguments)
        {
            ValidateExpression(argument, functionReturnType, context, functions, catalog, diagnostics);
        }
    }

    private static void ValidateValueType(MirType type, string context, EnumCatalog catalog, List<JavaScriptDiagnostic> diagnostics, bool allowVoid)
    {
        switch (type)
        {
            case MirResultType result:
                ValidateValueType(result.SuccessType, $"Result success component of '{type.Name}' in {context}", catalog, diagnostics, allowVoid: true);
                ValidateValueType(result.ErrorType, $"Result error component of '{type.Name}' in {context}", catalog, diagnostics, allowVoid: false);
                return;
            case MirArrayType:
                AddUnsupported(diagnostics, $"array type '{type.Name}' in {context}");
                return;
            case MirType named when named is not MirArrayType and not MirResultType && named.Identifier is "number" or "boolean" or "string":
                return;
            case MirType { Identifier: "void" } when allowVoid:
                return;
            case MirType named when named is not MirArrayType and not MirResultType && catalog.ContainsEnum(named.Identifier):
                return;
            default:
                AddUnsupported(diagnostics, $"type '{type.Name}' in {context}");
                return;
        }
    }

    private static void RequireType(MirType type, string expected, string context, List<JavaScriptDiagnostic> diagnostics)
    {
        if (type is MirArrayType or MirResultType || !string.Equals(type.Identifier, expected, StringComparison.Ordinal))
        {
            AddInvalid(diagnostics, $"expected {expected} type for {context}, found '{type.Name}'");
        }
    }

    private static void RequireMatchingType(MirType actual, MirType expected, string context, List<JavaScriptDiagnostic> diagnostics)
    {
        if (!MirTypeFacts.AreEquivalent(actual, expected))
        {
            AddInvalid(diagnostics, $"expected type '{expected.Name}' for {context}, found '{actual.Name}'");
        }
    }

    private static void ValidatePrimitiveEquality(MirBinaryExpression binary, string context, List<JavaScriptDiagnostic> diagnostics)
    {
        RequireType(binary.Type, "boolean", $"equality expression in {context}", diagnostics);
        RequireMatchingType(binary.Left.Type, binary.Right.Type, $"operands of equality expression in {context}", diagnostics);

        if (binary.Left.Type is MirType { Identifier: "boolean" or "number" or "string" })
        {
            return;
        }

        AddUnsupported(diagnostics, $"equality for type '{binary.Left.Type.Name}' in {context}");
    }

    private static void ValidateResultMatch(MirResultMatchExpression match, MirType functionReturnType, string context, IReadOnlyDictionary<string, MirFunction> functions, EnumCatalog catalog, List<JavaScriptDiagnostic> diagnostics)
    {
        ValidateExpression(match.Scrutinee, functionReturnType, context, functions, catalog, diagnostics);
        ValidateValueType(match.Type, $"Result match result in {context}", catalog, diagnostics, allowVoid: false);
        if (match.Scrutinee.Type is not MirResultType resultType)
        {
            AddInvalid(diagnostics, $"Result match has non-Result scrutinee '{match.Scrutinee.Type.Name}' in {context}");
            return;
        }

        RequireMatchingType(match.OkBinding.Type, resultType.SuccessType, $"ok binding in Result match in {context}", diagnostics);
        RequireMatchingType(match.ErrBinding.Type, resultType.ErrorType, $"err binding in Result match in {context}", diagnostics);
        RequireMatchingType(match.OkExpression.Type, match.Type, $"ok arm in Result match in {context}", diagnostics);
        RequireMatchingType(match.ErrExpression.Type, match.Type, $"err arm in Result match in {context}", diagnostics);
        ValidateExpression(match.OkExpression, functionReturnType, context, functions, catalog, diagnostics);
        ValidateExpression(match.ErrExpression, functionReturnType, context, functions, catalog, diagnostics);
    }

    private static void ValidatePropagation(MirPropagateExpression propagation, MirType functionReturnType, string context, IReadOnlyDictionary<string, MirFunction> functions, EnumCatalog catalog, List<JavaScriptDiagnostic> diagnostics)
    {
        ValidateExpression(propagation.Operand, functionReturnType, context, functions, catalog, diagnostics);
        if (propagation.Target != MirPropagationTarget.FunctionReturn)
        {
            AddUnsupported(diagnostics, $"propagation target '{propagation.Target}' in {context}");
            return;
        }

        if (functionReturnType is not MirResultType functionResult || propagation.Operand.Type is not MirResultType operandResult)
        {
            AddInvalid(diagnostics, $"function-return propagation requires Result operand and Result return type in {context}");
            return;
        }

        RequireMatchingType(propagation.Type, operandResult.SuccessType, $"propagation success value in {context}", diagnostics);
        RequireMatchingType(functionResult.ErrorType, operandResult.ErrorType, $"propagation error type in {context}", diagnostics);
    }

    private static void AddUnsupported(List<JavaScriptDiagnostic> diagnostics, string feature)
    {
        diagnostics.Add(new JavaScriptDiagnostic(UnsupportedDiagnosticId, $"Unsupported MIR for JavaScript backend: {feature}."));
    }

    private static void AddInvalid(List<JavaScriptDiagnostic> diagnostics, string message)
    {
        diagnostics.Add(new JavaScriptDiagnostic(InvalidDiagnosticId, $"Invalid MIR for JavaScript backend: {message}."));
    }

    private static void EmitValueRuntime(JavaScriptTextWriter writer, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        writer.WriteLine($"function {names.Panic}() {{");
        writer.Indent();
        writer.WriteLine("throw new Error(\"Copeland JavaScript backend invariant failure.\");");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine($"function {names.MakeValue}(type, tag, payload) {{");
        writer.Indent();
        writer.WriteLine("return Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));");
        writer.Unindent();
        writer.WriteLine("}");

        foreach (EnumInfo enumInfo in catalog.Enums)
        {
            writer.WriteLine();
            writer.WriteLine($"const {names.TypeToken(enumInfo)} = Object.freeze(Object.create(null));");
        }

        foreach (EnumInfo enumInfo in catalog.Enums)
        {
            writer.WriteLine();
            EmitValidator(writer, enumInfo, catalog, results, names);
        }

        foreach (ResultInfo result in results.Types)
        {
            writer.WriteLine();
            writer.WriteLine($"const {names.TypeToken(result)} = Object.freeze(Object.create(null));");
        }

        foreach (ResultInfo result in results.Types)
        {
            writer.WriteLine();
            EmitResultValidator(writer, result, catalog, results, names);
        }
    }

    private static void EmitValidator(JavaScriptTextWriter writer, EnumInfo enumInfo, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        writer.WriteLine($"function {names.Validator(enumInfo)}(value) {{");
        writer.Indent();
        writer.WriteLine($"if (typeof value !== \"object\" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, \"$type\") || !Object.prototype.hasOwnProperty.call(value, \"$tag\") || !Object.prototype.hasOwnProperty.call(value, \"$payload\") || value.$type !== {names.TypeToken(enumInfo)} || typeof value.$tag !== \"string\" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {{");
        writer.Indent();
        writer.WriteLine($"{names.Panic}();");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("switch (value.$tag) {");
        writer.Indent();

        foreach (MirEnumCase enumCase in enumInfo.Definition.Cases)
        {
            writer.WriteLine($"case {JavaScriptLiteralWriter.WriteString(enumCase.Name)}:");
            writer.Indent();
            EmitPayloadValidation(writer, enumCase, catalog, results, names);
            writer.WriteLine("return;");
            writer.Unindent();
        }

        writer.WriteLine("default:");
        writer.Indent();
        writer.WriteLine($"{names.Panic}();");
        writer.Unindent();
        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitResultValidator(JavaScriptTextWriter writer, ResultInfo result, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        writer.WriteLine($"function {names.Validator(result)}(value) {{");
        writer.Indent();
        writer.WriteLine($"if (typeof value !== \"object\" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, \"$type\") || !Object.prototype.hasOwnProperty.call(value, \"$tag\") || !Object.prototype.hasOwnProperty.call(value, \"$payload\") || value.$type !== {names.TypeToken(result)} || typeof value.$tag !== \"string\" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {{");
        writer.Indent();
        writer.WriteLine($"{names.Panic}();");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("switch (value.$tag) {");
        writer.Indent();
        writer.WriteLine("case \"ok\":");
        writer.Indent();
        writer.WriteLine($"if (!({PayloadTypeCondition("value.$payload[0]", result.Type.SuccessType, catalog, results, names)})) {{ {names.Panic}(); }}");
        writer.WriteLine("return;");
        writer.Unindent();
        writer.WriteLine("case \"err\":");
        writer.Indent();
        writer.WriteLine($"if (!({PayloadTypeCondition("value.$payload[0]", result.Type.ErrorType, catalog, results, names)})) {{ {names.Panic}(); }}");
        writer.WriteLine("return;");
        writer.Unindent();
        writer.WriteLine("default:");
        writer.Indent();
        writer.WriteLine($"{names.Panic}();");
        writer.Unindent();
        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitPayloadValidation(JavaScriptTextWriter writer, MirEnumCase enumCase, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        writer.WriteLine($"if (value.$payload.length !== {enumCase.PayloadFields.Count}) {{");
        writer.Indent();
        writer.WriteLine($"{names.Panic}();");
        writer.Unindent();
        writer.WriteLine("}");

        for (int index = 0; index < enumCase.PayloadFields.Count; index += 1)
        {
            MirType type = enumCase.PayloadFields[index].Type;
            writer.WriteLine($"if (!Object.prototype.hasOwnProperty.call(value.$payload, {index}) || !({PayloadTypeCondition($"value.$payload[{index}]", type, catalog, results, names)})) {{");
            writer.Indent();
            writer.WriteLine($"{names.Panic}();");
            writer.Unindent();
            writer.WriteLine("}");
        }
    }

    private static string PayloadTypeCondition(string expression, MirType type, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        return type switch
        {
            MirType { Identifier: "boolean" } => $"typeof {expression} === \"boolean\"",
            MirType { Identifier: "number" } => $"typeof {expression} === \"number\"",
            MirType { Identifier: "string" } => $"typeof {expression} === \"string\"",
            MirType { Identifier: "void" } => $"{expression} === null",
            MirResultType result => $"({names.Validator(results.Get(result))}({expression}), true)",
            MirType named when named is not MirArrayType and not MirResultType && catalog.TryGetEnum(named.Identifier, out EnumInfo enumInfo) => $"({names.Validator(enumInfo)}({expression}), true)",
            _ => "false",
        };
    }

    private static void EmitFunction(JavaScriptTextWriter writer, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        string parameters = string.Join(", ", function.Parameters.Select(parameter => JavaScriptIdentifierEncoder.Encode(parameter.Name)));
        writer.WriteLine($"function {JavaScriptIdentifierEncoder.Encode(function.Name)}({parameters}) {{");
        writer.Indent();
        foreach (MirStatement statement in function.Body)
        {
            EmitStatement(writer, statement, function, catalog, results, names);
        }

        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitStatement(JavaScriptTextWriter writer, MirStatement statement, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        switch (statement)
        {
            case MirVariableDeclarationStatement declaration:
                EmittedExpression initializer = EmitExpression(declaration.Initializer, function, catalog, results, names);
                WritePrelude(writer, initializer.Prelude);
                writer.WriteLine($"const {JavaScriptIdentifierEncoder.Encode(declaration.Local.Name)} = {initializer.Value};");
                break;
            case MirReturnStatement { Expression: null } when function.ReturnType is MirResultType result && result.SuccessType is MirType { Identifier: "void" }:
                writer.WriteLine($"return {names.MakeValue}({names.TypeToken(results.Get(result))}, \"ok\", [null]);");
                break;
            case MirReturnStatement returnStatement when returnStatement.Expression is null:
                writer.WriteLine("return;");
                break;
            case MirReturnStatement returnStatement:
                EmittedExpression returned = EmitExpression(returnStatement.Expression!, function, catalog, results, names);
                WritePrelude(writer, returned.Prelude);
                writer.WriteLine($"return {returned.Value};");
                break;
            case MirExpressionStatement expressionStatement:
                EmittedExpression expression = EmitExpression(expressionStatement.Expression, function, catalog, results, names);
                WritePrelude(writer, expression.Prelude);
                writer.WriteLine($"{expression.Value};");
                break;
            default:
                throw new InvalidOperationException($"Validated JavaScript emission received unsupported statement {statement.GetType().Name}.");
        }
    }

    private static EmittedExpression EmitExpression(MirExpression expression, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        return expression switch
        {
            MirLiteralExpression { Value: bool boolean } => EmittedExpression.ValueOnly(boolean ? "true" : "false"),
            MirLiteralExpression { Value: string text } => EmittedExpression.ValueOnly(JavaScriptLiteralWriter.WriteString(text)),
            MirLiteralExpression { Value: not null } literal => EmittedExpression.ValueOnly(JavaScriptLiteralWriter.WriteNumber(literal.Value)),
            MirUnitExpression => EmittedExpression.ValueOnly("null"),
            MirVariableExpression variable => EmittedExpression.ValueOnly(JavaScriptIdentifierEncoder.Encode(variable.Name)),
            MirBinaryExpression binary => EmitBinary(binary, function, catalog, results, names),
            MirCallExpression call => EmitCall(call, function, catalog, results, names),
            MirEnumValueExpression value => EmitEnumValueExpression(value, function, catalog, results, names),
            MirMatchExpression match => EmitEnumMatchExpression(match, function, catalog, results, names),
            MirResultMatchExpression match => EmitResultMatchExpression(match, function, catalog, results, names),
            MirIfExpression conditional => EmitIfExpression(conditional, function, catalog, results, names),
            MirOkExpression ok => EmitResultConstruction(ok.Payload, (MirResultType)ok.Type, "ok", function, catalog, results, names),
            MirErrExpression err => EmitResultConstruction(err.Payload, (MirResultType)err.Type, "err", function, catalog, results, names),
            MirPropagateExpression propagation => EmitPropagation(propagation, function, catalog, results, names),
            _ => throw new InvalidOperationException($"Validated JavaScript emission received unsupported expression {expression.GetType().Name}.")
        };
    }

    private static EmittedExpression EmitBinary(MirBinaryExpression binary, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        EmittedExpression left = EmitExpression(binary.Left, function, catalog, results, names);
        EmittedExpression right = EmitExpression(binary.Right, function, catalog, results, names);
        return EmittedExpression.Combine($"({left.Value} {MapBinaryOperator(binary.Operator)} {right.Value})", left, right);
    }

    private static EmittedExpression EmitCall(MirCallExpression call, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        var emittedArguments = call.Arguments.Select(argument => EmitExpression(argument, function, catalog, results, names)).ToList();
        return EmittedExpression.Combine($"{JavaScriptIdentifierEncoder.Encode(call.FunctionName)}({string.Join(", ", emittedArguments.Select(argument => argument.Value))})", emittedArguments);
    }

    private static EmittedExpression EmitEnumValueExpression(MirEnumValueExpression value, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        EnumInfo enumInfo = catalog.GetEnum(value.EnumName);
        var payloads = value.Arguments.Select(argument => EmitExpression(argument, function, catalog, results, names)).ToList();
        return EmittedExpression.Combine($"{names.MakeValue}({names.TypeToken(enumInfo)}, {JavaScriptLiteralWriter.WriteString(value.CaseName)}, [{string.Join(", ", payloads.Select(payload => payload.Value))}])", payloads);
    }

    private static EmittedExpression EmitEnumMatchExpression(MirMatchExpression match, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        if (ContainsControlFlow(match))
        {
            return EmitStructuredEnumMatch(match, function, catalog, results, names);
        }

        EnumInfo enumInfo = catalog.GetEnum(match.Scrutinee.Type.Identifier);
        string scrutinee = names.NextMatchScrutinee();
        var parts = new List<string>
        {
            "(() => {",
            $"const {scrutinee} = {EmitExpression(match.Scrutinee, function, catalog, results, names).Value};",
            $"{names.Validator(enumInfo)}({scrutinee});",
            $"switch ({scrutinee}.$tag) {{",
        };

        foreach (MirMatchArm arm in match.Arms)
        {
            parts.Add($"case {JavaScriptLiteralWriter.WriteString(arm.CaseName)}: {{");
            for (int index = 0; index < arm.PayloadBindings.Count; index += 1)
            {
                string binding = JavaScriptIdentifierEncoder.Encode(arm.PayloadBindings[index].Name);
                parts.Add($"const {binding} = {scrutinee}.$payload[{index}];");
            }

            parts.Add($"return {EmitExpression(arm.Expression, function, catalog, results, names).Value};");
            parts.Add("}");
        }

        parts.Add("default:");
        parts.Add($"return {names.Panic}();");
        parts.Add("}");
        parts.Add("})()");
        return EmittedExpression.ValueOnly(string.Join(" ", parts));
    }

    private static EmittedExpression EmitResultConstruction(MirExpression payload, MirResultType type, string tag, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        EmittedExpression emittedPayload = EmitExpression(payload, function, catalog, results, names);
        return new EmittedExpression(emittedPayload.Prelude, $"{names.MakeValue}({names.TypeToken(results.Get(type))}, \"{tag}\", [{emittedPayload.Value}])");
    }

    private static EmittedExpression EmitPropagation(MirPropagateExpression propagation, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        if (propagation.Target != MirPropagationTarget.FunctionReturn || function.ReturnType is not MirResultType functionResult || propagation.Operand.Type is not MirResultType operandResult)
        {
            throw new InvalidOperationException("Validated JavaScript emission received an unsupported Result propagation target.");
        }

        EmittedExpression operand = EmitExpression(propagation.Operand, function, catalog, results, names);
        string temporary = names.NextTemporary("propagate");
        var prelude = new List<EmittedLine>(operand.Prelude)
        {
            new($"const {temporary} = {operand.Value};", 0),
            new($"{names.Validator(results.Get(operandResult))}({temporary});", 0),
            new($"if ({temporary}.$tag === \"err\") {{", 0),
            new($"return {names.MakeValue}({names.TypeToken(results.Get(functionResult))}, \"err\", [{temporary}.$payload[0]]);", 1),
            new("}", 0),
        };
        return new EmittedExpression(prelude, $"{temporary}.$payload[0]");
    }

    private static EmittedExpression EmitResultMatchExpression(MirResultMatchExpression match, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        MirResultType resultType = (MirResultType)match.Scrutinee.Type;
        EmittedExpression scrutinee = EmitExpression(match.Scrutinee, function, catalog, results, names);
        EmittedExpression ok = EmitExpression(match.OkExpression, function, catalog, results, names);
        EmittedExpression err = EmitExpression(match.ErrExpression, function, catalog, results, names);
        string scrutineeTemporary = names.NextTemporary("result_match");
        string valueTemporary = names.NextTemporary("result_value");
        string okBinding = JavaScriptIdentifierEncoder.Encode(match.OkBinding.Name);
        string errBinding = JavaScriptIdentifierEncoder.Encode(match.ErrBinding.Name);
        var prelude = new List<EmittedLine>(scrutinee.Prelude)
        {
            new($"const {scrutineeTemporary} = {scrutinee.Value};", 0),
            new($"{names.Validator(results.Get(resultType))}({scrutineeTemporary});", 0),
            new($"let {valueTemporary};", 0),
            new($"switch ({scrutineeTemporary}.$tag) {{", 0),
            new("case \"ok\": {", 1),
            new($"const {okBinding} = {scrutineeTemporary}.$payload[0];", 2),
        };
        prelude.AddRange(ok.Prelude.Select(line => line.OffsetBy(2)));
        prelude.Add(new EmittedLine($"{valueTemporary} = {ok.Value};", 2));
        prelude.Add(new EmittedLine("break;", 2));
        prelude.Add(new EmittedLine("}", 1));
        prelude.Add(new EmittedLine("case \"err\": {", 1));
        prelude.Add(new EmittedLine($"const {errBinding} = {scrutineeTemporary}.$payload[0];", 2));
        prelude.AddRange(err.Prelude.Select(line => line.OffsetBy(2)));
        prelude.Add(new EmittedLine($"{valueTemporary} = {err.Value};", 2));
        prelude.Add(new EmittedLine("break;", 2));
        prelude.Add(new EmittedLine("}", 1));
        prelude.Add(new EmittedLine("default:", 1));
        prelude.Add(new EmittedLine($"{names.Panic}();", 2));
        prelude.Add(new EmittedLine("}", 0));
        return new EmittedExpression(prelude, valueTemporary);
    }

    private static EmittedExpression EmitIfExpression(MirIfExpression conditional, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        EmittedExpression condition = EmitExpression(conditional.Condition, function, catalog, results, names);
        EmittedExpression thenExpression = EmitExpression(conditional.ThenExpression, function, catalog, results, names);
        EmittedExpression elseExpression = EmitExpression(conditional.ElseExpression, function, catalog, results, names);
        if (thenExpression.Prelude.Count == 0 && elseExpression.Prelude.Count == 0)
        {
            return new EmittedExpression(condition.Prelude, $"({condition.Value} ? {thenExpression.Value} : {elseExpression.Value})");
        }

        string valueTemporary = names.NextTemporary("if_value");
        var prelude = new List<EmittedLine>(condition.Prelude)
        {
            new($"let {valueTemporary};", 0),
            new($"if ({condition.Value}) {{", 0),
        };
        prelude.AddRange(thenExpression.Prelude.Select(line => line.OffsetBy(1)));
        prelude.Add(new EmittedLine($"{valueTemporary} = {thenExpression.Value};", 1));
        prelude.Add(new EmittedLine("} else {", 0));
        prelude.AddRange(elseExpression.Prelude.Select(line => line.OffsetBy(1)));
        prelude.Add(new EmittedLine($"{valueTemporary} = {elseExpression.Value};", 1));
        prelude.Add(new EmittedLine("}", 0));
        return new EmittedExpression(prelude, valueTemporary);
    }

    private static EmittedExpression EmitStructuredEnumMatch(MirMatchExpression match, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        EnumInfo enumInfo = catalog.GetEnum(match.Scrutinee.Type.Identifier);
        EmittedExpression scrutinee = EmitExpression(match.Scrutinee, function, catalog, results, names);
        string scrutineeTemporary = names.NextTemporary("match");
        string valueTemporary = names.NextTemporary("match_value");
        var prelude = new List<EmittedLine>(scrutinee.Prelude)
        {
            new($"const {scrutineeTemporary} = {scrutinee.Value};", 0),
            new($"{names.Validator(enumInfo)}({scrutineeTemporary});", 0),
            new($"let {valueTemporary};", 0),
            new($"switch ({scrutineeTemporary}.$tag) {{", 0),
        };
        foreach (MirMatchArm arm in match.Arms)
        {
            EmittedExpression armExpression = EmitExpression(arm.Expression, function, catalog, results, names);
            prelude.Add(new EmittedLine($"case {JavaScriptLiteralWriter.WriteString(arm.CaseName)}:", 1));
            prelude.Add(new EmittedLine("{", 1));
            for (int index = 0; index < arm.PayloadBindings.Count; index += 1)
            {
                prelude.Add(new EmittedLine($"const {JavaScriptIdentifierEncoder.Encode(arm.PayloadBindings[index].Name)} = {scrutineeTemporary}.$payload[{index}];", 2));
            }
            prelude.AddRange(armExpression.Prelude.Select(line => line.OffsetBy(2)));
            prelude.Add(new EmittedLine($"{valueTemporary} = {armExpression.Value};", 2));
            prelude.Add(new EmittedLine("break;", 2));
            prelude.Add(new EmittedLine("}", 1));
        }
        prelude.Add(new EmittedLine("default:", 1));
        prelude.Add(new EmittedLine($"{names.Panic}();", 2));
        prelude.Add(new EmittedLine("}", 0));
        return new EmittedExpression(prelude, valueTemporary);
    }

    private static bool ContainsControlFlow(MirExpression expression)
    {
        return expression switch
        {
            MirPropagateExpression or MirResultMatchExpression => true,
            MirBinaryExpression binary => ContainsControlFlow(binary.Left) || ContainsControlFlow(binary.Right),
            MirCallExpression call => call.Arguments.Any(ContainsControlFlow),
            MirEnumValueExpression value => value.Arguments.Any(ContainsControlFlow),
            MirMatchExpression match => ContainsControlFlow(match.Scrutinee) || match.Arms.Any(arm => ContainsControlFlow(arm.Expression)),
            MirIfExpression conditional => ContainsControlFlow(conditional.Condition) || ContainsControlFlow(conditional.ThenExpression) || ContainsControlFlow(conditional.ElseExpression),
            MirOkExpression ok => ContainsControlFlow(ok.Payload),
            MirErrExpression err => ContainsControlFlow(err.Payload),
            _ => false,
        };
    }

    private static void WritePrelude(JavaScriptTextWriter writer, IReadOnlyList<EmittedLine> prelude)
    {
        foreach (EmittedLine line in prelude)
        {
            for (int index = 0; index < line.Indent; index += 1)
            {
                writer.Indent();
            }
            writer.WriteLine(line.Text);
            for (int index = 0; index < line.Indent; index += 1)
            {
                writer.Unindent();
            }
        }
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

    private sealed record EmittedLine(string Text, int Indent)
    {
        public EmittedLine OffsetBy(int amount) => new(Text, Indent + amount);
    }

    private sealed class EmittedExpression(IReadOnlyList<EmittedLine> prelude, string value)
    {
        public IReadOnlyList<EmittedLine> Prelude { get; } = prelude;

        public string Value { get; } = value;

        public static EmittedExpression ValueOnly(string value) => new([], value);

        public static EmittedExpression Combine(string value, params EmittedExpression[] expressions) => Combine(value, (IEnumerable<EmittedExpression>)expressions);

        public static EmittedExpression Combine(string value, IEnumerable<EmittedExpression> expressions)
        {
            var prelude = new List<EmittedLine>();
            foreach (EmittedExpression expression in expressions)
            {
                prelude.AddRange(expression.Prelude);
            }

            return new EmittedExpression(prelude, value);
        }
    }

    private sealed class EnumCatalog
    {
        private readonly Dictionary<string, EnumInfo> byName = new(StringComparer.Ordinal);

        public EnumCatalog(IReadOnlyList<MirEnum> enums, List<JavaScriptDiagnostic> diagnostics)
        {
            var values = new List<EnumInfo>(enums.Count);
            for (int index = 0; index < enums.Count; index += 1)
            {
                var info = new EnumInfo(enums[index], index);
                values.Add(info);
                if (!byName.TryAdd(enums[index].Name, info))
                {
                    AddInvalid(diagnostics, $"duplicate enum '{enums[index].Name}'");
                }
            }

            Enums = values;
        }

        public IReadOnlyList<EnumInfo> Enums { get; }

        public bool ContainsEnum(string name) => byName.ContainsKey(name);

        public bool TryGetEnum(string name, out EnumInfo enumInfo) => byName.TryGetValue(name, out enumInfo!);

        public EnumInfo GetEnum(string name) => byName[name];

        public void ValidateDefinitions(List<JavaScriptDiagnostic> diagnostics)
        {
            foreach (EnumInfo enumInfo in Enums)
            {
                if (enumInfo.Definition.Cases.Count == 0)
                {
                    AddInvalid(diagnostics, $"enum '{enumInfo.Definition.Name}' has no cases");
                }

                foreach (MirEnumCase enumCase in enumInfo.Definition.Cases)
                {
                    if (!enumInfo.TryGetCase(enumCase.Name, out MirEnumCase resolvedCase) || !ReferenceEquals(resolvedCase, enumCase))
                    {
                        AddInvalid(diagnostics, $"duplicate case '{enumInfo.Definition.Name}.{enumCase.Name}'");
                    }

                    foreach (MirEnumPayloadField field in enumCase.PayloadFields)
                    {
                        ValidateValueType(field.Type, $"payload '{field.Name}' of enum case '{enumInfo.Definition.Name}.{enumCase.Name}'", this, diagnostics, allowVoid: false);
                    }
                }
            }

            foreach (EnumInfo enumInfo in Enums)
            {
                if (HasRecursivePayloadShape(enumInfo, new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal)))
                {
                    AddUnsupported(diagnostics, $"recursive payload enum '{enumInfo.Definition.Name}'");
                }
            }
        }

        private bool HasRecursivePayloadShape(EnumInfo current, HashSet<string> visiting, HashSet<string> visited)
        {
            if (!visiting.Add(current.Definition.Name))
            {
                return true;
            }

            if (!visited.Add(current.Definition.Name))
            {
                visiting.Remove(current.Definition.Name);
                return false;
            }

            foreach (MirType payloadType in current.Definition.Cases.SelectMany(enumCase => enumCase.PayloadFields).Select(field => field.Type))
            {
                if (TryGetEnum(payloadType.Name, out EnumInfo nested) && HasRecursivePayloadShape(nested, visiting, visited))
                {
                    return true;
                }
            }

            visiting.Remove(current.Definition.Name);
            return false;
        }
    }

    private sealed class EnumInfo
    {
        private readonly Dictionary<string, MirEnumCase> cases = new(StringComparer.Ordinal);

        public EnumInfo(MirEnum definition, int index)
        {
            Definition = definition;
            Index = index;
            foreach (MirEnumCase enumCase in definition.Cases)
            {
                cases.TryAdd(enumCase.Name, enumCase);
            }
        }

        public MirEnum Definition { get; }

        public int Index { get; }

        public bool TryGetCase(string name, out MirEnumCase enumCase) => cases.TryGetValue(name, out enumCase!);
    }

    private sealed class ResultInfo(MirResultType type, int index)
    {
        public MirResultType Type { get; } = type;

        public int Index { get; } = index;
    }

    private sealed class ResultCatalog
    {
        private readonly List<ResultInfo> types = [];

        public static ResultCatalog Empty { get; } = new();

        public IReadOnlyList<ResultInfo> Types => types;

        public ResultInfo Get(MirResultType type)
        {
            foreach (ResultInfo result in types)
            {
                if (MirTypeFacts.AreEquivalent(result.Type, type))
                {
                    return result;
                }
            }

            throw new InvalidOperationException($"No JavaScript Result token exists for '{type.Name}'.");
        }

        public static ResultCatalog Create(MirProgram program)
        {
            var catalog = new ResultCatalog();
            foreach (MirEnum @enum in program.Enums)
            {
                foreach (MirEnumCase @case in @enum.Cases)
                {
                    foreach (MirEnumPayloadField field in @case.PayloadFields)
                    {
                        catalog.Add(field.Type);
                    }
                }
            }

            foreach (MirFunction function in program.Functions)
            {
                catalog.Add(function.ReturnType);
                foreach (MirParameter parameter in function.Parameters)
                {
                    catalog.Add(parameter.Type);
                }

                foreach (MirLocal local in function.Locals)
                {
                    catalog.Add(local.Type);
                }

                foreach (MirStatement statement in function.Body)
                {
                    catalog.Add(statement);
                }
            }

            return catalog;
        }

        private void Add(MirStatement statement)
        {
            switch (statement)
            {
                case MirVariableDeclarationStatement declaration:
                    Add(declaration.Initializer);
                    break;
                case MirExpressionStatement expression:
                    Add(expression.Expression);
                    break;
                case MirReturnStatement { Expression: not null } returned:
                    Add(returned.Expression);
                    break;
                case MirIfStatement conditional:
                    Add(conditional.Condition);
                    foreach (MirStatement nested in conditional.ThenStatements)
                    {
                        Add(nested);
                    }
                    if (conditional.ElseStatements is not null)
                    {
                        foreach (MirStatement nested in conditional.ElseStatements)
                        {
                            Add(nested);
                        }
                    }
                    break;
            }
        }

        private void Add(MirExpression expression)
        {
            Add(expression.Type);
            switch (expression)
            {
                case MirBinaryExpression binary:
                    Add(binary.Left);
                    Add(binary.Right);
                    break;
                case MirCallExpression call:
                    foreach (MirExpression argument in call.Arguments)
                    {
                        Add(argument);
                    }
                    break;
                case MirEnumValueExpression value:
                    foreach (MirExpression argument in value.Arguments)
                    {
                        Add(argument);
                    }
                    break;
                case MirMatchExpression match:
                    Add(match.Scrutinee);
                    foreach (MirMatchArm arm in match.Arms)
                    {
                        Add(arm.Expression);
                    }
                    break;
                case MirResultMatchExpression match:
                    Add(match.Scrutinee);
                    Add(match.OkExpression);
                    Add(match.ErrExpression);
                    break;
                case MirIfExpression conditional:
                    Add(conditional.Condition);
                    Add(conditional.ThenExpression);
                    Add(conditional.ElseExpression);
                    break;
                case MirOkExpression ok:
                    Add(ok.Payload);
                    break;
                case MirErrExpression err:
                    Add(err.Payload);
                    break;
                case MirPropagateExpression propagation:
                    Add(propagation.Operand);
                    break;
            }
        }

        private void Add(MirType type)
        {
            switch (type)
            {
                case MirResultType result:
                    if (!types.Any(existing => MirTypeFacts.AreEquivalent(existing.Type, result)))
                    {
                        types.Add(new ResultInfo(result, types.Count));
                    }
                    Add(result.SuccessType);
                    Add(result.ErrorType);
                    break;
                case MirArrayType array:
                    Add(array.ElementType);
                    break;
            }
        }
    }

    private sealed class GeneratedNames
    {
        private readonly Dictionary<EnumInfo, string> typeTokens;
        private readonly Dictionary<EnumInfo, string> validators;
        private readonly Dictionary<ResultInfo, string> resultTypeTokens;
        private readonly Dictionary<ResultInfo, string> resultValidators;
        private readonly NameAllocator allocator;

        private GeneratedNames(NameAllocator allocator, string panic, string makeValue, Dictionary<EnumInfo, string> typeTokens, Dictionary<EnumInfo, string> validators, Dictionary<ResultInfo, string> resultTypeTokens, Dictionary<ResultInfo, string> resultValidators)
        {
            this.allocator = allocator;
            Panic = panic;
            MakeValue = makeValue;
            this.typeTokens = typeTokens;
            this.validators = validators;
            this.resultTypeTokens = resultTypeTokens;
            this.resultValidators = resultValidators;
        }

        public string Panic { get; }

        public string MakeValue { get; }

        public string TypeToken(EnumInfo enumInfo) => typeTokens[enumInfo];

        public string Validator(EnumInfo enumInfo) => validators[enumInfo];

        public string TypeToken(ResultInfo result) => resultTypeTokens[result];

        public string Validator(ResultInfo result) => resultValidators[result];

        public string NextMatchScrutinee() => allocator.Allocate("match");

        public string NextTemporary(string purpose) => allocator.Allocate(purpose);

        public static GeneratedNames Create(MirProgram program, EnumCatalog catalog, ResultCatalog results)
        {
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            foreach (MirFunction function in program.Functions)
            {
                occupied.Add(JavaScriptIdentifierEncoder.Encode(function.Name));
                foreach (MirParameter parameter in function.Parameters)
                {
                    occupied.Add(JavaScriptIdentifierEncoder.Encode(parameter.Name));
                }

                foreach (MirLocal local in function.Locals)
                {
                    occupied.Add(JavaScriptIdentifierEncoder.Encode(local.Name));
                }
            }

            var allocator = new NameAllocator(occupied);
            string panic = allocator.Allocate("panic");
            string makeValue = allocator.Allocate("make");
            var typeTokens = new Dictionary<EnumInfo, string>();
            var validators = new Dictionary<EnumInfo, string>();
            foreach (EnumInfo enumInfo in catalog.Enums)
            {
                typeTokens.Add(enumInfo, allocator.Allocate("type"));
                validators.Add(enumInfo, allocator.Allocate("validate"));
            }

            var resultTypeTokens = new Dictionary<ResultInfo, string>();
            var resultValidators = new Dictionary<ResultInfo, string>();
            foreach (ResultInfo result in results.Types)
            {
                resultTypeTokens.Add(result, allocator.Allocate("result_type"));
                resultValidators.Add(result, allocator.Allocate("result_validate"));
            }

            return new GeneratedNames(allocator, panic, makeValue, typeTokens, validators, resultTypeTokens, resultValidators);
        }
    }

    private sealed class NameAllocator(HashSet<string> occupied)
    {
        private int nextIndex;

        public string Allocate(string purpose)
        {
            while (true)
            {
                string candidate = $"__cope_m3_{purpose}_{nextIndex++}";
                if (occupied.Add(candidate))
                {
                    return candidate;
                }
            }
        }
    }
}
