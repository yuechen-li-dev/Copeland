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

        GeneratedNames names = GeneratedNames.Create(program, catalog);
        var writer = new JavaScriptTextWriter();
        writer.WriteLine("\"use strict\";");

        if (catalog.Enums.Count > 0)
        {
            writer.WriteLine();
            EmitEnumRuntime(writer, catalog, names);
        }

        foreach (MirFunction function in program.Functions)
        {
            writer.WriteLine();
            EmitFunction(writer, function, catalog, names);
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
            if (function.IsFallible)
            {
                AddUnsupported(diagnostics, $"fallible {context}");
            }

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

                ValidateExpression(declaration.Initializer, context, functions, catalog, diagnostics);
                RequireMatchingType(declaration.Initializer.Type, declaration.Local.Type, $"initializer for local '{declaration.Local.Name}' in {context}", diagnostics);
                break;
            case MirReturnStatement returnStatement when returnStatement.Expression is not null:
                ValidateExpression(returnStatement.Expression, context, functions, catalog, diagnostics);
                RequireMatchingType(returnStatement.Expression.Type, functionReturnType, $"return expression in {context}", diagnostics);
                break;
            case MirReturnStatement:
                break;
            case MirExpressionStatement expressionStatement:
                ValidateExpression(expressionStatement.Expression, context, functions, catalog, diagnostics);
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

                ValidateExpression(binary.Left, context, functions, catalog, diagnostics);
                ValidateExpression(binary.Right, context, functions, catalog, diagnostics);
                break;
            case MirCallExpression call:
                ValidateCall(call, context, functions, catalog, diagnostics);
                break;
            case MirIfExpression conditional:
                RequireType(conditional.Condition.Type, "boolean", $"if-expression condition in {context}", diagnostics);
                ValidateValueType(conditional.Type, $"if expression in {context}", catalog, diagnostics, allowVoid: false);
                ValidateExpression(conditional.Condition, context, functions, catalog, diagnostics);
                ValidateExpression(conditional.ThenExpression, context, functions, catalog, diagnostics);
                ValidateExpression(conditional.ElseExpression, context, functions, catalog, diagnostics);
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
                ValidateEnumValue(enumValue, context, functions, catalog, diagnostics);
                break;
            case MirMatchExpression match:
                ValidateMatch(match, context, functions, catalog, diagnostics);
                break;
            default:
                AddUnsupported(diagnostics, $"unknown MIR expression '{expression.GetType().Name}' in {context}");
                break;
        }
    }

    private static void ValidateEnumValue(MirEnumValueExpression value, string context, IReadOnlyDictionary<string, MirFunction> functions, EnumCatalog catalog, List<JavaScriptDiagnostic> diagnostics)
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
            ValidateExpression(argument, context, functions, catalog, diagnostics);
        }
    }

    private static void ValidateMatch(MirMatchExpression match, string context, IReadOnlyDictionary<string, MirFunction> functions, EnumCatalog catalog, List<JavaScriptDiagnostic> diagnostics)
    {
        ValidateExpression(match.Scrutinee, context, functions, catalog, diagnostics);
        ValidateValueType(match.Type, $"match result in {context}", catalog, diagnostics, allowVoid: false);

        if (!catalog.TryGetEnum(match.Scrutinee.Type.Name, out EnumInfo enumInfo))
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

            ValidateExpression(arm.Expression, context, functions, catalog, diagnostics);
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

    private static void ValidateCall(MirCallExpression call, string context, IReadOnlyDictionary<string, MirFunction> functions, EnumCatalog catalog, List<JavaScriptDiagnostic> diagnostics)
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
                RequireMatchingType(call.Arguments[index].Type, target.Parameters[index].Type, $"argument {index + 1} of call '{call.FunctionName}' in {context}", diagnostics);
            }
        }

        ValidateValueType(call.Type, $"call '{call.FunctionName}' in {context}", catalog, diagnostics, allowVoid: true);
        foreach (MirExpression argument in call.Arguments)
        {
            ValidateExpression(argument, context, functions, catalog, diagnostics);
        }
    }

    private static void ValidateValueType(MirType type, string context, EnumCatalog catalog, List<JavaScriptDiagnostic> diagnostics, bool allowVoid)
    {
        if (type.Name is "number" or "boolean" or "string" || (allowVoid && type.Name == "void") || catalog.ContainsEnum(type.Name))
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

    private static void ValidatePrimitiveEquality(MirBinaryExpression binary, string context, List<JavaScriptDiagnostic> diagnostics)
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

    private static void EmitEnumRuntime(JavaScriptTextWriter writer, EnumCatalog catalog, GeneratedNames names)
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
            EmitValidator(writer, enumInfo, catalog, names);
        }
    }

    private static void EmitValidator(JavaScriptTextWriter writer, EnumInfo enumInfo, EnumCatalog catalog, GeneratedNames names)
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
            EmitPayloadValidation(writer, enumCase, catalog, names);
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

    private static void EmitPayloadValidation(JavaScriptTextWriter writer, MirEnumCase enumCase, EnumCatalog catalog, GeneratedNames names)
    {
        writer.WriteLine($"if (value.$payload.length !== {enumCase.PayloadFields.Count}) {{");
        writer.Indent();
        writer.WriteLine($"{names.Panic}();");
        writer.Unindent();
        writer.WriteLine("}");

        for (int index = 0; index < enumCase.PayloadFields.Count; index += 1)
        {
            MirType type = enumCase.PayloadFields[index].Type;
            writer.WriteLine($"if (!Object.prototype.hasOwnProperty.call(value.$payload, {index}) || !({PayloadTypeCondition($"value.$payload[{index}]", type, catalog, names)})) {{");
            writer.Indent();
            writer.WriteLine($"{names.Panic}();");
            writer.Unindent();
            writer.WriteLine("}");
        }
    }

    private static string PayloadTypeCondition(string expression, MirType type, EnumCatalog catalog, GeneratedNames names)
    {
        return type.Name switch
        {
            "boolean" => $"typeof {expression} === \"boolean\"",
            "number" => $"typeof {expression} === \"number\"",
            "string" => $"typeof {expression} === \"string\"",
            _ when catalog.TryGetEnum(type.Name, out EnumInfo enumInfo) => $"({names.Validator(enumInfo)}({expression}), true)",
            _ => "false",
        };
    }

    private static void EmitFunction(JavaScriptTextWriter writer, MirFunction function, EnumCatalog catalog, GeneratedNames names)
    {
        string parameters = string.Join(", ", function.Parameters.Select(parameter => JavaScriptIdentifierEncoder.Encode(parameter.Name)));
        writer.WriteLine($"function {JavaScriptIdentifierEncoder.Encode(function.Name)}({parameters}) {{");
        writer.Indent();
        foreach (MirStatement statement in function.Body)
        {
            EmitStatement(writer, statement, catalog, names);
        }

        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitStatement(JavaScriptTextWriter writer, MirStatement statement, EnumCatalog catalog, GeneratedNames names)
    {
        switch (statement)
        {
            case MirVariableDeclarationStatement declaration:
                writer.WriteLine($"const {JavaScriptIdentifierEncoder.Encode(declaration.Local.Name)} = {EmitExpression(declaration.Initializer, catalog, names)};");
                break;
            case MirReturnStatement returnStatement when returnStatement.Expression is null:
                writer.WriteLine("return;");
                break;
            case MirReturnStatement returnStatement:
                writer.WriteLine($"return {EmitExpression(returnStatement.Expression!, catalog, names)};");
                break;
            case MirExpressionStatement expressionStatement:
                writer.WriteLine($"{EmitExpression(expressionStatement.Expression, catalog, names)};");
                break;
            default:
                throw new InvalidOperationException($"Validated JavaScript emission received unsupported statement {statement.GetType().Name}.");
        }
    }

    private static string EmitExpression(MirExpression expression, EnumCatalog catalog, GeneratedNames names)
    {
        return expression switch
        {
            MirLiteralExpression { Value: bool boolean } => boolean ? "true" : "false",
            MirLiteralExpression { Value: string text } => JavaScriptLiteralWriter.WriteString(text),
            MirLiteralExpression { Value: not null } literal => JavaScriptLiteralWriter.WriteNumber(literal.Value),
            MirVariableExpression variable => JavaScriptIdentifierEncoder.Encode(variable.Name),
            MirBinaryExpression binary => $"({EmitExpression(binary.Left, catalog, names)} {MapBinaryOperator(binary.Operator)} {EmitExpression(binary.Right, catalog, names)})",
            MirCallExpression call => $"{JavaScriptIdentifierEncoder.Encode(call.FunctionName)}({string.Join(", ", call.Arguments.Select(argument => EmitExpression(argument, catalog, names)))})",
            MirEnumValueExpression value => EmitEnumValueExpression(value, catalog, names),
            MirMatchExpression match => EmitMatchExpression(match, catalog, names),
            MirIfExpression conditional => $"({EmitExpression(conditional.Condition, catalog, names)} ? {EmitExpression(conditional.ThenExpression, catalog, names)} : {EmitExpression(conditional.ElseExpression, catalog, names)})",
            _ => throw new InvalidOperationException($"Validated JavaScript emission received unsupported expression {expression.GetType().Name}.")
        };
    }

    private static string EmitEnumValueExpression(MirEnumValueExpression value, EnumCatalog catalog, GeneratedNames names)
    {
        EnumInfo enumInfo = catalog.GetEnum(value.EnumName);
        string payloads = string.Join(", ", value.Arguments.Select(argument => EmitExpression(argument, catalog, names)));
        return $"{names.MakeValue}({names.TypeToken(enumInfo)}, {JavaScriptLiteralWriter.WriteString(value.CaseName)}, [{payloads}])";
    }

    private static string EmitMatchExpression(MirMatchExpression match, EnumCatalog catalog, GeneratedNames names)
    {
        EnumInfo enumInfo = catalog.GetEnum(match.Scrutinee.Type.Name);
        string scrutinee = names.NextMatchScrutinee();
        var parts = new List<string>
        {
            "(() => {",
            $"const {scrutinee} = {EmitExpression(match.Scrutinee, catalog, names)};",
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

            parts.Add($"return {EmitExpression(arm.Expression, catalog, names)};");
            parts.Add("}");
        }

        parts.Add("default:");
        parts.Add($"return {names.Panic}();");
        parts.Add("}");
        parts.Add("})()");
        return string.Join(" ", parts);
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

    private sealed class GeneratedNames
    {
        private readonly Dictionary<EnumInfo, string> typeTokens;
        private readonly Dictionary<EnumInfo, string> validators;
        private readonly NameAllocator allocator;

        private GeneratedNames(NameAllocator allocator, string panic, string makeValue, Dictionary<EnumInfo, string> typeTokens, Dictionary<EnumInfo, string> validators)
        {
            this.allocator = allocator;
            Panic = panic;
            MakeValue = makeValue;
            this.typeTokens = typeTokens;
            this.validators = validators;
        }

        public string Panic { get; }

        public string MakeValue { get; }

        public string TypeToken(EnumInfo enumInfo) => typeTokens[enumInfo];

        public string Validator(EnumInfo enumInfo) => validators[enumInfo];

        public string NextMatchScrutinee() => allocator.Allocate("match");

        public static GeneratedNames Create(MirProgram program, EnumCatalog catalog)
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

            return new GeneratedNames(allocator, panic, makeValue, typeTokens, validators);
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
