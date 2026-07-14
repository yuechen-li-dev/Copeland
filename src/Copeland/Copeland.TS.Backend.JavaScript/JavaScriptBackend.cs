using Copeland.TS.Mir;

namespace Copeland.TS.Backend.JavaScript;

public static class JavaScriptBackend
{
    private const string UnsupportedDiagnosticId = "COPE-JS-0001";
    private const string InvalidDiagnosticId = "COPE-JS-0002";

    public static JavaScriptCompilation Emit(MirProgram program)
    {
        return Emit(program, options: null);
    }

    public static JavaScriptCompilation Emit(MirProgram program, JavaScriptEmissionOptions? options)
    {
        ArgumentNullException.ThrowIfNull(program);
        JavaScriptEmissionOptions effectiveOptions = options ?? new JavaScriptEmissionOptions();
        if (!Enum.IsDefined(effectiveOptions.Profile))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Unsupported JavaScript emission profile.");
        }

        var diagnostics = MirValidator.Validate(program)
            .Select(diagnostic => new JavaScriptDiagnostic(InvalidDiagnosticId, $"Invalid MIR: {diagnostic.Message}"))
            .ToList();
        if (diagnostics.Count > 0)
        {
            return new JavaScriptCompilation(null, diagnostics);
        }
        EnumCatalog catalog = ValidateProgram(program, diagnostics);
        if (diagnostics.Count > 0)
        {
            return new JavaScriptCompilation(null, diagnostics);
        }

        ResultCatalog results = ResultCatalog.Create(program);
        bool usesUnwrap = ProgramUsesUnwrap(program);
        bool usesTryExcept = ProgramUsesTryExcept(program);
        GeneratedNames names = GeneratedNames.Create(program, catalog, results, usesUnwrap, usesTryExcept, effectiveOptions.Profile);
        var writer = new JavaScriptTextWriter(names.Document, effectiveOptions.Profile);
        writer.WriteLine("\"use strict\";");

        if (catalog.Enums.Count > 0 || catalog.Records.Count > 0 || program.Tables.Count > 0 || results.Types.Count > 0 || usesTryExcept)
        {
            writer.WriteLine();
            EmitValueRuntime(writer, program.TsonEncodingPlans, catalog, results, names, usesUnwrap, usesTryExcept);
        }

        foreach (MirFunction function in program.Functions)
        {
            writer.WriteLine();
            EmitFunction(writer, function, catalog, results, names);
        }

        string sourceText = writer.ToString();
        if (effectiveOptions.Profile == JavaScriptEmissionProfile.Symbolic)
        {
            SymbolicJavaScriptVocabulary.ValidateIdentifierFile(sourceText);
        }

        return new JavaScriptCompilation(sourceText, []);
    }

    private static EnumCatalog ValidateProgram(MirProgram program, List<JavaScriptDiagnostic> diagnostics)
    {
        var catalog = new EnumCatalog(program.Enums, program.Records, program.Tables, diagnostics);
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
                bool isLogical = binary.Operator is "&&" or "||";
                if (!isSupportedArithmetic && !isEquality && !isLogical)
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

                if (isLogical)
                {
                    RequireType(binary.Type, "boolean", $"logical expression in {context}", diagnostics);
                    RequireType(binary.Left.Type, "boolean", $"left operand of logical expression in {context}", diagnostics);
                    RequireType(binary.Right.Type, "boolean", $"right operand of logical expression in {context}", diagnostics);
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
                ValidateExpression(assignment.Expression, functionReturnType, context, functions, catalog, diagnostics);
                RequireMatchingType(assignment.Expression.Type, assignment.Type, $"assignment to '{assignment.Name}' in {context}", diagnostics);
                break;
            case MirUnaryExpression unary when unary.Operator is "-" or "!":
                ValidateExpression(unary.Operand, functionReturnType, context, functions, catalog, diagnostics);
                break;
            case MirUnaryExpression unary:
                AddUnsupported(diagnostics, $"unary operator '{unary.Operator}' in {context}");
                break;
            case MirArrayExpression array:
                if (array.Type is not MirArrayType arrayType)
                {
                    AddUnsupported(diagnostics, $"array expression without an array type in {context}");
                    break;
                }

                foreach (MirExpression element in array.Elements)
                {
                    ValidateExpression(element, functionReturnType, context, functions, catalog, diagnostics);
                    RequireMatchingType(element.Type, arrayType.ElementType, $"array element in {context}", diagnostics);
                }
                break;
            case MirRecordConstructionExpression construction:
                ValidateRecordConstruction(construction, functionReturnType, context, functions, catalog, diagnostics);
                break;
            case MirRecordFieldAccessExpression access:
                ValidateExpression(access.Receiver, functionReturnType, context, functions, catalog, diagnostics);
                ValidateValueType(access.Type, $"record field access in {context}", catalog, diagnostics, allowVoid: false);
                break;
            case MirTsonEncodeExpression encode:
                ValidateExpression(encode.Operand, functionReturnType, context, functions, catalog, diagnostics);
                break;
            case MirTableReferenceExpression reference:
                ValidateValueType(reference.Type, $"table reference in {context}", catalog, diagnostics, allowVoid: false);
                break;
            case MirTableColumnAccessExpression access:
                ValidateExpression(access.Receiver, functionReturnType, context, functions, catalog, diagnostics);
                ValidateValueType(access.Type, $"table column access in {context}", catalog, diagnostics, allowVoid: false);
                break;
            case MirTableRowAccessExpression access:
                ValidateExpression(access.Receiver, functionReturnType, context, functions, catalog, diagnostics);
                ValidateExpression(access.Index, functionReturnType, context, functions, catalog, diagnostics);
                ValidateValueType(access.Type, $"table row access in {context}", catalog, diagnostics, allowVoid: false);
                break;
            case MirColumnElementAccessExpression access:
                ValidateExpression(access.Receiver, functionReturnType, context, functions, catalog, diagnostics);
                ValidateExpression(access.Index, functionReturnType, context, functions, catalog, diagnostics);
                ValidateValueType(access.Type, $"column element access in {context}", catalog, diagnostics, allowVoid: false);
                break;
            case MirTableRowFieldAccessExpression access:
                ValidateExpression(access.Receiver, functionReturnType, context, functions, catalog, diagnostics);
                ValidateValueType(access.Type, $"table row field access in {context}", catalog, diagnostics, allowVoid: false);
                break;
            case MirRecordWithExpression withExpression:
                ValidateExpression(withExpression.Source, functionReturnType, context, functions, catalog, diagnostics);
                foreach (MirRecordFieldValue replacement in withExpression.Replacements)
                {
                    ValidateExpression(replacement.Value, functionReturnType, context, functions, catalog, diagnostics);
                }
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
            case MirTryExpression tryExpression:
                ValidateTryExcept(tryExpression, functionReturnType, context, functions, catalog, diagnostics);
                break;
            case MirPropagateExpression propagate:
                ValidatePropagation(propagate, functionReturnType, context, functions, catalog, diagnostics);
                break;
            case MirUnwrapExpression unwrap:
                ValidateUnwrap(unwrap, functionReturnType, context, functions, catalog, diagnostics);
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

    private static void ValidateRecordConstruction(
        MirRecordConstructionExpression construction,
        MirType functionReturnType,
        string context,
        IReadOnlyDictionary<string, MirFunction> functions,
        EnumCatalog catalog,
        List<JavaScriptDiagnostic> diagnostics)
    {
        if (!catalog.TryGetRecord(construction.RecordTypeId, out _))
        {
            AddInvalid(diagnostics, $"unknown record '{construction.RecordTypeId}' for construction in {context}");
        }

        foreach (MirRecordFieldValue initializer in construction.Initializers)
        {
            ValidateExpression(initializer.Value, functionReturnType, context, functions, catalog, diagnostics);
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
            case MirArrayType array:
                ValidateValueType(array.ElementType, $"array element type in {context}", catalog, diagnostics, allowVoid: false);
                return;
            case MirType named when named is not MirArrayType and not MirResultType && named.Identifier is "number" or "boolean" or "string":
                return;
            case MirType { Identifier: "void" } when allowVoid:
                return;
            case MirType named when named is not MirArrayType and not MirResultType && catalog.ContainsEnum(named.Identifier):
                return;
            case MirRecordType record when catalog.ContainsRecord(record.RecordTypeId):
                return;
            case MirTableType table when catalog.ContainsTable(table.TableId):
                return;
            case MirTableRowType row when catalog.ContainsRow(row.RowTypeId):
                return;
            case MirColumnType column:
                ValidateValueType(column.ElementType, $"column element type in {context}", catalog, diagnostics, allowVoid: false);
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
        if (propagation.Operand.Type is not MirResultType operandResult)
        {
            AddInvalid(diagnostics, $"propagation requires a Result operand in {context}");
            return;
        }

        RequireMatchingType(propagation.Type, operandResult.SuccessType, $"propagation success value in {context}", diagnostics);
        if (propagation.Target is MirPropagationTarget.FunctionReturn)
        {
            if (functionReturnType is not MirResultType functionResult)
            {
                AddInvalid(diagnostics, $"function-return propagation requires a Result return type in {context}");
                return;
            }

            RequireMatchingType(functionResult.ErrorType, operandResult.ErrorType, $"propagation error type in {context}", diagnostics);
            return;
        }

        if (propagation.Target is not MirPropagationTarget.LexicalExcept)
        {
            AddUnsupported(diagnostics, $"propagation target '{propagation.Target}' in {context}");
        }
    }

    private static void ValidateTryExcept(MirTryExpression tryExpression, MirType functionReturnType, string context, IReadOnlyDictionary<string, MirFunction> functions, EnumCatalog catalog, List<JavaScriptDiagnostic> diagnostics)
    {
        RequireMatchingType(tryExpression.Protected.Type, tryExpression.Type, $"protected value of try/except in {context}", diagnostics);
        RequireMatchingType(tryExpression.Handler.Type, tryExpression.Type, $"handler value of try/except in {context}", diagnostics);
        RequireMatchingType(tryExpression.HandlerBinding.Type, tryExpression.HandledErrorType, $"handler binding of try/except in {context}", diagnostics);
        ValidateValueType(tryExpression.Type, $"try/except result in {context}", catalog, diagnostics, allowVoid: true);
        ValidateValueType(tryExpression.HandledErrorType, $"try/except handled error in {context}", catalog, diagnostics, allowVoid: false);
        ValidateValueBlock(tryExpression.Protected, functionReturnType, context, functions, catalog, diagnostics);
        ValidateValueBlock(tryExpression.Handler, functionReturnType, context, functions, catalog, diagnostics);
    }

    private static void ValidateValueBlock(MirValueBlock block, MirType functionReturnType, string context, IReadOnlyDictionary<string, MirFunction> functions, EnumCatalog catalog, List<JavaScriptDiagnostic> diagnostics)
    {
        foreach (MirStatement statement in block.PrefixStatements)
        {
            if (statement is not MirVariableDeclarationStatement and not MirExpressionStatement)
            {
                AddInvalid(diagnostics, $"try value block contains unsupported prefix statement '{statement.GetType().Name}' in {context}");
            }

            ValidateStatement(statement, functionReturnType, context, functions, catalog, diagnostics);
        }

        ValidateExpression(block.ValueExpression, functionReturnType, context, functions, catalog, diagnostics);
    }

    private static void ValidateUnwrap(MirUnwrapExpression unwrap, MirType functionReturnType, string context, IReadOnlyDictionary<string, MirFunction> functions, EnumCatalog catalog, List<JavaScriptDiagnostic> diagnostics)
    {
        ValidateExpression(unwrap.Operand, functionReturnType, context, functions, catalog, diagnostics);
        if (unwrap.Operand.Type is not MirResultType resultType)
        {
            AddInvalid(diagnostics, $"unwrap has non-Result operand '{unwrap.Operand.Type.Name}' in {context}");
            return;
        }

        RequireMatchingType(unwrap.Type, resultType.SuccessType, $"unwrap success value in {context}", diagnostics);
    }

    private static void AddUnsupported(List<JavaScriptDiagnostic> diagnostics, string feature)
    {
        diagnostics.Add(new JavaScriptDiagnostic(UnsupportedDiagnosticId, $"Unsupported MIR for JavaScript backend: {feature}."));
    }

    private static void AddInvalid(List<JavaScriptDiagnostic> diagnostics, string message)
    {
        diagnostics.Add(new JavaScriptDiagnostic(InvalidDiagnosticId, $"Invalid MIR for JavaScript backend: {message}."));
    }

    private static void EmitValueRuntime(JavaScriptTextWriter writer, IReadOnlyList<MirTsonEncodingPlan> tsonPlans, EnumCatalog catalog, ResultCatalog results, GeneratedNames names, bool usesUnwrap, bool usesTryExcept)
    {
        writer.WriteLine($"function {names.Panic}() {{");
        writer.Indent();
        writer.WriteLine("throw new Error(\"Copeland JavaScript backend invariant failure.\");");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
        if (usesUnwrap)
        {
            writer.WriteLine($"function {names.UnwrapPanic}(error) {{");
            writer.Indent();
            writer.WriteLine("const panic = new Error(\"COPE-PANIC-UNWRAP: Result unwrap encountered err\");");
            writer.WriteLine("panic.error = error;");
            writer.WriteLine("throw panic;");
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine();
        }
        bool usesTaggedValues = catalog.Enums.Count > 0 || results.Types.Count > 0 || usesTryExcept;
        if (usesTaggedValues)
        {
            writer.WriteLine($"function {names.MakeValue}(type, tag, payload) {{");
            writer.Indent();
            writer.WriteLine("const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));");
            foreach (EnumInfo enumInfo in catalog.Enums)
            {
                writer.WriteLine($"if (type === {names.TypeToken(enumInfo)}) {names.EnumInstances(enumInfo)}.add(value);");
            }
            writer.WriteLine("return value;");
            writer.Unindent();
            writer.WriteLine("}");
        }

        if (usesTryExcept)
        {
            writer.WriteLine();
            EmitFlowRuntime(writer, names);
        }

        foreach (MirRecordDefinition record in catalog.Records)
        {
            writer.WriteLine();
            EmitRecordRuntime(writer, record, names);
        }

        foreach (EnumInfo enumInfo in catalog.Enums)
        {
            writer.WriteLine();
            writer.WriteLine($"const {names.TypeToken(enumInfo)} = Object.freeze(Object.create(null));");
            writer.WriteLine($"const {names.EnumInstances(enumInfo)} = new WeakSet();");
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

        if (catalog.Tables.Count > 0)
        {
            writer.WriteLine();
            EmitColumnRuntime(writer, names);
        }

        foreach (MirTableDefinition table in catalog.Tables)
        {
            writer.WriteLine();
            EmitTableRuntime(writer, table, catalog, results, names);
        }

        if (tsonPlans.Count > 0)
        {
            writer.WriteLine();
            EmitTsonEncodingRuntime(writer, tsonPlans, catalog, results, names);
        }
    }

    private static void EmitColumnRuntime(JavaScriptTextWriter writer, GeneratedNames names)
    {
        writer.WriteLine($"const {names.ColumnCarrierToken} = Symbol({JavaScriptLiteralWriter.WriteString(names.SymbolDescription(names.ColumnCarrierToken, "cope.column"))});");
        writer.WriteLine($"const {names.ColumnReadSlot} = Symbol({JavaScriptLiteralWriter.WriteString(names.SymbolDescription(names.ColumnReadSlot, "cope.column.read"))});");
        if (names.UsesTsonTableEncoding)
        {
            writer.WriteLine($"const {names.ColumnInstances} = new WeakSet();");
            writer.WriteLine($"const {names.ColumnValuesSlot} = Symbol({JavaScriptLiteralWriter.WriteString(names.SymbolDescription(names.ColumnValuesSlot, "cope.column.values"))});");
        }
        writer.WriteLine($"const {names.TableRowTableSlot} = Symbol({JavaScriptLiteralWriter.WriteString(names.SymbolDescription(names.TableRowTableSlot, "cope.table.row.table"))});");
        writer.WriteLine($"const {names.TableRowIndexSlot} = Symbol({JavaScriptLiteralWriter.WriteString(names.SymbolDescription(names.TableRowIndexSlot, "cope.table.row.index"))});");
        writer.WriteLine();
        writer.WriteLine($"function {names.ColumnValidator}(value) {{");
        writer.Indent();
        string tsonValidation = names.UsesTsonTableEncoding
            ? $" || !{names.ColumnInstances}.has(value) || !Object.prototype.hasOwnProperty.call(value, {names.ColumnValuesSlot}) || !Array.isArray(value[{names.ColumnValuesSlot}]) || !Object.isFrozen(value[{names.ColumnValuesSlot}])"
            : " || Object.getOwnPropertySymbols(value).length !== 3";
        writer.WriteLine($"if (typeof value !== \"object\" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, {names.ColumnCarrierToken}) || value[{names.ColumnCarrierToken}] !== {names.ColumnCarrierToken} || !Object.prototype.hasOwnProperty.call(value, {names.ColumnReadSlot}) || typeof value[{names.ColumnReadSlot}] !== \"function\"{tsonValidation}) {{");
        writer.Indent();
        writer.WriteLine($"{names.Panic}();");
        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitTableRuntime(
        JavaScriptTextWriter writer,
        MirTableDefinition table,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names)
    {
        var boundsErrorToken = names.TypeToken(catalog.GetEnum("TableBoundsError"));
        writer.WriteLine($"const {names.TableTypeToken(table)} = Symbol({JavaScriptLiteralWriter.WriteString(names.SymbolDescription(names.TableTypeToken(table), table.Id.Value))});");
        if (names.UsesTsonTableEncoding)
        {
            writer.WriteLine($"const {names.TableInstances(table)} = new WeakSet();");
        }
        writer.WriteLine($"const {names.TableRowTypeToken(table)} = Symbol({JavaScriptLiteralWriter.WriteString(names.SymbolDescription(names.TableRowTypeToken(table), table.RowTypeId))});");
        writer.WriteLine($"const {names.TableRowReadSlot(table)} = Symbol({JavaScriptLiteralWriter.WriteString(names.SymbolDescription(names.TableRowReadSlot(table), table.Id.Value + ".rows"))});");
        foreach (MirTableColumnDefinition column in table.Columns)
        {
            writer.WriteLine($"const {names.TableColumnSlot(column)} = Symbol({JavaScriptLiteralWriter.WriteString(names.SymbolDescription(names.TableColumnSlot(column), column.Id.Value))});");
            writer.WriteLine($"const {names.TableColumnToken(column)} = Symbol({JavaScriptLiteralWriter.WriteString(names.SymbolDescription(names.TableColumnToken(column), column.Id.Value + ".column"))});");
        }

        writer.WriteLine();
        EmitTableValidator(writer, table, names);
        writer.WriteLine();
        EmitTableRowValidator(writer, table, names);
        writer.WriteLine();
        writer.WriteLine($"function {names.TableCreateRow(table)}(tableValue, index) {{");
        writer.Indent();
        writer.WriteLine("const row = Object.create(null);");
        writer.WriteLine("Object.defineProperties(row, {");
        writer.Indent();
        writer.WriteLine($"[{names.TableRowTypeToken(table)}]: {{ value: {names.TableRowTypeToken(table)}, writable: false, enumerable: false, configurable: false }},");
        writer.WriteLine($"[{names.TableRowTableSlot}]: {{ value: tableValue, writable: false, enumerable: false, configurable: false }},");
        writer.WriteLine($"[{names.TableRowIndexSlot}]: {{ value: index, writable: false, enumerable: false, configurable: false }},");
        writer.Unindent();
        writer.WriteLine("});");
        writer.WriteLine("return Object.freeze(row);");
        writer.Unindent();
        writer.WriteLine("}");

        writer.WriteLine();
        writer.WriteLine($"function {names.TableCreate(table)}() {{");
        writer.Indent();
        foreach (MirTableColumnDefinition column in table.Columns)
        {
            string values = string.Join(", ", column.Constants.Select(constant => EmitTableConstant(constant, catalog, results, names)));
            writer.WriteLine($"const {names.TableStorage(column)} = Object.freeze([{values}]);");
            writer.WriteLine($"const {names.TableColumnValue(column)} = Object.create(null);");
            writer.WriteLine($"Object.defineProperties({names.TableColumnValue(column)}, {{");
            writer.Indent();
            writer.WriteLine($"[{names.ColumnCarrierToken}]: {{ value: {names.ColumnCarrierToken}, writable: false, enumerable: false, configurable: false }},");
            writer.WriteLine($"[{names.TableColumnToken(column)}]: {{ value: {names.TableColumnToken(column)}, writable: false, enumerable: false, configurable: false }},");
            if (names.UsesTsonTableEncoding)
            {
                writer.WriteLine($"[{names.ColumnValuesSlot}]: {{ value: {names.TableStorage(column)}, writable: false, enumerable: false, configurable: false }},");
            }
            writer.WriteLine($"[{names.ColumnReadSlot}]: {{ value: (index) => {{");
            writer.Indent();
            EmitBoundsCheckedResult(writer, "index", table.RowCount, column.ElementType, results, names, boundsErrorToken, $"{names.TableStorage(column)}[index]");
            writer.Unindent();
            writer.WriteLine("}, writable: false, enumerable: false, configurable: false },");
            writer.Unindent();
            writer.WriteLine("});");
            writer.WriteLine($"Object.freeze({names.TableColumnValue(column)});");
            if (names.UsesTsonTableEncoding)
            {
                writer.WriteLine($"{names.ColumnInstances}.add({names.TableColumnValue(column)});");
            }
        }

        writer.WriteLine("const value = Object.create(null);");
        writer.WriteLine("Object.defineProperties(value, {");
        writer.Indent();
        writer.WriteLine($"[{names.TableTypeToken(table)}]: {{ value: {names.TableTypeToken(table)}, writable: false, enumerable: false, configurable: false }},");
        writer.WriteLine($"[{names.TableRowReadSlot(table)}]: {{ value: (index) => {{");
        writer.Indent();
        MirResultType rowResult = new(new MirTableRowType(table.RowTypeId, table.Name + ".Row"), new MirNamedType("TableBoundsError"));
        EmitBoundsCheckedResult(writer, "index", table.RowCount, rowResult.SuccessType, results, names, boundsErrorToken, $"{names.TableCreateRow(table)}(value, index)");
        writer.Unindent();
        writer.WriteLine("}, writable: false, enumerable: false, configurable: false },");
        foreach (MirTableColumnDefinition column in table.Columns)
        {
            writer.WriteLine($"[{names.TableColumnSlot(column)}]: {{ value: {names.TableColumnValue(column)}, writable: false, enumerable: false, configurable: false }},");
        }
        writer.Unindent();
        writer.WriteLine("});");
        if (names.UsesTsonTableEncoding)
        {
            writer.WriteLine("Object.freeze(value);");
            writer.WriteLine($"{names.TableInstances(table)}.add(value);");
            writer.WriteLine("return value;");
        }
        else
        {
            writer.WriteLine("return Object.freeze(value);");
        }
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine($"const {names.TableSingleton(table)} = {names.TableCreate(table)}();");
    }

    private static void EmitTableValidator(JavaScriptTextWriter writer, MirTableDefinition table, GeneratedNames names)
    {
        writer.WriteLine($"function {names.TableValidator(table)}(value) {{");
        writer.Indent();
        string tableValidation = names.UsesTsonTableEncoding
            ? $"if (typeof value !== \"object\" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !{names.TableInstances(table)}.has(value) || !Object.prototype.hasOwnProperty.call(value, {names.TableTypeToken(table)}) || value[{names.TableTypeToken(table)}] !== {names.TableTypeToken(table)} || !Object.prototype.hasOwnProperty.call(value, {names.TableRowReadSlot(table)}) || typeof value[{names.TableRowReadSlot(table)}] !== \"function\") {{"
            : $"if (typeof value !== \"object\" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, {names.TableTypeToken(table)}) || value[{names.TableTypeToken(table)}] !== {names.TableTypeToken(table)} || !Object.prototype.hasOwnProperty.call(value, {names.TableRowReadSlot(table)}) || typeof value[{names.TableRowReadSlot(table)}] !== \"function\" || Object.getOwnPropertySymbols(value).length !== {table.Columns.Count + 2}) {{";
        writer.WriteLine(tableValidation);
        writer.Indent();
        writer.WriteLine($"{names.Panic}();");
        writer.Unindent();
        writer.WriteLine("}");
        foreach (MirTableColumnDefinition column in table.Columns)
        {
            writer.WriteLine($"if (!Object.prototype.hasOwnProperty.call(value, {names.TableColumnSlot(column)})) {{ {names.Panic}(); }}");
        }
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitTableRowValidator(JavaScriptTextWriter writer, MirTableDefinition table, GeneratedNames names)
    {
        writer.WriteLine($"function {names.TableRowValidator(table)}(value) {{");
        writer.Indent();
        string rowSymbolValidation = names.UsesTsonTableEncoding ? string.Empty : " || Object.getOwnPropertySymbols(value).length !== 3";
        writer.WriteLine($"if (typeof value !== \"object\" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, {names.TableRowTypeToken(table)}) || value[{names.TableRowTypeToken(table)}] !== {names.TableRowTypeToken(table)} || !Object.prototype.hasOwnProperty.call(value, {names.TableRowTableSlot}) || !Object.prototype.hasOwnProperty.call(value, {names.TableRowIndexSlot}) || !Number.isInteger(value[{names.TableRowIndexSlot}]){rowSymbolValidation}) {{");
        writer.Indent();
        writer.WriteLine($"{names.Panic}();");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"{names.TableValidator(table)}(value[{names.TableRowTableSlot}]);");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitBoundsCheckedResult(
        JavaScriptTextWriter writer,
        string index,
        int rowCount,
        MirType successType,
        ResultCatalog results,
        GeneratedNames names,
        string boundsErrorToken,
        string successValue)
    {
        MirResultType resultType = new(successType, new MirNamedType("TableBoundsError"));
        var resultToken = names.TypeToken(results.Get(resultType));
        writer.WriteLine($"if (!Number.isFinite({index}) || !Number.isInteger({index})) {{");
        writer.Indent();
        writer.WriteLine($"return {names.MakeValue}({resultToken}, \"err\", [{names.MakeValue}({boundsErrorToken}, \"InvalidIndex\", [{index}])]);");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"if ({index} < 0 || {index} >= {rowCount}) {{");
        writer.Indent();
        writer.WriteLine($"return {names.MakeValue}({resultToken}, \"err\", [{names.MakeValue}({boundsErrorToken}, \"OutOfBounds\", [{index}, {rowCount}])]);");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"return {names.MakeValue}({resultToken}, \"ok\", [{successValue}]);");
    }

    private static string EmitTableConstant(MirTableConstant constant, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        return constant switch
        {
            MirTableLiteralConstant { Value: bool value } => value ? "true" : "false",
            MirTableLiteralConstant { Value: string value } => JavaScriptLiteralWriter.WriteString(value),
            MirTableLiteralConstant literal => JavaScriptLiteralWriter.WriteNumber(literal.Value),
            MirTableArrayConstant array => $"Object.freeze([{string.Join(", ", array.Elements.Select(element => EmitTableConstant(element, catalog, results, names))) }])",
            MirTableRecordConstant record => EmitTableRecordConstant(record, catalog, results, names),
            MirTableEnumConstant value => $"{names.MakeValue}({names.TypeToken(catalog.GetEnum(value.EnumName))}, {JavaScriptLiteralWriter.WriteString(value.CaseName)}, [{string.Join(", ", value.Payloads.Select(payload => EmitTableConstant(payload, catalog, results, names)))}])",
            MirTableResultConstant result => $"{names.MakeValue}({names.TypeToken(results.Get(result.Type))}, \"{(result.IsOk ? "ok" : "err")}\", [{EmitTableConstant(result.Payload, catalog, results, names)}])",
            _ => throw new InvalidOperationException($"Unsupported validated table constant {constant.GetType().Name}."),
        };
    }

    private static string EmitTableRecordConstant(MirTableRecordConstant constant, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        MirRecordDefinition record = catalog.GetRecord(constant.RecordTypeId);
        var values = constant.Fields.ToDictionary(field => field.FieldId, field => field.Value);
        return $"{names.RecordConstructor(record)}({string.Join(", ", record.Fields.Select(field => EmitTableConstant(values[field.Id], catalog, results, names)))})";
    }

    private static void EmitRecordRuntime(JavaScriptTextWriter writer, MirRecordDefinition record, GeneratedNames names)
    {
        var typeToken = names.RecordTypeToken(record);
        writer.WriteLine($"const {typeToken} = Symbol({JavaScriptLiteralWriter.WriteString(names.SymbolDescription(typeToken, record.Id.Value))});");
        writer.WriteLine($"const {names.RecordInstances(record)} = new WeakSet();");
        foreach (MirRecordFieldDefinition field in record.Fields)
        {
            writer.WriteLine($"const {names.RecordFieldSlot(field)} = Symbol({JavaScriptLiteralWriter.WriteString(names.SymbolDescription(names.RecordFieldSlot(field), field.Id.Value))});");
        }

        writer.WriteLine();
        string parameters = string.Join(", ", record.Fields.Select((_, index) => $"field{index}"));
        writer.WriteLine($"function {names.RecordConstructor(record)}({parameters}) {{");
        writer.Indent();
        writer.WriteLine("const value = Object.create(null);");
        writer.WriteLine("Object.defineProperties(value, {");
        writer.Indent();
        writer.WriteLine($"[{typeToken}]: {{ value: {typeToken}, writable: false, enumerable: false, configurable: false }},");
        for (int index = 0; index < record.Fields.Count; index += 1)
        {
            writer.WriteLine($"[{names.RecordFieldSlot(record.Fields[index])}]: {{ value: field{index}, writable: false, enumerable: false, configurable: false }},");
        }
        writer.Unindent();
        writer.WriteLine("});");
        writer.WriteLine("Object.freeze(value);");
        writer.WriteLine($"{names.RecordInstances(record)}.add(value);");
        writer.WriteLine("return value;");
        writer.Unindent();
        writer.WriteLine("}");

        writer.WriteLine();
        writer.WriteLine($"function {names.RecordValidator(record)}(value) {{");
        writer.Indent();
        var conditions = new List<string>
        {
            "typeof value !== \"object\"",
            "value === null",
            "Object.getPrototypeOf(value) !== null",
            "!Object.isFrozen(value)",
            $"!{names.RecordInstances(record)}.has(value)",
            $"!Object.prototype.hasOwnProperty.call(value, {typeToken})",
            $"value[{typeToken}] !== {typeToken}",
        };
        conditions.AddRange(record.Fields.Select(field => $"!Object.prototype.hasOwnProperty.call(value, {names.RecordFieldSlot(field)})"));
        writer.WriteLine($"if ({string.Join(" || ", conditions)}) {{");
        writer.Indent();
        writer.WriteLine($"{names.Panic}();");
        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitValidator(JavaScriptTextWriter writer, EnumInfo enumInfo, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        writer.WriteLine($"function {names.Validator(enumInfo)}(value) {{");
        writer.Indent();
        writer.WriteLine($"if (typeof value !== \"object\" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !{names.EnumInstances(enumInfo)}.has(value) || !Object.prototype.hasOwnProperty.call(value, \"$type\") || !Object.prototype.hasOwnProperty.call(value, \"$tag\") || !Object.prototype.hasOwnProperty.call(value, \"$payload\") || value.$type !== {names.TypeToken(enumInfo)} || typeof value.$tag !== \"string\" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {{");
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
            MirRecordType record when catalog.TryGetRecord(record.RecordTypeId, out MirRecordDefinition definition) => $"({names.RecordValidator(definition)}({expression}), true)",
            MirTableType table when catalog.ContainsTable(table.TableId) => $"({names.TableValidator(catalog.GetTable(table.TableId))}({expression}), true)",
            MirTableRowType row when catalog.ContainsRow(row.RowTypeId) => $"({names.TableRowValidator(catalog.GetTableByRowType(row.RowTypeId))}({expression}), true)",
            MirColumnType => $"({names.ColumnValidator}({expression}), true)",
            MirArrayType => $"Array.isArray({expression})",
            MirType named when named is not MirArrayType and not MirResultType && catalog.TryGetEnum(named.Identifier, out EnumInfo enumInfo) => $"({names.Validator(enumInfo)}({expression}), true)",
            _ => "false",
        };
    }

    private static void EmitFlowRuntime(JavaScriptTextWriter writer, GeneratedNames names)
    {
        writer.WriteLine($"const {names.FlowToken} = Object.freeze(Object.create(null));");
        writer.WriteLine();
        writer.WriteLine($"function {names.FlowValue}(value) {{");
        writer.Indent();
        writer.WriteLine($"return Object.freeze(Object.assign(Object.create(null), {{ $flow: {names.FlowToken}, $kind: \"value\", $value: value }}));");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine($"function {names.FlowToHandler}(handler, error) {{");
        writer.Indent();
        writer.WriteLine($"return Object.freeze(Object.assign(Object.create(null), {{ $flow: {names.FlowToken}, $kind: \"handler\", $handler: handler, $error: error }}));");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine($"function {names.FlowToFunction}(error) {{");
        writer.Indent();
        writer.WriteLine($"return Object.freeze(Object.assign(Object.create(null), {{ $flow: {names.FlowToken}, $kind: \"function\", $error: error }}));");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine($"function {names.ValidateFlow}(flow) {{");
        writer.Indent();
        writer.WriteLine($"if (typeof flow !== \"object\" || flow === null || Object.getPrototypeOf(flow) !== null || !Object.isFrozen(flow) || flow.$flow !== {names.FlowToken} || typeof flow.$kind !== \"string\") {{");
        writer.Indent();
        writer.WriteLine($"{names.Panic}();");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("switch (flow.$kind) {");
        writer.Indent();
        writer.WriteLine("case \"value\":");
        writer.Indent();
        writer.WriteLine("if (!Object.prototype.hasOwnProperty.call(flow, \"$value\")) { " + names.Panic + "(); }");
        writer.WriteLine("return;");
        writer.Unindent();
        writer.WriteLine("case \"handler\":");
        writer.Indent();
        writer.WriteLine("if (!Number.isInteger(flow.$handler) || !Object.prototype.hasOwnProperty.call(flow, \"$error\")) { " + names.Panic + "(); }");
        writer.WriteLine("return;");
        writer.Unindent();
        writer.WriteLine("case \"function\":");
        writer.Indent();
        writer.WriteLine("if (!Object.prototype.hasOwnProperty.call(flow, \"$error\")) { " + names.Panic + "(); }");
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

    private static void EmitFunction(JavaScriptTextWriter writer, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        JavaScriptScopeId functionScope = names.EnterFunction(function);
        writer.EnterScope(functionScope);
        string parameters = string.Join(", ", function.Parameters.Select(parameter => JavaScriptIdentifierEncoder.Encode(parameter.Name)));
        writer.WriteLine($"function {JavaScriptIdentifierEncoder.Encode(function.Name)}({parameters}) {{");
        writer.Indent();

        bool usesTryExcept = FunctionUsesTryExcept(function);
        if (usesTryExcept)
        {
            var flow = names.NextTemporary("function_flow");
            writer.WriteLine($"const {flow} = (() => {{");
            writer.Indent();
            foreach (MirStatement statement in function.Body)
            {
                EmitStatement(writer, statement, function, catalog, results, names, flowEnabled: true);
            }
            writer.WriteLine($"return {names.FlowValue}(undefined);");
            writer.Unindent();
            writer.WriteLine("})();");
            writer.WriteLine($"{names.ValidateFlow}({flow});");
            writer.WriteLine($"if ({flow}.$kind === \"value\") {{");
            writer.Indent();
            writer.WriteLine($"return {flow}.$value;");
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine($"if ({flow}.$kind === \"function\") {{");
            writer.Indent();
            if (function.ReturnType is MirResultType functionResult)
            {
                writer.WriteLine($"return {names.MakeValue}({names.TypeToken(results.Get(functionResult))}, \"err\", [{flow}.$error]);");
            }
            else
            {
                writer.WriteLine($"{names.Panic}();");
            }
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine($"{names.Panic}();");
            writer.Unindent();
            writer.WriteLine("}");
            names.LeaveFunction();
            writer.EnterScope(names.Document.ProgramScope);
            return;
        }

        foreach (MirStatement statement in function.Body)
        {
            EmitStatement(writer, statement, function, catalog, results, names, flowEnabled: false);
        }

        writer.Unindent();
        writer.WriteLine("}");
        names.LeaveFunction();
        writer.EnterScope(names.Document.ProgramScope);
    }

    private static void EmitStatement(JavaScriptTextWriter writer, MirStatement statement, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names, bool flowEnabled)
    {
        switch (statement)
        {
            case MirVariableDeclarationStatement declaration:
                EmittedExpression initializer = EmitExpression(declaration.Initializer, function, catalog, results, names, flowEnabled);
                WritePrelude(writer, initializer.Prelude);
                string declarationKeyword = declaration.Local.IsReadOnly ? "const" : "let";
                writer.WriteLine($"{declarationKeyword} {JavaScriptIdentifierEncoder.Encode(declaration.Local.Name)} = {initializer.Value};");
                break;
            case MirReturnStatement { Expression: null } when function.ReturnType is MirResultType result && result.SuccessType is MirType { Identifier: "void" }:
                writer.WriteLine(flowEnabled
                    ? $"return {names.FlowValue}({names.MakeValue}({names.TypeToken(results.Get(result))}, \"ok\", [null]));"
                    : $"return {names.MakeValue}({names.TypeToken(results.Get(result))}, \"ok\", [null]);");
                break;
            case MirReturnStatement returnStatement when returnStatement.Expression is null:
                writer.WriteLine(flowEnabled ? $"return {names.FlowValue}(undefined);" : "return;");
                break;
            case MirReturnStatement returnStatement:
                EmittedExpression returned = EmitExpression(returnStatement.Expression!, function, catalog, results, names, flowEnabled);
                WritePrelude(writer, returned.Prelude);
                writer.WriteLine(flowEnabled ? $"return {names.FlowValue}({returned.Value});" : $"return {returned.Value};");
                break;
            case MirExpressionStatement expressionStatement:
                EmittedExpression expression = EmitExpression(expressionStatement.Expression, function, catalog, results, names, flowEnabled);
                WritePrelude(writer, expression.Prelude);
                writer.WriteLine($"{expression.Value};");
                break;
            default:
                throw new InvalidOperationException($"Validated JavaScript emission received unsupported statement {statement.GetType().Name}.");
        }
    }

    private static EmittedExpression EmitExpression(MirExpression expression, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names, bool flowEnabled)
    {
        return expression switch
        {
            MirLiteralExpression { Value: bool boolean } => EmittedExpression.ValueOnly(boolean ? "true" : "false"),
            MirLiteralExpression { Value: string text } => EmittedExpression.ValueOnly(JavaScriptLiteralWriter.WriteString(text)),
            MirLiteralExpression { Value: not null } literal => EmittedExpression.ValueOnly(JavaScriptLiteralWriter.WriteNumber(literal.Value)),
            MirUnitExpression => EmittedExpression.ValueOnly("null"),
            MirVariableExpression variable => EmittedExpression.ValueOnly(JavaScriptIdentifierEncoder.Encode(variable.Name)),
            MirAssignmentExpression assignment => EmitAssignment(assignment, function, catalog, results, names, flowEnabled),
            MirUnaryExpression unary => EmitUnary(unary, function, catalog, results, names, flowEnabled),
            MirBinaryExpression binary => EmitBinary(binary, function, catalog, results, names, flowEnabled),
            MirCallExpression call => EmitCall(call, function, catalog, results, names, flowEnabled),
            MirArrayExpression array => EmitArrayExpression(array, function, catalog, results, names, flowEnabled),
            MirRecordConstructionExpression construction => EmitRecordConstruction(construction, function, catalog, results, names, flowEnabled),
            MirRecordFieldAccessExpression access => EmitRecordFieldAccess(access, function, catalog, results, names, flowEnabled),
            MirTableReferenceExpression reference => EmittedExpression.ValueOnly(names.TableSingleton(catalog.GetTable(reference.TableId))),
            MirTableColumnAccessExpression access => EmitTableColumnAccess(access, function, catalog, results, names, flowEnabled),
            MirTableRowAccessExpression access => EmitTableRowAccess(access, function, catalog, results, names, flowEnabled),
            MirColumnElementAccessExpression access => EmitColumnElementAccess(access, function, catalog, results, names, flowEnabled),
            MirTableRowFieldAccessExpression access => EmitTableRowFieldAccess(access, function, catalog, results, names, flowEnabled),
            MirRecordWithExpression withExpression => EmitRecordWith(withExpression, function, catalog, results, names, flowEnabled),
            MirEnumValueExpression value => EmitEnumValueExpression(value, function, catalog, results, names, flowEnabled),
            MirMatchExpression match => EmitEnumMatchExpression(match, function, catalog, results, names, flowEnabled),
            MirResultMatchExpression match => EmitResultMatchExpression(match, function, catalog, results, names, flowEnabled),
            MirIfExpression conditional => EmitIfExpression(conditional, function, catalog, results, names, flowEnabled),
            MirOkExpression ok => EmitResultConstruction(ok.Payload, (MirResultType)ok.Type, "ok", function, catalog, results, names, flowEnabled),
            MirErrExpression err => EmitResultConstruction(err.Payload, (MirResultType)err.Type, "err", function, catalog, results, names, flowEnabled),
            MirPropagateExpression propagation => EmitPropagation(propagation, function, catalog, results, names, flowEnabled),
            MirUnwrapExpression unwrap => EmitUnwrap(unwrap, function, catalog, results, names, flowEnabled),
            MirTryExpression tryExpression => EmitTryExcept(tryExpression, function, catalog, results, names, flowEnabled),
            MirTsonEncodeExpression encode => EmitTsonEncode(encode, function, catalog, results, names, flowEnabled),
            _ => throw new InvalidOperationException($"Validated JavaScript emission received unsupported expression {expression.GetType().Name}.")
        };
    }

    private static EmittedExpression EmitUnary(
        MirUnaryExpression unary,
        MirFunction function,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names,
        bool flowEnabled)
    {
        EmittedExpression operand = EmitExpression(unary.Operand, function, catalog, results, names, flowEnabled);
        return new EmittedExpression(operand.Prelude, $"({unary.Operator}{operand.Value})");
    }

    private static EmittedExpression EmitTsonEncode(
        MirTsonEncodeExpression encode,
        MirFunction function,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names,
        bool flowEnabled)
    {
        EmittedExpression operand = EmitExpression(encode.Operand, function, catalog, results, names, flowEnabled);
        string property = JavaScriptLiteralWriter.WriteString(encode.PlanId.Value);
        return new EmittedExpression(operand.Prelude, $"{names.TsonRuntime}[{property}]({operand.Value})");
    }

    private static void EmitTsonEncodingRuntime(
        JavaScriptTextWriter writer,
        IReadOnlyList<MirTsonEncodingPlan> plans,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names)
    {
        var tsonNames = TsonGeneratedNames.Create(plans, names);
        writer.WriteLine($"const {names.TsonRuntime} = (() => {{");
        writer.Indent();
        bool needsArrayWriter = plans.Any(plan => CollectTsonArrayPlans(plan).Count > 0);
        EmitJavaScriptTsonWriter(writer, needsArrayWriter, tsonNames);
        writer.WriteLine();
        writer.WriteLine($"function {tsonNames.BooleanWriter}(writer, value, indentation) {{ return writer.static(value ? \"true\" : \"false\"); }}");
        writer.WriteLine($"function {tsonNames.NumberWriter}(writer, value, indentation) {{ return writer.number(value); }}");
        writer.WriteLine($"function {tsonNames.StringWriter}(writer, value, indentation) {{ return writer.string(value); }}");

        for (int planIndex = 0; planIndex < plans.Count; planIndex++)
        {
            writer.WriteLine();
            EmitJavaScriptTsonPlan(writer, plans[planIndex], planIndex, catalog, results, names, tsonNames);
        }

        writer.WriteLine();
        writer.WriteLine("const api = Object.create(null);");
        for (int planIndex = 0; planIndex < plans.Count; planIndex++)
        {
            writer.WriteLine($"Object.defineProperty(api, {JavaScriptLiteralWriter.WriteString(plans[planIndex].Id.Value)}, {{ value: {tsonNames.Encoder(planIndex)}, writable: false, enumerable: false, configurable: false }});");
        }
        writer.WriteLine("return Object.freeze(api);");
        writer.Unindent();
        writer.WriteLine("})();");
    }

    private static void EmitJavaScriptTsonWriter(JavaScriptTextWriter writer, bool needsArrayWriter, TsonGeneratedNames tsonNames)
    {
        writer.WriteLine($"function {tsonNames.MakeWriter}(maximumBytes, maximumStringCodeUnits) {{");
        writer.Indent();
        writer.WriteLine("const parts = [];");
        writer.WriteLine("const bitsBuffer = new ArrayBuffer(8);");
        writer.WriteLine("const bitsView = new DataView(bitsBuffer);");
        writer.WriteLine("let byteCount = 0;");
        writer.WriteLine("let error = null;");
        writer.WriteLine($"function {tsonNames.WriterFail}(kind) {{ if (error === null) error = kind; return false; }}");
        writer.WriteLine($"function {tsonNames.WriterAppend}(value) {{");
        writer.Indent();
        writer.WriteLine("let added = 0;");
        writer.WriteLine("for (let index = 0; index < value.length; index += 1) {");
        writer.Indent();
        writer.WriteLine("const code = value.charCodeAt(index);");
        writer.WriteLine("if (code <= 0x7F) added += 1;");
        writer.WriteLine("else if (code <= 0x7FF) added += 2;");
        writer.WriteLine("else if (code >= 0xD800 && code <= 0xDBFF) {");
        writer.Indent();
        writer.WriteLine($"if (index + 1 >= value.length) return {tsonNames.WriterFail}(\"invalid\");");
        writer.WriteLine("const low = value.charCodeAt(index + 1);");
        writer.WriteLine($"if (low < 0xDC00 || low > 0xDFFF) return {tsonNames.WriterFail}(\"invalid\");");
        writer.WriteLine("added += 4;");
        writer.WriteLine("index += 1;");
        writer.Unindent();
        writer.WriteLine($"}} else if (code >= 0xDC00 && code <= 0xDFFF) return {tsonNames.WriterFail}(\"invalid\");");
        writer.WriteLine("else added += 3;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"if (byteCount > maximumBytes - added) return {tsonNames.WriterFail}(\"limit\");");
        writer.WriteLine("byteCount += added;");
        writer.WriteLine("parts.push(value);");
        writer.WriteLine("return true;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"function {tsonNames.UnicodeEscape}(code) {{ return {tsonNames.WriterAppend}(\"\\\\u\" + code.toString(16).toUpperCase().padStart(4, \"0\")); }}");
        writer.WriteLine($"function {tsonNames.StringWriterValue}(value) {{");
        writer.Indent();
        writer.WriteLine($"if (value.length > maximumStringCodeUnits) return {tsonNames.WriterFail}(\"limit\");");
        writer.WriteLine("for (let index = 0; index < value.length; index += 1) {");
        writer.Indent();
        writer.WriteLine("const code = value.charCodeAt(index);");
        writer.WriteLine("if (code >= 0xD800 && code <= 0xDBFF) {");
        writer.Indent();
        writer.WriteLine($"if (index + 1 >= value.length) return {tsonNames.WriterFail}(\"invalid\");");
        writer.WriteLine("const low = value.charCodeAt(index + 1);");
        writer.WriteLine($"if (low < 0xDC00 || low > 0xDFFF) return {tsonNames.WriterFail}(\"invalid\");");
        writer.WriteLine("index += 1;");
        writer.Unindent();
        writer.WriteLine($"}} else if (code >= 0xDC00 && code <= 0xDFFF) return {tsonNames.WriterFail}(\"invalid\");");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"if (!{tsonNames.WriterAppend}(\"\\\"\")) return false;");
        writer.WriteLine("for (let index = 0; index < value.length; index += 1) {");
        writer.Indent();
        writer.WriteLine("const code = value.charCodeAt(index);");
        writer.WriteLine($"if (code === 0x22) {{ if (!{tsonNames.WriterAppend}(\"\\\\\\\"\")) return false; }}");
        writer.WriteLine($"else if (code === 0x5C) {{ if (!{tsonNames.WriterAppend}(\"\\\\\\\\\")) return false; }}");
        writer.WriteLine($"else if (code === 0x08) {{ if (!{tsonNames.WriterAppend}(\"\\\\b\")) return false; }}");
        writer.WriteLine($"else if (code === 0x0C) {{ if (!{tsonNames.WriterAppend}(\"\\\\f\")) return false; }}");
        writer.WriteLine($"else if (code === 0x0A) {{ if (!{tsonNames.WriterAppend}(\"\\\\n\")) return false; }}");
        writer.WriteLine($"else if (code === 0x0D) {{ if (!{tsonNames.WriterAppend}(\"\\\\r\")) return false; }}");
        writer.WriteLine($"else if (code === 0x09) {{ if (!{tsonNames.WriterAppend}(\"\\\\t\")) return false; }}");
        writer.WriteLine($"else if (code < 0x20 || code === 0x2028 || code === 0x2029) {{ if (!{tsonNames.UnicodeEscape}(code)) return false; }}");
        writer.WriteLine("else if (code >= 0xD800 && code <= 0xDBFF) {");
        writer.Indent();
        writer.WriteLine($"if (!{tsonNames.WriterAppend}(value.slice(index, index + 2))) return false;");
        writer.WriteLine("index += 1;");
        writer.Unindent();
        writer.WriteLine($"}} else if (!{tsonNames.WriterAppend}(value[index])) return false;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"return {tsonNames.WriterAppend}(\"\\\"\");");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"function {tsonNames.NumberWriterValue}(value) {{");
        writer.Indent();
        writer.WriteLine("bitsView.setFloat64(0, value, false);");
        writer.WriteLine("let high = bitsView.getUint32(0, false);");
        writer.WriteLine("let low = bitsView.getUint32(4, false);");
        writer.WriteLine("if ((high & 0x7FF00000) === 0x7FF00000 && ((high & 0x000FFFFF) !== 0 || low !== 0)) { high = 0x7FF80000; low = 0; }");
        writer.WriteLine("const hexadecimal = high.toString(16).toUpperCase().padStart(8, \"0\") + low.toString(16).toUpperCase().padStart(8, \"0\");");
        writer.WriteLine($"return {tsonNames.WriterAppend}(\"$number(\\\"\" + hexadecimal + \"\\\")\");");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("return Object.freeze({");
        writer.Indent();
        writer.WriteLine($"static: {tsonNames.WriterAppend},");
        writer.WriteLine($"indent: level => {tsonNames.WriterAppend}(\" \".repeat(level * 4)),");
        writer.WriteLine($"string: {tsonNames.StringWriterValue},");
        writer.WriteLine($"number: {tsonNames.NumberWriterValue},");
        if (needsArrayWriter)
        {
            writer.WriteLine($"outputLimit: () => {tsonNames.WriterFail}(\"limit\"),");
        }
        writer.WriteLine("error: () => error,");
        writer.WriteLine("finish: () => parts.join(\"\"),");
        writer.Unindent();
        writer.WriteLine("});");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitJavaScriptTsonPlan(
        JavaScriptTextWriter writer,
        MirTsonEncodingPlan plan,
        int planIndex,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names,
        TsonGeneratedNames tsonNames)
    {
        IReadOnlyList<MirTsonArrayPlan> arrayPlans = CollectTsonArrayPlans(plan);
        var recordIndexes = plan.Definitions.OfType<MirTsonRecordPlan>().Select((item, index) => (item, index)).ToDictionary(pair => pair.item.RecordTypeId, pair => pair.index);
        var enumIndexes = plan.Definitions.OfType<MirTsonEnumPlan>().Select((item, index) => (item, index)).ToDictionary(pair => pair.item.Name, pair => pair.index);
        foreach (MirTsonRecordPlan record in plan.Definitions.OfType<MirTsonRecordPlan>())
        {
            MirRecordDefinition carrier = catalog.GetRecord(record.RecordTypeId);
            writer.WriteLine($"function {tsonNames.RecordWriter(planIndex, recordIndexes[record.RecordTypeId])}(writer, value, indentation) {{");
            writer.Indent();
            writer.WriteLine($"{names.RecordValidator(carrier)}(value);");
            if (record.Fields.Count == 0)
            {
                writer.WriteLine($"return writer.static({JavaScriptLiteralWriter.WriteString($"$record.{record.Name}({{}})")});");
            }
            else
            {
                writer.WriteLine($"if (!writer.static({JavaScriptLiteralWriter.WriteString($"$record.{record.Name}({{\n")})) return false;");
                for (int index = 0; index < record.Fields.Count; index++)
                {
                    MirTsonRecordFieldPlan field = record.Fields[index];
                    MirRecordFieldDefinition carrierField = carrier.Fields[index];
                    writer.WriteLine("if (!writer.indent(indentation + 1)) return false;");
                    writer.WriteLine($"if (!writer.static({JavaScriptLiteralWriter.WriteString($"\"{field.Name}\": ")})) return false;");
                    writer.WriteLine($"if (!{JavaScriptTsonValueWriter(planIndex, field.ValuePlan, recordIndexes, enumIndexes, arrayPlans, tsonNames)}(writer, value[{names.RecordFieldSlot(carrierField)}], indentation + 1)) return false;");
                    writer.WriteLine("if (!writer.static(\",\\n\")) return false;");
                }
                writer.WriteLine("if (!writer.indent(indentation)) return false;");
                writer.WriteLine("return writer.static(\"})\");");
            }
            writer.Unindent();
            writer.WriteLine("}");
        }
        foreach (MirTsonEnumPlan @enum in plan.Definitions.OfType<MirTsonEnumPlan>())
        {
            EnumInfo carrier = catalog.GetEnum(@enum.Name);
            writer.WriteLine($"function {tsonNames.EnumWriter(planIndex, enumIndexes[@enum.Name])}(writer, value, indentation) {{");
            writer.Indent();
            writer.WriteLine($"{names.Validator(carrier)}(value);");
            writer.WriteLine("switch (value.$tag) {");
            writer.Indent();
            foreach (MirTsonEnumCasePlan @case in @enum.Cases)
            {
                writer.WriteLine($"case {JavaScriptLiteralWriter.WriteString(@case.Name)}:");
                writer.Indent();
                if (@case.Payloads.Count == 0)
                {
                    writer.WriteLine($"return writer.static({JavaScriptLiteralWriter.WriteString($"{@enum.Name}.{@case.Name}")});");
                }
                else
                {
                    writer.WriteLine($"if (!writer.static({JavaScriptLiteralWriter.WriteString($"{@enum.Name}.{@case.Name}(\n")})) return false;");
                    for (int index = 0; index < @case.Payloads.Count; index++)
                    {
                        MirTsonEnumPayloadPlan payload = @case.Payloads[index];
                        writer.WriteLine("if (!writer.indent(indentation + 1)) return false;");
                        writer.WriteLine($"if (!{JavaScriptTsonValueWriter(planIndex, payload.ValuePlan, recordIndexes, enumIndexes, arrayPlans, tsonNames)}(writer, value.$payload[{index}], indentation + 1)) return false;");
                        writer.WriteLine(index + 1 < @case.Payloads.Count
                            ? "if (!writer.static(\",\\n\")) return false;"
                            : "if (!writer.static(\"\\n\")) return false;");
                    }
                    writer.WriteLine("if (!writer.indent(indentation)) return false;");
                    writer.WriteLine("return writer.static(\")\");");
                }
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

        foreach (MirTsonArrayPlan arrayPlan in arrayPlans)
        {
            EmitJavaScriptTsonArrayWriter(writer, plan, planIndex, arrayPlan, recordIndexes, enumIndexes, arrayPlans, names, tsonNames);
        }

        if (plan.TablePlan is not null)
        {
            EmitJavaScriptTsonTablePlan(writer, plan, planIndex, catalog, results, names, recordIndexes, enumIndexes, arrayPlans, tsonNames);
            return;
        }

        MirResultType resultType = new(new MirNamedType("string"), new MirNamedType("TsonEncodeError"));
        var resultToken = names.TypeToken(results.Get(resultType));
        var errorToken = names.TypeToken(catalog.GetEnum("TsonEncodeError"));
        writer.WriteLine($"function {tsonNames.Encoder(planIndex)}(value) {{");
        writer.Indent();
        writer.WriteLine($"const writer = {tsonNames.MakeWriter}({plan.Limits.MaximumUtf8Bytes}, {plan.Limits.MaximumStringCodeUnits});");
        writer.WriteLine($"if (!writer.static({JavaScriptLiteralWriter.WriteString(MirTsonCanonicalText.BuildDocumentPrefix(plan))})");
        writer.Indent();
        writer.WriteLine($"|| !{JavaScriptTsonValueWriter(planIndex, plan.RootValuePlan, recordIndexes, enumIndexes, arrayPlans, tsonNames)}(writer, value, 0)");
        writer.WriteLine("|| !writer.static(\";\\n\")) {");
        writer.Unindent();
        writer.Indent();
        writer.WriteLine("const tag = writer.error() === \"invalid\" ? \"InvalidUnicode\" : \"OutputLimitExceeded\";");
        writer.WriteLine($"const error = {names.MakeValue}({errorToken}, tag, []);");
        writer.WriteLine($"return {names.MakeValue}({resultToken}, \"err\", [error]);");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"return {names.MakeValue}({resultToken}, \"ok\", [writer.finish()]);");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitJavaScriptTsonTablePlan(
        JavaScriptTextWriter writer,
        MirTsonEncodingPlan plan,
        int planIndex,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names,
        IReadOnlyDictionary<MirRecordTypeId, int> recordIndexes,
        IReadOnlyDictionary<string, int> enumIndexes,
        IReadOnlyList<MirTsonArrayPlan> arrayPlans,
        TsonGeneratedNames tsonNames)
    {
        MirTsonTablePlan tablePlan = plan.TablePlan!;
        MirTableDefinition table = catalog.GetTable(tablePlan.TableId);
        MirResultType resultType = new(new MirNamedType("string"), new MirNamedType("TsonEncodeError"));
        var resultToken = names.TypeToken(results.Get(resultType));
        var errorToken = names.TypeToken(catalog.GetEnum("TsonEncodeError"));
        writer.WriteLine($"function {tsonNames.Encoder(planIndex)}(value) {{");
        writer.Indent();
        writer.WriteLine($"{names.TableValidator(table)}(value);");
        for (int index = 0; index < tablePlan.Columns.Count; index++)
        {
            MirTsonTableColumnPlan column = tablePlan.Columns[index];
            MirTableColumnDefinition carrierColumn = table.Columns[index];
            writer.WriteLine($"const column{index} = value[{names.TableColumnSlot(carrierColumn)}];");
            writer.WriteLine($"{names.ColumnValidator}(column{index});");
            writer.WriteLine($"if (column{index}[{names.TableColumnToken(carrierColumn)}] !== {names.TableColumnToken(carrierColumn)}) {{ {names.Panic}(); }}");
            writer.WriteLine($"const cells{index} = column{index}[{names.ColumnValuesSlot}];");
            writer.WriteLine($"const length{index} = cells{index}.length;");
            writer.WriteLine($"if (length{index} !== {column.ExpectedElementCount}) {{ {names.Panic}(); }}");
        }
        writer.WriteLine($"const writer = {tsonNames.MakeWriter}({plan.Limits.MaximumUtf8Bytes}, {plan.Limits.MaximumStringCodeUnits});");
        writer.WriteLine($"if (!writer.static({JavaScriptLiteralWriter.WriteString(MirTsonCanonicalText.BuildDocumentPrefix(plan))})) {{");
        writer.Indent();
        EmitJavaScriptTsonFailure(writer, resultToken, errorToken, names);
        writer.Unindent();
        writer.WriteLine("}");
        for (int columnIndex = 0; columnIndex < tablePlan.Columns.Count; columnIndex++)
        {
            MirTsonTableColumnPlan column = tablePlan.Columns[columnIndex];
            writer.WriteLine($"if (length{columnIndex} === 0) {{");
            writer.Indent();
            writer.WriteLine($"if (!writer.static({JavaScriptLiteralWriter.WriteString(MirTsonCanonicalText.BuildTableColumnPrefix(plan, column))}) || !writer.static(\"[];\\n\")) {{");
            writer.Indent();
            EmitJavaScriptTsonFailure(writer, resultToken, errorToken, names);
            writer.Unindent();
            writer.WriteLine("}");
            writer.Unindent();
            writer.WriteLine("} else {");
            writer.Indent();
            writer.WriteLine($"if (!writer.static({JavaScriptLiteralWriter.WriteString(MirTsonCanonicalText.BuildTableColumnPrefix(plan, column))}) || !writer.static(\"[\\n\")) {{");
            writer.Indent();
            EmitJavaScriptTsonFailure(writer, resultToken, errorToken, names);
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine($"for (let index = 0; index < length{columnIndex}; index += 1) {{");
            writer.Indent();
            writer.WriteLine($"if (!Object.prototype.hasOwnProperty.call(cells{columnIndex}, index)) {{ {names.Panic}(); }}");
            writer.WriteLine($"const cell = cells{columnIndex}[index];");
            writer.WriteLine($"if (!writer.indent(2) || !{JavaScriptTsonValueWriter(planIndex, column.ElementPlan, recordIndexes, enumIndexes, arrayPlans, tsonNames)}(writer, cell, 2) || !writer.static(\",\\n\")) {{");
            writer.Indent();
            EmitJavaScriptTsonFailure(writer, resultToken, errorToken, names);
            writer.Unindent();
            writer.WriteLine("}");
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine("if (!writer.indent(1) || !writer.static(\"];\\n\")) {");
            writer.Indent();
            EmitJavaScriptTsonFailure(writer, resultToken, errorToken, names);
            writer.Unindent();
            writer.WriteLine("}");
            writer.Unindent();
            writer.WriteLine("}");
        }
        writer.WriteLine($"if (!writer.static({JavaScriptLiteralWriter.WriteString(MirTsonCanonicalText.BuildTableDocumentSuffix(plan))})) {{");
        writer.Indent();
        EmitJavaScriptTsonFailure(writer, resultToken, errorToken, names);
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"return {names.MakeValue}({resultToken}, \"ok\", [writer.finish()]);");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitJavaScriptTsonFailure(
        JavaScriptTextWriter writer,
        string resultToken,
        string errorToken,
        GeneratedNames names)
    {
        writer.WriteLine("const tag = writer.error() === \"invalid\" ? \"InvalidUnicode\" : \"OutputLimitExceeded\";");
        writer.WriteLine($"const error = {names.MakeValue}({errorToken}, tag, []);");
        writer.WriteLine($"return {names.MakeValue}({resultToken}, \"err\", [error]);");
    }

    private static string JavaScriptTsonValueWriter(
        int planIndex,
        MirTsonValuePlan valuePlan,
        IReadOnlyDictionary<MirRecordTypeId, int> recordIndexes,
        IReadOnlyDictionary<string, int> enumIndexes,
        IReadOnlyList<MirTsonArrayPlan>? arrayPlans = null,
        TsonGeneratedNames? tsonNames = null)
        => valuePlan switch
        {
            MirTsonBooleanPlan => tsonNames?.BooleanWriter ?? "writeBoolean",
            MirTsonNumberPlan => tsonNames?.NumberWriter ?? "writeNumber",
            MirTsonStringPlan => tsonNames?.StringWriter ?? "writeString",
            MirTsonRecordValuePlan record => tsonNames?.RecordWriter(planIndex, recordIndexes[record.RecordTypeId]) ?? $"writeP{planIndex}R{recordIndexes[record.RecordTypeId]}",
            MirTsonEnumValuePlan @enum => tsonNames?.EnumWriter(planIndex, enumIndexes[@enum.EnumName]) ?? $"writeP{planIndex}E{enumIndexes[@enum.EnumName]}",
            MirTsonArrayPlan array when arrayPlans is not null => tsonNames?.ArrayWriter(planIndex, JavaScriptTsonArrayPlanIndex(array, arrayPlans)) ?? JavaScriptTsonArrayWriterName(planIndex, array, arrayPlans),
            _ => throw new InvalidOperationException("Unsupported validated TSON value plan."),
        };

    private static void EmitJavaScriptTsonArrayWriter(
        JavaScriptTextWriter writer,
        MirTsonEncodingPlan plan,
        int planIndex,
        MirTsonArrayPlan arrayPlan,
        IReadOnlyDictionary<MirRecordTypeId, int> recordIndexes,
        IReadOnlyDictionary<string, int> enumIndexes,
        IReadOnlyList<MirTsonArrayPlan> arrayPlans,
        GeneratedNames names,
        TsonGeneratedNames tsonNames)
    {
        writer.WriteLine($"function {tsonNames.ArrayWriter(planIndex, JavaScriptTsonArrayPlanIndex(arrayPlan, arrayPlans))}(writer, value, indentation) {{");
        writer.Indent();
        writer.WriteLine("const array = value;");
        writer.WriteLine($"if (!Array.isArray(array)) {{ {names.Panic}(); }}");
        writer.WriteLine("const length = array.length;");
        writer.WriteLine($"if (length > {plan.Limits.MaximumArrayLength}) return writer.outputLimit();");
        writer.WriteLine("if (length === 0) return writer.static(\"[]\");");
        writer.WriteLine("if (!writer.static(\"[\\n\")) return false;");
        writer.WriteLine("for (let index = 0; index < length; index += 1) {");
        writer.Indent();
        writer.WriteLine("if (!Object.prototype.hasOwnProperty.call(array, index)) { " + names.Panic + "(); }");
        writer.WriteLine("const element = array[index];");
        EmitJavaScriptTsonArrayElementValidation(writer, arrayPlan.ElementPlan, "element", names);
        writer.WriteLine("if (!writer.indent(indentation + 1)) return false;");
        writer.WriteLine($"if (!{JavaScriptTsonValueWriter(planIndex, arrayPlan.ElementPlan, recordIndexes, enumIndexes, arrayPlans, tsonNames)}(writer, element, indentation + 1)) return false;");
        writer.WriteLine("if (!writer.static(\",\\n\")) return false;");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("if (!writer.indent(indentation)) return false;");
        writer.WriteLine("return writer.static(\"]\");");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void EmitJavaScriptTsonArrayElementValidation(
        JavaScriptTextWriter writer,
        MirTsonValuePlan valuePlan,
        string value,
        GeneratedNames names)
    {
        string? condition = valuePlan switch
        {
            MirTsonBooleanPlan => $"typeof {value} !== \"boolean\"",
            MirTsonNumberPlan => $"typeof {value} !== \"number\"",
            MirTsonStringPlan => $"typeof {value} !== \"string\"",
            MirTsonArrayPlan => $"!Array.isArray({value})",
            _ => null,
        };
        if (condition is not null)
        {
            writer.WriteLine($"if ({condition}) {{ {names.Panic}(); }}");
        }
    }

    private static string JavaScriptTsonArrayWriterName(int planIndex, MirTsonArrayPlan arrayPlan, IReadOnlyList<MirTsonArrayPlan> arrayPlans)
        => $"writeP{planIndex}A{JavaScriptTsonArrayPlanIndex(arrayPlan, arrayPlans)}";

    private static int JavaScriptTsonArrayPlanIndex(MirTsonArrayPlan arrayPlan, IReadOnlyList<MirTsonArrayPlan> arrayPlans)
    {
        for (int index = 0; index < arrayPlans.Count; index++)
        {
            if (arrayPlans[index].Equals(arrayPlan))
            {
                return index;
            }
        }

        throw new InvalidOperationException("Validated TSON array plan was not collected.");
    }

    private static IReadOnlyList<MirTsonArrayPlan> CollectTsonArrayPlans(MirTsonEncodingPlan plan)
    {
        var arrays = new List<MirTsonArrayPlan>();

        void Visit(MirTsonValuePlan valuePlan)
        {
            if (valuePlan is not MirTsonArrayPlan array || arrays.Contains(array))
            {
                return;
            }

            arrays.Add(array);
            Visit(array.ElementPlan);
        }

        Visit(plan.RootValuePlan);
        if (plan.TablePlan is not null)
        {
            foreach (MirTsonTableColumnPlan column in plan.TablePlan.Columns)
            {
                Visit(column.ElementPlan);
            }
        }
        foreach (MirTsonNominalPlan definition in plan.Definitions)
        {
            IEnumerable<MirTsonValuePlan> values = definition switch
            {
                MirTsonRecordPlan record => record.Fields.Select(field => field.ValuePlan),
                MirTsonEnumPlan @enum => @enum.Cases.SelectMany(@case => @case.Payloads.Select(payload => payload.ValuePlan)),
                _ => [],
            };
            foreach (MirTsonValuePlan value in values)
            {
                Visit(value);
            }
        }

        return arrays;
    }

    private static EmittedExpression EmitBinary(MirBinaryExpression binary, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names, bool flowEnabled)
    {
        EmittedExpression left = EmitExpression(binary.Left, function, catalog, results, names, flowEnabled);
        EmittedExpression right = EmitExpression(binary.Right, function, catalog, results, names, flowEnabled);
        if (binary.Operator is "&&" or "||")
        {
            return EmitLogicalBinary(binary.Operator, left, right, names);
        }

        return CombineOrdered(
            [left, right],
            names,
            values => $"({values[0]} {MapBinaryOperator(binary.Operator)} {values[1]})");
    }

    private static EmittedExpression EmitLogicalBinary(
        string binaryOperator,
        EmittedExpression left,
        EmittedExpression right,
        GeneratedNames names)
    {
        if (right.Prelude.Count == 0)
        {
            return new EmittedExpression(
                left.Prelude,
                $"({left.Value} {binaryOperator} {right.Value})");
        }

        var resultTemporary = names.NextTemporary("logical_result");
        string branchCondition = binaryOperator == "&&" ? left.Value : $"!({left.Value})";
        string shortCircuitValue = binaryOperator == "&&" ? "false" : "true";
        var prelude = new List<EmittedLine>(left.Prelude)
        {
            new($"let {resultTemporary};", 0),
            new($"if ({branchCondition}) {{", 0),
        };
        prelude.AddRange(right.Prelude.Select(line => line.OffsetBy(1)));
        prelude.Add(new EmittedLine($"{resultTemporary} = {right.Value};", 1));
        prelude.Add(new EmittedLine("} else {", 0));
        prelude.Add(new EmittedLine($"{resultTemporary} = {shortCircuitValue};", 1));
        prelude.Add(new EmittedLine("}", 0));
        return new EmittedExpression(prelude, resultTemporary);
    }

    private static EmittedExpression EmitAssignment(MirAssignmentExpression assignment, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names, bool flowEnabled)
    {
        EmittedExpression value = EmitExpression(assignment.Expression, function, catalog, results, names, flowEnabled);
        return new EmittedExpression(value.Prelude, $"({JavaScriptIdentifierEncoder.Encode(assignment.Name)} = {value.Value})");
    }

    private static EmittedExpression EmitCall(MirCallExpression call, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names, bool flowEnabled)
    {
        var emittedArguments = call.Arguments.Select(argument => EmitExpression(argument, function, catalog, results, names, flowEnabled)).ToList();
        return CombineOrdered(
            emittedArguments,
            names,
            values => $"{JavaScriptIdentifierEncoder.Encode(call.FunctionName)}({string.Join(", ", values)})");
    }

    private static EmittedExpression EmitArrayExpression(
        MirArrayExpression array,
        MirFunction function,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names,
        bool flowEnabled)
    {
        var elements = new List<EmittedExpression>(array.Elements.Count);
        for (int index = 0; index < array.Elements.Count; index++)
        {
            elements.Add(EmitExpression(array.Elements[index], function, catalog, results, names, flowEnabled));
        }

        return CombineOrdered(
            elements,
            names,
            values => "[" + string.Join(", ", values) + "]");
    }

    private static EmittedExpression EmitRecordConstruction(
        MirRecordConstructionExpression construction,
        MirFunction function,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names,
        bool flowEnabled)
    {
        MirRecordDefinition record = catalog.GetRecord(construction.RecordTypeId);
        var prelude = new List<EmittedLine>();
        var valuesByField = new Dictionary<MirRecordFieldId, string>();
        foreach (MirRecordFieldValue initializer in construction.Initializers)
        {
            EmittedExpression value = EmitExpression(initializer.Value, function, catalog, results, names, flowEnabled);
            prelude.AddRange(value.Prelude);
            var temporary = names.NextTemporary("record_init");
            prelude.Add(new EmittedLine($"const {temporary} = {value.Value};", 0));
            valuesByField.Add(initializer.FieldId, temporary);
        }

        string arguments = string.Join(", ", record.Fields.Select(field => valuesByField[field.Id]));
        return new EmittedExpression(prelude, $"{names.RecordConstructor(record)}({arguments})");
    }

    private static EmittedExpression EmitRecordFieldAccess(
        MirRecordFieldAccessExpression access,
        MirFunction function,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names,
        bool flowEnabled)
    {
        MirRecordDefinition record = catalog.GetRecord(access.RecordTypeId);
        MirRecordFieldDefinition field = record.Fields.Single(candidate => candidate.Id == access.FieldId);
        EmittedExpression receiver = EmitExpression(access.Receiver, function, catalog, results, names, flowEnabled);
        var temporary = names.NextTemporary("record_receiver");
        var prelude = new List<EmittedLine>(receiver.Prelude)
        {
            new($"const {temporary} = {receiver.Value};", 0),
            new($"{names.RecordValidator(record)}({temporary});", 0),
        };
        return new EmittedExpression(prelude, $"{temporary}[{names.RecordFieldSlot(field)}]");
    }

    private static EmittedExpression EmitTableColumnAccess(
        MirTableColumnAccessExpression access,
        MirFunction function,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names,
        bool flowEnabled)
    {
        MirTableDefinition table = catalog.GetTable(access.TableId);
        MirTableColumnDefinition column = catalog.GetTableColumn(access.ColumnId);
        EmittedExpression receiver = EmitExpression(access.Receiver, function, catalog, results, names, flowEnabled);
        var temporary = names.NextTemporary("table_receiver");
        var prelude = new List<EmittedLine>(receiver.Prelude)
        {
            new($"const {temporary} = {receiver.Value};", 0),
            new($"{names.TableValidator(table)}({temporary});", 0),
        };
        return new EmittedExpression(prelude, $"{temporary}[{names.TableColumnSlot(column)}]");
    }

    private static EmittedExpression EmitTableRowAccess(
        MirTableRowAccessExpression access,
        MirFunction function,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names,
        bool flowEnabled)
    {
        MirTableDefinition table = catalog.GetTable(access.TableId);
        MirResultType resultType = (MirResultType)access.Type;
        EmittedExpression receiver = EmitExpression(access.Receiver, function, catalog, results, names, flowEnabled);
        EmittedExpression index = EmitExpression(access.Index, function, catalog, results, names, flowEnabled);
        var receiverTemporary = names.NextTemporary("table_receiver");
        var indexTemporary = names.NextTemporary("table_index");
        var resultTemporary = names.NextTemporary("table_row");
        var prelude = new List<EmittedLine>(receiver.Prelude)
        {
            new($"const {receiverTemporary} = {receiver.Value};", 0),
        };
        prelude.AddRange(index.Prelude);
        prelude.Add(new EmittedLine($"const {indexTemporary} = {index.Value};", 0));
        prelude.Add(new EmittedLine($"{names.TableValidator(table)}({receiverTemporary});", 0));
        prelude.Add(new EmittedLine($"const {resultTemporary} = {receiverTemporary}[{names.TableRowReadSlot(table)}]({indexTemporary});", 0));
        prelude.Add(new EmittedLine($"{names.Validator(results.Get(resultType))}({resultTemporary});", 0));
        return new EmittedExpression(prelude, resultTemporary);
    }

    private static EmittedExpression EmitColumnElementAccess(
        MirColumnElementAccessExpression access,
        MirFunction function,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names,
        bool flowEnabled)
    {
        MirResultType resultType = (MirResultType)access.Type;
        EmittedExpression receiver = EmitExpression(access.Receiver, function, catalog, results, names, flowEnabled);
        EmittedExpression index = EmitExpression(access.Index, function, catalog, results, names, flowEnabled);
        var receiverTemporary = names.NextTemporary("column_receiver");
        var indexTemporary = names.NextTemporary("column_index");
        var resultTemporary = names.NextTemporary("column_element");
        var prelude = new List<EmittedLine>(receiver.Prelude)
        {
            new($"const {receiverTemporary} = {receiver.Value};", 0),
        };
        prelude.AddRange(index.Prelude);
        prelude.Add(new EmittedLine($"const {indexTemporary} = {index.Value};", 0));
        prelude.Add(new EmittedLine($"{names.ColumnValidator}({receiverTemporary});", 0));
        prelude.Add(new EmittedLine($"const {resultTemporary} = {receiverTemporary}[{names.ColumnReadSlot}]({indexTemporary});", 0));
        prelude.Add(new EmittedLine($"{names.Validator(results.Get(resultType))}({resultTemporary});", 0));
        return new EmittedExpression(prelude, resultTemporary);
    }

    private static EmittedExpression EmitTableRowFieldAccess(
        MirTableRowFieldAccessExpression access,
        MirFunction function,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names,
        bool flowEnabled)
    {
        MirTableDefinition table = catalog.GetTableByRowType(access.RowTypeId);
        MirTableColumnDefinition column = table.Columns.Single(candidate => candidate.Id.Value + ".f" == access.FieldId);
        MirResultType resultType = new(access.Type, new MirNamedType("TableBoundsError"));
        EmittedExpression receiver = EmitExpression(access.Receiver, function, catalog, results, names, flowEnabled);
        var rowTemporary = names.NextTemporary("table_row");
        var tableTemporary = names.NextTemporary("row_table");
        var resultTemporary = names.NextTemporary("row_field");
        var prelude = new List<EmittedLine>(receiver.Prelude)
        {
            new($"const {rowTemporary} = {receiver.Value};", 0),
            new($"{names.TableRowValidator(table)}({rowTemporary});", 0),
            new($"const {tableTemporary} = {rowTemporary}[{names.TableRowTableSlot}];", 0),
            new($"const {resultTemporary} = {tableTemporary}[{names.TableColumnSlot(column)}][{names.ColumnReadSlot}]({rowTemporary}[{names.TableRowIndexSlot}]);", 0),
            new($"{names.Validator(results.Get(resultType))}({resultTemporary});", 0),
            new($"if ({resultTemporary}.$tag !== \"ok\") {{ {names.Panic}(); }}", 0),
        };
        return new EmittedExpression(prelude, $"{resultTemporary}.$payload[0]");
    }

    private static EmittedExpression EmitRecordWith(
        MirRecordWithExpression withExpression,
        MirFunction function,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names,
        bool flowEnabled)
    {
        MirRecordDefinition record = catalog.GetRecord(withExpression.RecordTypeId);
        EmittedExpression source = EmitExpression(withExpression.Source, function, catalog, results, names, flowEnabled);
        var sourceTemporary = names.NextTemporary("record_source");
        var prelude = new List<EmittedLine>(source.Prelude)
        {
            new($"const {sourceTemporary} = {source.Value};", 0),
            new($"{names.RecordValidator(record)}({sourceTemporary});", 0),
        };
        var replacementsByField = new Dictionary<MirRecordFieldId, string>();
        foreach (MirRecordFieldValue replacement in withExpression.Replacements)
        {
            EmittedExpression value = EmitExpression(replacement.Value, function, catalog, results, names, flowEnabled);
            prelude.AddRange(value.Prelude);
            var temporary = names.NextTemporary("record_replacement");
            prelude.Add(new EmittedLine($"const {temporary} = {value.Value};", 0));
            replacementsByField.Add(replacement.FieldId, temporary);
        }

        string arguments = string.Join(", ", record.Fields.Select(field =>
            replacementsByField.TryGetValue(field.Id, out string? replacement)
                ? replacement
                : $"{sourceTemporary}[{names.RecordFieldSlot(field)}]"));
        return new EmittedExpression(prelude, $"{names.RecordConstructor(record)}({arguments})");
    }

    private static EmittedExpression CombineOrdered(
        IReadOnlyList<EmittedExpression> expressions,
        GeneratedNames names,
        Func<IReadOnlyList<string>, string> buildValue)
    {
        int lastPreludeIndex = -1;
        for (int index = 0; index < expressions.Count; index += 1)
        {
            if (expressions[index].Prelude.Count > 0)
            {
                lastPreludeIndex = index;
            }
        }

        var prelude = new List<EmittedLine>();
        var values = new List<string>(expressions.Count);
        for (int index = 0; index < expressions.Count; index += 1)
        {
            EmittedExpression expression = expressions[index];
            prelude.AddRange(expression.Prelude);
            if (index < lastPreludeIndex)
            {
                var temporary = names.NextTemporary("ordered");
                prelude.Add(new EmittedLine($"const {temporary} = {expression.Value};", 0));
                values.Add(temporary);
            }
            else
            {
                values.Add(expression.Value);
            }
        }

        return new EmittedExpression(prelude, buildValue(values));
    }

    private static EmittedExpression EmitEnumValueExpression(MirEnumValueExpression value, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names, bool flowEnabled)
    {
        EnumInfo enumInfo = catalog.GetEnum(value.EnumName);
        var payloads = value.Arguments.Select(argument => EmitExpression(argument, function, catalog, results, names, flowEnabled)).ToList();
        return CombineOrdered(
            payloads,
            names,
            values => $"{names.MakeValue}({names.TypeToken(enumInfo)}, {JavaScriptLiteralWriter.WriteString(value.CaseName)}, [{string.Join(", ", values)}])");
    }

    private static EmittedExpression EmitEnumMatchExpression(MirMatchExpression match, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names, bool flowEnabled)
    {
        if (ContainsControlFlow(match))
        {
            return EmitStructuredEnumMatch(match, function, catalog, results, names, flowEnabled);
        }

        EnumInfo enumInfo = catalog.GetEnum(match.Scrutinee.Type.Identifier);
        var scrutinee = names.NextMatchScrutinee();
        var parts = new List<string>
        {
            "(() => {",
            $"const {scrutinee} = {EmitExpression(match.Scrutinee, function, catalog, results, names, flowEnabled).Value};",
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

            parts.Add($"return {EmitExpression(arm.Expression, function, catalog, results, names, flowEnabled).Value};");
            parts.Add("}");
        }

        parts.Add("default:");
        parts.Add($"return {names.Panic}();");
        parts.Add("}");
        parts.Add("})()");
        return EmittedExpression.ValueOnly(string.Join(" ", parts));
    }

    private static EmittedExpression EmitResultConstruction(MirExpression payload, MirResultType type, string tag, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names, bool flowEnabled)
    {
        EmittedExpression emittedPayload = EmitExpression(payload, function, catalog, results, names, flowEnabled);
        return new EmittedExpression(emittedPayload.Prelude, $"{names.MakeValue}({names.TypeToken(results.Get(type))}, \"{tag}\", [{emittedPayload.Value}])");
    }

    private static EmittedExpression EmitPropagation(MirPropagateExpression propagation, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names, bool flowEnabled)
    {
        if (propagation.Operand.Type is not MirResultType operandResult)
        {
            throw new InvalidOperationException("Validated JavaScript emission received an unsupported Result propagation target.");
        }

        EmittedExpression operand = EmitExpression(propagation.Operand, function, catalog, results, names, flowEnabled);
        var temporary = names.NextTemporary("propagate");
        var prelude = new List<EmittedLine>(operand.Prelude)
        {
            new($"const {temporary} = {operand.Value};", 0),
            new($"{names.Validator(results.Get(operandResult))}({temporary});", 0),
            new($"if ({temporary}.$tag === \"err\") {{", 0),
            new(EmitPropagationReturn(propagation.Target, function, results, names, temporary, flowEnabled), 1),
            new("}", 0),
        };
        return new EmittedExpression(prelude, $"{temporary}.$payload[0]");
    }

    private static string EmitPropagationReturn(MirPropagationTarget target, MirFunction function, ResultCatalog results, GeneratedNames names, string temporary, bool flowEnabled)
    {
        if (flowEnabled)
        {
            return target switch
            {
                MirPropagationTarget.FunctionReturn => $"return {names.FlowToFunction}({temporary}.$payload[0]);",
                MirPropagationTarget.LexicalExcept lexical => $"return {names.FlowToHandler}({lexical.HandlerId.Value}, {temporary}.$payload[0]);",
                _ => throw new InvalidOperationException("Validated JavaScript emission received an unknown propagation target."),
            };
        }

        if (target is not MirPropagationTarget.FunctionReturn || function.ReturnType is not MirResultType functionResult)
        {
            throw new InvalidOperationException("Validated JavaScript emission received a lexical propagation outside a flow function.");
        }

        return $"return {names.MakeValue}({names.TypeToken(results.Get(functionResult))}, \"err\", [{temporary}.$payload[0]]);";
    }

    private static EmittedExpression EmitTryExcept(MirTryExpression tryExpression, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names, bool flowEnabled)
    {
        if (!flowEnabled)
        {
            throw new InvalidOperationException("Validated JavaScript emission received a try/except expression outside a flow function.");
        }

        var protectedFlow = names.NextTemporary("try_protected");
        var handlerFlow = names.NextTemporary("try_handler");
        var value = names.NextTemporary("try_value");
        string error = JavaScriptIdentifierEncoder.Encode(tryExpression.HandlerBinding.Name);
        var prelude = new List<EmittedLine>
        {
            new($"const {protectedFlow} = (() => {{", 0),
        };
        prelude.AddRange(EmitValueBlock(tryExpression.Protected, function, catalog, results, names).Select(line => line.OffsetBy(1)));
        prelude.Add(new EmittedLine("})();", 0));
        prelude.Add(new EmittedLine($"{names.ValidateFlow}({protectedFlow});", 0));
        prelude.Add(new EmittedLine($"let {value};", 0));
        prelude.Add(new EmittedLine($"if ({protectedFlow}.$kind === \"handler\" && {protectedFlow}.$handler === {tryExpression.HandlerId.Value}) {{", 0));
        prelude.Add(new EmittedLine($"const {error} = {protectedFlow}.$error;", 1));
        prelude.Add(new EmittedLine($"const {handlerFlow} = (() => {{", 1));
        prelude.AddRange(EmitValueBlock(tryExpression.Handler, function, catalog, results, names).Select(line => line.OffsetBy(2)));
        prelude.Add(new EmittedLine("})();", 1));
        prelude.Add(new EmittedLine($"{names.ValidateFlow}({handlerFlow});", 1));
        prelude.Add(new EmittedLine($"if ({handlerFlow}.$kind !== \"value\") {{", 1));
        prelude.Add(new EmittedLine($"return {handlerFlow};", 2));
        prelude.Add(new EmittedLine("}", 1));
        prelude.Add(new EmittedLine($"{value} = {handlerFlow}.$value;", 1));
        prelude.Add(new EmittedLine($"}} else if ({protectedFlow}.$kind === \"value\") {{", 0));
        prelude.Add(new EmittedLine($"{value} = {protectedFlow}.$value;", 1));
        prelude.Add(new EmittedLine("} else {", 0));
        prelude.Add(new EmittedLine($"return {protectedFlow};", 1));
        prelude.Add(new EmittedLine("}", 0));
        return new EmittedExpression(prelude, value);
    }

    private static IReadOnlyList<EmittedLine> EmitValueBlock(MirValueBlock block, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names)
    {
        var lines = new List<EmittedLine>();
        foreach (MirStatement statement in block.PrefixStatements)
        {
            switch (statement)
            {
                case MirVariableDeclarationStatement declaration:
                    EmittedExpression initializer = EmitExpression(declaration.Initializer, function, catalog, results, names, flowEnabled: true);
                    lines.AddRange(initializer.Prelude);
                    string declarationKeyword = declaration.Local.IsReadOnly ? "const" : "let";
                    lines.Add(new EmittedLine($"{declarationKeyword} {JavaScriptIdentifierEncoder.Encode(declaration.Local.Name)} = {initializer.Value};", 0));
                    break;
                case MirExpressionStatement expression:
                    EmittedExpression emitted = EmitExpression(expression.Expression, function, catalog, results, names, flowEnabled: true);
                    lines.AddRange(emitted.Prelude);
                    lines.Add(new EmittedLine($"{emitted.Value};", 0));
                    break;
                default:
                    throw new InvalidOperationException("Validated JavaScript emission received an unsupported try value block statement.");
            }
        }

        EmittedExpression value = EmitExpression(block.ValueExpression, function, catalog, results, names, flowEnabled: true);
        lines.AddRange(value.Prelude);
        lines.Add(new EmittedLine($"return {names.FlowValue}({value.Value});", 0));
        return lines;
    }

    private static EmittedExpression EmitUnwrap(MirUnwrapExpression unwrap, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names, bool flowEnabled)
    {
        MirResultType resultType = unwrap.ResultType;
        EmittedExpression operand = EmitExpression(unwrap.Operand, function, catalog, results, names, flowEnabled);
        var temporary = names.NextTemporary("unwrap");
        var prelude = new List<EmittedLine>(operand.Prelude)
        {
            new($"const {temporary} = {operand.Value};", 0),
            new($"{names.Validator(results.Get(resultType))}({temporary});", 0),
            new($"if ({temporary}.$tag === \"err\") {{", 0),
            new($"{names.UnwrapPanic}({temporary}.$payload[0]);", 1),
            new("}", 0),
        };
        return new EmittedExpression(prelude, $"{temporary}.$payload[0]");
    }

    private static EmittedExpression EmitResultMatchExpression(MirResultMatchExpression match, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names, bool flowEnabled)
    {
        MirResultType resultType = (MirResultType)match.Scrutinee.Type;
        EmittedExpression scrutinee = EmitExpression(match.Scrutinee, function, catalog, results, names, flowEnabled);
        EmittedExpression ok = EmitExpression(match.OkExpression, function, catalog, results, names, flowEnabled);
        EmittedExpression err = EmitExpression(match.ErrExpression, function, catalog, results, names, flowEnabled);
        var scrutineeTemporary = names.NextTemporary("result_match");
        var valueTemporary = names.NextTemporary("result_value");
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

    private static EmittedExpression EmitIfExpression(MirIfExpression conditional, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names, bool flowEnabled)
    {
        EmittedExpression condition = EmitExpression(conditional.Condition, function, catalog, results, names, flowEnabled);
        EmittedExpression thenExpression = EmitExpression(conditional.ThenExpression, function, catalog, results, names, flowEnabled);
        EmittedExpression elseExpression = EmitExpression(conditional.ElseExpression, function, catalog, results, names, flowEnabled);
        if (thenExpression.Prelude.Count == 0 && elseExpression.Prelude.Count == 0)
        {
            return new EmittedExpression(condition.Prelude, $"({condition.Value} ? {thenExpression.Value} : {elseExpression.Value})");
        }

        var valueTemporary = names.NextTemporary("if_value");
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

    private static EmittedExpression EmitStructuredEnumMatch(MirMatchExpression match, MirFunction function, EnumCatalog catalog, ResultCatalog results, GeneratedNames names, bool flowEnabled)
    {
        EnumInfo enumInfo = catalog.GetEnum(match.Scrutinee.Type.Identifier);
        EmittedExpression scrutinee = EmitExpression(match.Scrutinee, function, catalog, results, names, flowEnabled);
        var scrutineeTemporary = names.NextTemporary("match");
        var valueTemporary = names.NextTemporary("match_value");
        var prelude = new List<EmittedLine>(scrutinee.Prelude)
        {
            new($"const {scrutineeTemporary} = {scrutinee.Value};", 0),
            new($"{names.Validator(enumInfo)}({scrutineeTemporary});", 0),
            new($"let {valueTemporary};", 0),
            new($"switch ({scrutineeTemporary}.$tag) {{", 0),
        };
        foreach (MirMatchArm arm in match.Arms)
        {
            EmittedExpression armExpression = EmitExpression(arm.Expression, function, catalog, results, names, flowEnabled);
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
            MirPropagateExpression or MirUnwrapExpression or MirResultMatchExpression or MirTryExpression => true,
            MirUnaryExpression unary => ContainsControlFlow(unary.Operand),
            MirRecordConstructionExpression or MirRecordFieldAccessExpression or MirRecordWithExpression => true,
            MirTableColumnAccessExpression or MirTableRowAccessExpression or MirColumnElementAccessExpression or MirTableRowFieldAccessExpression => true,
            MirBinaryExpression binary => ContainsControlFlow(binary.Left) || ContainsControlFlow(binary.Right),
            MirCallExpression call => call.Arguments.Any(ContainsControlFlow),
            MirAssignmentExpression assignment => ContainsControlFlow(assignment.Expression),
            MirEnumValueExpression value => value.Arguments.Any(ContainsControlFlow),
            MirMatchExpression match => ContainsControlFlow(match.Scrutinee) || match.Arms.Any(arm => ContainsControlFlow(arm.Expression)),
            MirIfExpression conditional => ContainsControlFlow(conditional.Condition) || ContainsControlFlow(conditional.ThenExpression) || ContainsControlFlow(conditional.ElseExpression),
            MirOkExpression ok => ContainsControlFlow(ok.Payload),
            MirErrExpression err => ContainsControlFlow(err.Payload),
            MirTsonEncodeExpression encode => ContainsControlFlow(encode.Operand),
            _ => false,
        };
    }

    private static bool ProgramUsesTryExcept(MirProgram program)
        => program.Functions.Any(FunctionUsesTryExcept);

    private static bool FunctionUsesTryExcept(MirFunction function)
        => function.Body.Any(StatementUsesTryExcept);

    private static bool StatementUsesTryExcept(MirStatement statement)
        => statement switch
        {
            MirVariableDeclarationStatement declaration => ExpressionUsesTryExcept(declaration.Initializer),
            MirExpressionStatement expression => ExpressionUsesTryExcept(expression.Expression),
            MirReturnStatement { Expression: not null } returned => ExpressionUsesTryExcept(returned.Expression),
            MirIfStatement conditional => ExpressionUsesTryExcept(conditional.Condition) || conditional.ThenStatements.Any(StatementUsesTryExcept) || (conditional.ElseStatements?.Any(StatementUsesTryExcept) ?? false),
            _ => false,
        };

    private static bool ExpressionUsesTryExcept(MirExpression expression)
        => expression switch
        {
            MirTryExpression => true,
            MirBinaryExpression binary => ExpressionUsesTryExcept(binary.Left) || ExpressionUsesTryExcept(binary.Right),
            MirCallExpression call => call.Arguments.Any(ExpressionUsesTryExcept),
            MirAssignmentExpression assignment => ExpressionUsesTryExcept(assignment.Expression),
            MirArrayExpression array => array.Elements.Any(ExpressionUsesTryExcept),
            MirRecordConstructionExpression construction => construction.Initializers.Any(initializer => ExpressionUsesTryExcept(initializer.Value)),
            MirRecordFieldAccessExpression access => ExpressionUsesTryExcept(access.Receiver),
            MirTableColumnAccessExpression access => ExpressionUsesTryExcept(access.Receiver),
            MirTableRowAccessExpression access => ExpressionUsesTryExcept(access.Receiver) || ExpressionUsesTryExcept(access.Index),
            MirColumnElementAccessExpression access => ExpressionUsesTryExcept(access.Receiver) || ExpressionUsesTryExcept(access.Index),
            MirTableRowFieldAccessExpression access => ExpressionUsesTryExcept(access.Receiver),
            MirRecordWithExpression withExpression => ExpressionUsesTryExcept(withExpression.Source) || withExpression.Replacements.Any(replacement => ExpressionUsesTryExcept(replacement.Value)),
            MirEnumValueExpression value => value.Arguments.Any(ExpressionUsesTryExcept),
            MirMatchExpression match => ExpressionUsesTryExcept(match.Scrutinee) || match.Arms.Any(arm => ExpressionUsesTryExcept(arm.Expression)),
            MirResultMatchExpression match => ExpressionUsesTryExcept(match.Scrutinee) || ExpressionUsesTryExcept(match.OkExpression) || ExpressionUsesTryExcept(match.ErrExpression),
            MirIfExpression conditional => ExpressionUsesTryExcept(conditional.Condition) || ExpressionUsesTryExcept(conditional.ThenExpression) || ExpressionUsesTryExcept(conditional.ElseExpression),
            MirOkExpression ok => ExpressionUsesTryExcept(ok.Payload),
            MirErrExpression err => ExpressionUsesTryExcept(err.Payload),
            MirPropagateExpression propagation => ExpressionUsesTryExcept(propagation.Operand),
            MirUnaryExpression unary => ExpressionUsesTryExcept(unary.Operand),
            MirUnwrapExpression unwrap => ExpressionUsesTryExcept(unwrap.Operand),
            MirTsonEncodeExpression encode => ExpressionUsesTryExcept(encode.Operand),
            _ => false,
        };

    private static bool ProgramUsesUnwrap(MirProgram program)
        => program.Functions.Any(function => function.Body.Any(StatementUsesUnwrap));

    private static bool StatementUsesUnwrap(MirStatement statement)
        => statement switch
        {
            MirVariableDeclarationStatement declaration => ExpressionUsesUnwrap(declaration.Initializer),
            MirExpressionStatement expression => ExpressionUsesUnwrap(expression.Expression),
            MirReturnStatement { Expression: not null } returnStatement => ExpressionUsesUnwrap(returnStatement.Expression),
            MirIfStatement conditional => ExpressionUsesUnwrap(conditional.Condition) || conditional.ThenStatements.Any(StatementUsesUnwrap) || (conditional.ElseStatements?.Any(StatementUsesUnwrap) ?? false),
            _ => false,
        };

    private static bool ExpressionUsesUnwrap(MirExpression expression)
        => expression switch
        {
            MirUnwrapExpression => true,
            MirBinaryExpression binary => ExpressionUsesUnwrap(binary.Left) || ExpressionUsesUnwrap(binary.Right),
            MirCallExpression call => call.Arguments.Any(ExpressionUsesUnwrap),
            MirAssignmentExpression assignment => ExpressionUsesUnwrap(assignment.Expression),
            MirArrayExpression array => array.Elements.Any(ExpressionUsesUnwrap),
            MirRecordConstructionExpression construction => construction.Initializers.Any(initializer => ExpressionUsesUnwrap(initializer.Value)),
            MirRecordFieldAccessExpression access => ExpressionUsesUnwrap(access.Receiver),
            MirTableColumnAccessExpression access => ExpressionUsesUnwrap(access.Receiver),
            MirTableRowAccessExpression access => ExpressionUsesUnwrap(access.Receiver) || ExpressionUsesUnwrap(access.Index),
            MirColumnElementAccessExpression access => ExpressionUsesUnwrap(access.Receiver) || ExpressionUsesUnwrap(access.Index),
            MirTableRowFieldAccessExpression access => ExpressionUsesUnwrap(access.Receiver),
            MirRecordWithExpression withExpression => ExpressionUsesUnwrap(withExpression.Source) || withExpression.Replacements.Any(replacement => ExpressionUsesUnwrap(replacement.Value)),
            MirEnumValueExpression value => value.Arguments.Any(ExpressionUsesUnwrap),
            MirMatchExpression match => ExpressionUsesUnwrap(match.Scrutinee) || match.Arms.Any(arm => ExpressionUsesUnwrap(arm.Expression)),
            MirResultMatchExpression match => ExpressionUsesUnwrap(match.Scrutinee) || ExpressionUsesUnwrap(match.OkExpression) || ExpressionUsesUnwrap(match.ErrExpression),
            MirIfExpression conditional => ExpressionUsesUnwrap(conditional.Condition) || ExpressionUsesUnwrap(conditional.ThenExpression) || ExpressionUsesUnwrap(conditional.ElseExpression),
            MirOkExpression ok => ExpressionUsesUnwrap(ok.Payload),
            MirErrExpression err => ExpressionUsesUnwrap(err.Payload),
            MirPropagateExpression propagation => ExpressionUsesUnwrap(propagation.Operand),
            MirUnaryExpression unary => ExpressionUsesUnwrap(unary.Operand),
            MirTryExpression tryExpression => ValueBlockUsesUnwrap(tryExpression.Protected) || ValueBlockUsesUnwrap(tryExpression.Handler),
            MirTsonEncodeExpression encode => ExpressionUsesUnwrap(encode.Operand),
            _ => false,
        };

    private static bool ValueBlockUsesUnwrap(MirValueBlock block)
        => block.PrefixStatements.Any(StatementUsesUnwrap) || ExpressionUsesUnwrap(block.ValueExpression);

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
    }

    private sealed class EnumCatalog
    {
        private readonly Dictionary<string, EnumInfo> byName = new(StringComparer.Ordinal);
        private readonly Dictionary<MirRecordTypeId, MirRecordDefinition> recordsById = [];
        private readonly Dictionary<MirTableId, MirTableDefinition> tablesById = [];
        private readonly Dictionary<MirTableColumnId, MirTableColumnDefinition> columnsById = [];
        private readonly Dictionary<string, MirTableDefinition> tablesByRowType = new(StringComparer.Ordinal);

        public EnumCatalog(IReadOnlyList<MirEnum> enums, IReadOnlyList<MirRecordDefinition> records, IReadOnlyList<MirTableDefinition> tables, List<JavaScriptDiagnostic> diagnostics)
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
            Records = records;
            foreach (MirRecordDefinition record in records)
            {
                recordsById.TryAdd(record.Id, record);
            }

            Tables = tables;
            foreach (MirTableDefinition table in tables)
            {
                tablesById.TryAdd(table.Id, table);
                tablesByRowType.TryAdd(table.RowTypeId, table);
                foreach (MirTableColumnDefinition column in table.Columns)
                {
                    columnsById.TryAdd(column.Id, column);
                }
            }
        }

        public IReadOnlyList<EnumInfo> Enums { get; }

        public IReadOnlyList<MirRecordDefinition> Records { get; }

        public IReadOnlyList<MirTableDefinition> Tables { get; }

        public bool ContainsEnum(string name) => byName.ContainsKey(name);

        public bool TryGetEnum(string name, out EnumInfo enumInfo) => byName.TryGetValue(name, out enumInfo!);

        public EnumInfo GetEnum(string name) => byName[name];

        public bool ContainsRecord(MirRecordTypeId id) => recordsById.ContainsKey(id);

        public bool TryGetRecord(MirRecordTypeId id, out MirRecordDefinition definition) => recordsById.TryGetValue(id, out definition!);

        public MirRecordDefinition GetRecord(MirRecordTypeId id) => recordsById[id];

        public bool ContainsTable(MirTableId id) => tablesById.ContainsKey(id);

        public bool ContainsRow(string rowTypeId) => tablesByRowType.ContainsKey(rowTypeId);

        public MirTableDefinition GetTable(MirTableId id) => tablesById[id];

        public MirTableDefinition GetTableByRowType(string rowTypeId) => tablesByRowType[rowTypeId];

        public MirTableColumnDefinition GetTableColumn(MirTableColumnId id) => columnsById[id];

        public void ValidateDefinitions(List<JavaScriptDiagnostic> diagnostics)
        {
            foreach (MirRecordDefinition record in Records)
            {
                foreach (MirRecordFieldDefinition field in record.Fields)
                {
                    ValidateValueType(field.Type, $"field '{record.Name}.{field.Name}'", this, diagnostics, allowVoid: false);
                }
            }

            foreach (MirTableDefinition table in Tables)
            {
                foreach (MirTableColumnDefinition column in table.Columns)
                {
                    ValidateValueType(column.ElementType, $"column '{table.Name}.{column.Name}'", this, diagnostics, allowVoid: false);
                }
            }

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
            foreach (MirRecordDefinition record in program.Records)
            {
                foreach (MirRecordFieldDefinition field in record.Fields)
                {
                    catalog.Add(field.Type);
                }
            }

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

            foreach (MirTableDefinition table in program.Tables)
            {
                foreach (MirTableColumnDefinition column in table.Columns)
                {
                    catalog.Add(column.ElementType);
                    catalog.Add(new MirResultType(column.ElementType, new MirNamedType("TableBoundsError")));
                    foreach (MirTableConstant constant in column.Constants)
                    {
                        catalog.Add(constant);
                    }
                }

                catalog.Add(new MirResultType(new MirTableRowType(table.RowTypeId, table.Name + ".Row"), new MirNamedType("TableBoundsError")));
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
                case MirAssignmentExpression assignment:
                    Add(assignment.Expression);
                    break;
                case MirUnaryExpression unary:
                    Add(unary.Operand);
                    break;
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
                case MirRecordConstructionExpression construction:
                    foreach (MirRecordFieldValue initializer in construction.Initializers)
                    {
                        Add(initializer.Value);
                    }
                    break;
                case MirRecordFieldAccessExpression access:
                    Add(access.Receiver);
                    break;
                case MirTableColumnAccessExpression access:
                    Add(access.Receiver);
                    break;
                case MirTableRowAccessExpression access:
                    Add(access.Receiver);
                    Add(access.Index);
                    break;
                case MirColumnElementAccessExpression access:
                    Add(access.Receiver);
                    Add(access.Index);
                    break;
                case MirTableRowFieldAccessExpression access:
                    Add(access.Receiver);
                    break;
                case MirRecordWithExpression withExpression:
                    Add(withExpression.Source);
                    foreach (MirRecordFieldValue replacement in withExpression.Replacements)
                    {
                        Add(replacement.Value);
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
                case MirUnwrapExpression unwrap:
                    Add(unwrap.Operand);
                    break;
                case MirTryExpression tryExpression:
                    Add(tryExpression.Protected);
                    Add(tryExpression.Handler);
                    break;
                case MirTsonEncodeExpression encode:
                    Add(encode.Operand);
                    break;
            }
        }

        private void Add(MirTableConstant constant)
        {
            Add(constant.Type);
            switch (constant)
            {
                case MirTableArrayConstant array:
                    foreach (MirTableConstant element in array.Elements)
                    {
                        Add(element);
                    }
                    break;
                case MirTableRecordConstant record:
                    foreach (MirTableRecordFieldConstant field in record.Fields)
                    {
                        Add(field.Value);
                    }
                    break;
                case MirTableEnumConstant value:
                    foreach (MirTableConstant payload in value.Payloads)
                    {
                        Add(payload);
                    }
                    break;
                case MirTableResultConstant result:
                    Add(result.Payload);
                    break;
            }
        }

        private void Add(MirValueBlock block)
        {
            foreach (MirStatement statement in block.PrefixStatements)
            {
                Add(statement);
            }
            Add(block.ValueExpression);
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

    private sealed class TsonGeneratedNames
    {
        private readonly JavaScriptEmissionProfile profile;
        private readonly ScopedSymbolicNameAllocator? symbolic;
        private readonly Dictionary<(int PlanIndex, int RecordIndex), string> recordWriters = [];
        private readonly Dictionary<(int PlanIndex, int EnumIndex), string> enumWriters = [];
        private readonly Dictionary<(int PlanIndex, int ArrayIndex), string> arrayWriters = [];
        private readonly Dictionary<int, string> encoders = [];

        private TsonGeneratedNames(JavaScriptEmissionProfile profile, ScopedSymbolicNameAllocator? symbolic)
        {
            this.profile = profile;
            this.symbolic = symbolic;

            if (profile == JavaScriptEmissionProfile.Symbolic)
            {
                BooleanWriter = symbolic!.Allocate(symbolic.RuntimeScope, JavaScriptSymbolicBindingRole.TsonBooleanWriter, "writeBoolean");
                NumberWriter = symbolic.Allocate(symbolic.RuntimeScope, JavaScriptSymbolicBindingRole.TsonNumberWriter, "writeNumber");
                StringWriter = symbolic.Allocate(symbolic.RuntimeScope, JavaScriptSymbolicBindingRole.TsonStringWriter, "writeString");
                MakeWriter = symbolic.Allocate(symbolic.RuntimeScope, JavaScriptSymbolicBindingRole.TsonWriterFactory, "makeWriter");
                WriterFail = symbolic.Allocate(symbolic.WriterScope, JavaScriptSymbolicBindingRole.TsonWriterFail, "fail");
                WriterAppend = symbolic.Allocate(symbolic.WriterScope, JavaScriptSymbolicBindingRole.TsonWriterAppend, "appendRaw");
                UnicodeEscape = symbolic.Allocate(symbolic.WriterScope, JavaScriptSymbolicBindingRole.TsonUnicodeEscape, "unicodeEscape");
                StringWriterValue = symbolic.Allocate(symbolic.WriterScope, JavaScriptSymbolicBindingRole.TsonStringWriter, "writeString");
                NumberWriterValue = symbolic.Allocate(symbolic.WriterScope, JavaScriptSymbolicBindingRole.TsonNumberWriter, "writeNumber");
            }
            else
            {
                BooleanWriter = "writeBoolean";
                NumberWriter = "writeNumber";
                StringWriter = "writeString";
                MakeWriter = "makeWriter";
                WriterFail = "fail";
                WriterAppend = "appendRaw";
                UnicodeEscape = "unicodeEscape";
                StringWriterValue = "writeString";
                NumberWriterValue = "writeNumber";
            }
        }

        public string BooleanWriter { get; }

        public string NumberWriter { get; }

        public string StringWriter { get; }

        public string MakeWriter { get; }

        public string WriterFail { get; }

        public string WriterAppend { get; }

        public string UnicodeEscape { get; }

        public string StringWriterValue { get; }

        public string NumberWriterValue { get; }

        public string RecordWriter(int planIndex, int recordIndex)
        {
            return GetOrAdd(recordWriters, (planIndex, recordIndex), JavaScriptSymbolicBindingRole.TsonRecordWriter, $"writeP{planIndex}R{recordIndex}");
        }

        public string EnumWriter(int planIndex, int enumIndex)
        {
            return GetOrAdd(enumWriters, (planIndex, enumIndex), JavaScriptSymbolicBindingRole.TsonEnumWriter, $"writeP{planIndex}E{enumIndex}");
        }

        public string ArrayWriter(int planIndex, int arrayIndex)
        {
            return GetOrAdd(arrayWriters, (planIndex, arrayIndex), JavaScriptSymbolicBindingRole.TsonArrayWriter, $"writeP{planIndex}A{arrayIndex}");
        }

        public string Encoder(int planIndex)
        {
            if (encoders.TryGetValue(planIndex, out string? existing))
            {
                return existing;
            }

            string name = profile == JavaScriptEmissionProfile.Symbolic
                ? symbolic!.Allocate(symbolic.RuntimeScope, JavaScriptSymbolicBindingRole.TsonEncoder, $"encode{planIndex}")
                : $"encode{planIndex}";
            encoders.Add(planIndex, name);
            return name;
        }

        public static TsonGeneratedNames Create(IReadOnlyList<MirTsonEncodingPlan> plans, GeneratedNames names)
        {
            if (names.Profile != JavaScriptEmissionProfile.Symbolic)
            {
                return new TsonGeneratedNames(names.Profile, symbolic: null);
            }

            var reserved = names.Document.Bindings
                .Select(binding => binding.AssignedName)
                .Where(static name => !string.IsNullOrEmpty(name))
                .Cast<string>();
            var symbolic = new ScopedSymbolicNameAllocator(reserved);
            var tsonNames = new TsonGeneratedNames(names.Profile, symbolic);
            for (int planIndex = 0; planIndex < plans.Count; planIndex += 1)
            {
                MirTsonEncodingPlan plan = plans[planIndex];
                foreach ((MirTsonRecordPlan record, int recordIndex) in plan.Definitions.OfType<MirTsonRecordPlan>().Select((item, index) => (item, index)))
                {
                    _ = tsonNames.RecordWriter(planIndex, recordIndex);
                }

                foreach ((MirTsonEnumPlan @enum, int enumIndex) in plan.Definitions.OfType<MirTsonEnumPlan>().Select((item, index) => (item, index)))
                {
                    _ = tsonNames.EnumWriter(planIndex, enumIndex);
                }

                IReadOnlyList<MirTsonArrayPlan> arrays = CollectTsonArrayPlans(plan);
                for (int arrayIndex = 0; arrayIndex < arrays.Count; arrayIndex += 1)
                {
                    _ = tsonNames.ArrayWriter(planIndex, arrayIndex);
                }

                _ = tsonNames.Encoder(planIndex);
            }

            symbolic.Validate();
            return tsonNames;
        }

        private string GetOrAdd<TKey>(Dictionary<TKey, string> namesByKey, TKey key, JavaScriptSymbolicBindingRole role, string diagnosticName)
            where TKey : notnull
        {
            if (namesByKey.TryGetValue(key, out string? existing))
            {
                return existing;
            }

            string name = profile == JavaScriptEmissionProfile.Symbolic
                ? symbolic!.Allocate(symbolic.RuntimeScope, role, diagnosticName)
                : diagnosticName;
            namesByKey.Add(key, name);
            return name;
        }
    }

    private sealed class ScopedSymbolicNameAllocator
    {
        private readonly JavaScriptEmissionDocument document = new();
        private readonly JavaScriptNameAllocator allocator;

        public ScopedSymbolicNameAllocator(IEnumerable<string> reservedNames)
        {
            allocator = new JavaScriptNameAllocator(document, document.ProgramScope, reservedNames, JavaScriptEmissionProfile.Symbolic);
            RuntimeScope = document.CreateScope(JavaScriptScopeKind.Block, document.ProgramScope);
            WriterScope = document.CreateScope(JavaScriptScopeKind.Block, RuntimeScope);
        }

        public JavaScriptScopeId RuntimeScope { get; }

        public JavaScriptScopeId WriterScope { get; }

        public string Allocate(JavaScriptScopeId scope, JavaScriptSymbolicBindingRole role, string diagnosticPurpose)
        {
            return allocator.Allocate(scope, JavaScriptBindingRole.RuntimeHelper, diagnosticPurpose, JavaScriptDeclarationKind.Function, symbolicRole: role).Name;
        }

        public void Validate()
        {
            document.Validate();
        }
    }

    private sealed class GeneratedNames
    {
        private readonly Dictionary<EnumInfo, JavaScriptBindingReference> typeTokens;
        private readonly Dictionary<EnumInfo, JavaScriptBindingReference> enumInstances;
        private readonly Dictionary<EnumInfo, JavaScriptBindingReference> validators;
        private readonly Dictionary<ResultInfo, JavaScriptBindingReference> resultTypeTokens;
        private readonly Dictionary<ResultInfo, JavaScriptBindingReference> resultValidators;
        private readonly Dictionary<MirRecordDefinition, JavaScriptBindingReference> recordTypeTokens;
        private readonly Dictionary<MirRecordDefinition, JavaScriptBindingReference> recordInstances;
        private readonly Dictionary<MirRecordDefinition, JavaScriptBindingReference> recordConstructors;
        private readonly Dictionary<MirRecordDefinition, JavaScriptBindingReference> recordValidators;
        private readonly Dictionary<MirRecordFieldDefinition, JavaScriptBindingReference> recordFieldSlots;
        private readonly Dictionary<MirTableDefinition, JavaScriptBindingReference> tableTypeTokens;
        private readonly Dictionary<MirTableDefinition, JavaScriptBindingReference> tableInstances;
        private readonly Dictionary<MirTableDefinition, JavaScriptBindingReference> tableRowTypeTokens;
        private readonly Dictionary<MirTableDefinition, JavaScriptBindingReference> tableValidators;
        private readonly Dictionary<MirTableDefinition, JavaScriptBindingReference> tableRowValidators;
        private readonly Dictionary<MirTableDefinition, JavaScriptBindingReference> tableCreates;
        private readonly Dictionary<MirTableDefinition, JavaScriptBindingReference> tableCreateRows;
        private readonly Dictionary<MirTableDefinition, JavaScriptBindingReference> tableSingletons;
        private readonly Dictionary<MirTableDefinition, JavaScriptBindingReference> tableRowReadSlots;
        private readonly Dictionary<MirTableColumnDefinition, JavaScriptBindingReference> tableColumnSlots;
        private readonly Dictionary<MirTableColumnDefinition, JavaScriptBindingReference> tableColumnTokens;
        private readonly Dictionary<MirTableColumnDefinition, JavaScriptBindingReference> tableStorages;
        private readonly Dictionary<MirTableColumnDefinition, JavaScriptBindingReference> tableColumnValues;
        private readonly Dictionary<MirFunction, JavaScriptScopeId> functionScopes;
        private readonly NameAllocator allocator;

        private GeneratedNames(
            NameAllocator allocator,
            JavaScriptBindingReference panic,
            JavaScriptBindingReference unwrapPanic,
            JavaScriptBindingReference makeValue,
            JavaScriptBindingReference flowToken,
            JavaScriptBindingReference flowValue,
            JavaScriptBindingReference flowToHandler,
            JavaScriptBindingReference flowToFunction,
            JavaScriptBindingReference validateFlow,
            JavaScriptBindingReference tsonRuntime,
            bool usesTsonTableEncoding,
            Dictionary<EnumInfo, JavaScriptBindingReference> typeTokens,
            Dictionary<EnumInfo, JavaScriptBindingReference> enumInstances,
            Dictionary<EnumInfo, JavaScriptBindingReference> validators,
            Dictionary<ResultInfo, JavaScriptBindingReference> resultTypeTokens,
            Dictionary<ResultInfo, JavaScriptBindingReference> resultValidators,
            Dictionary<MirRecordDefinition, JavaScriptBindingReference> recordTypeTokens,
            Dictionary<MirRecordDefinition, JavaScriptBindingReference> recordInstances,
            Dictionary<MirRecordDefinition, JavaScriptBindingReference> recordConstructors,
            Dictionary<MirRecordDefinition, JavaScriptBindingReference> recordValidators,
            Dictionary<MirRecordFieldDefinition, JavaScriptBindingReference> recordFieldSlots,
            JavaScriptBindingReference columnCarrierToken,
            JavaScriptBindingReference columnInstances,
            JavaScriptBindingReference columnReadSlot,
            JavaScriptBindingReference columnValuesSlot,
            JavaScriptBindingReference columnValidator,
            JavaScriptBindingReference tableRowTableSlot,
            JavaScriptBindingReference tableRowIndexSlot,
            Dictionary<MirTableDefinition, JavaScriptBindingReference> tableTypeTokens,
            Dictionary<MirTableDefinition, JavaScriptBindingReference> tableInstances,
            Dictionary<MirTableDefinition, JavaScriptBindingReference> tableRowTypeTokens,
            Dictionary<MirTableDefinition, JavaScriptBindingReference> tableValidators,
            Dictionary<MirTableDefinition, JavaScriptBindingReference> tableRowValidators,
            Dictionary<MirTableDefinition, JavaScriptBindingReference> tableCreates,
            Dictionary<MirTableDefinition, JavaScriptBindingReference> tableCreateRows,
            Dictionary<MirTableDefinition, JavaScriptBindingReference> tableSingletons,
            Dictionary<MirTableDefinition, JavaScriptBindingReference> tableRowReadSlots,
            Dictionary<MirTableColumnDefinition, JavaScriptBindingReference> tableColumnSlots,
            Dictionary<MirTableColumnDefinition, JavaScriptBindingReference> tableColumnTokens,
            Dictionary<MirTableColumnDefinition, JavaScriptBindingReference> tableStorages,
            Dictionary<MirTableColumnDefinition, JavaScriptBindingReference> tableColumnValues,
            Dictionary<MirFunction, JavaScriptScopeId> functionScopes,
            JavaScriptEmissionProfile profile)
        {
            this.allocator = allocator;
            Panic = panic;
            UnwrapPanic = unwrapPanic;
            MakeValue = makeValue;
            FlowToken = flowToken;
            FlowValue = flowValue;
            FlowToHandler = flowToHandler;
            FlowToFunction = flowToFunction;
            ValidateFlow = validateFlow;
            TsonRuntime = tsonRuntime;
            UsesTsonTableEncoding = usesTsonTableEncoding;
            this.typeTokens = typeTokens;
            this.enumInstances = enumInstances;
            this.validators = validators;
            this.resultTypeTokens = resultTypeTokens;
            this.resultValidators = resultValidators;
            this.recordTypeTokens = recordTypeTokens;
            this.recordInstances = recordInstances;
            this.recordConstructors = recordConstructors;
            this.recordValidators = recordValidators;
            this.recordFieldSlots = recordFieldSlots;
            ColumnCarrierToken = columnCarrierToken;
            ColumnInstances = columnInstances;
            ColumnReadSlot = columnReadSlot;
            ColumnValuesSlot = columnValuesSlot;
            ColumnValidator = columnValidator;
            TableRowTableSlot = tableRowTableSlot;
            TableRowIndexSlot = tableRowIndexSlot;
            this.tableTypeTokens = tableTypeTokens;
            this.tableInstances = tableInstances;
            this.tableRowTypeTokens = tableRowTypeTokens;
            this.tableValidators = tableValidators;
            this.tableRowValidators = tableRowValidators;
            this.tableCreates = tableCreates;
            this.tableCreateRows = tableCreateRows;
            this.tableSingletons = tableSingletons;
            this.tableRowReadSlots = tableRowReadSlots;
            this.tableColumnSlots = tableColumnSlots;
            this.tableColumnTokens = tableColumnTokens;
            this.tableStorages = tableStorages;
            this.tableColumnValues = tableColumnValues;
            this.functionScopes = functionScopes;
            Profile = profile;
        }

        public JavaScriptBindingReference Panic { get; }

        public JavaScriptBindingReference UnwrapPanic { get; }

        public JavaScriptBindingReference MakeValue { get; }

        public JavaScriptBindingReference FlowToken { get; }

        public JavaScriptBindingReference FlowValue { get; }

        public JavaScriptBindingReference FlowToHandler { get; }

        public JavaScriptBindingReference FlowToFunction { get; }

        public JavaScriptBindingReference ValidateFlow { get; }

        public JavaScriptBindingReference TsonRuntime { get; }

        public bool UsesTsonTableEncoding { get; }

        public JavaScriptEmissionProfile Profile { get; }

        public JavaScriptEmissionDocument Document => allocator.Document;

        public JavaScriptBindingReference ColumnCarrierToken { get; }

        public JavaScriptBindingReference ColumnInstances { get; }

        public JavaScriptBindingReference ColumnReadSlot { get; }

        public JavaScriptBindingReference ColumnValuesSlot { get; }

        public JavaScriptBindingReference ColumnValidator { get; }

        public JavaScriptBindingReference TableRowTableSlot { get; }

        public JavaScriptBindingReference TableRowIndexSlot { get; }

        public JavaScriptBindingReference TypeToken(EnumInfo enumInfo) => typeTokens[enumInfo];

        public JavaScriptBindingReference EnumInstances(EnumInfo enumInfo) => enumInstances[enumInfo];

        public JavaScriptBindingReference Validator(EnumInfo enumInfo) => validators[enumInfo];

        public JavaScriptBindingReference TypeToken(ResultInfo result) => resultTypeTokens[result];

        public JavaScriptBindingReference Validator(ResultInfo result) => resultValidators[result];

        public JavaScriptBindingReference RecordTypeToken(MirRecordDefinition record) => recordTypeTokens[record];

        public JavaScriptBindingReference RecordInstances(MirRecordDefinition record) => recordInstances[record];

        public JavaScriptBindingReference RecordConstructor(MirRecordDefinition record) => recordConstructors[record];

        public JavaScriptBindingReference RecordValidator(MirRecordDefinition record) => recordValidators[record];

        public JavaScriptBindingReference RecordFieldSlot(MirRecordFieldDefinition field) => recordFieldSlots[field];

        public JavaScriptBindingReference TableTypeToken(MirTableDefinition table) => tableTypeTokens[table];

        public JavaScriptBindingReference TableInstances(MirTableDefinition table) => tableInstances[table];

        public JavaScriptBindingReference TableRowTypeToken(MirTableDefinition table) => tableRowTypeTokens[table];

        public JavaScriptBindingReference TableValidator(MirTableDefinition table) => tableValidators[table];

        public JavaScriptBindingReference TableRowValidator(MirTableDefinition table) => tableRowValidators[table];

        public JavaScriptBindingReference TableCreate(MirTableDefinition table) => tableCreates[table];

        public JavaScriptBindingReference TableCreateRow(MirTableDefinition table) => tableCreateRows[table];

        public JavaScriptBindingReference TableSingleton(MirTableDefinition table) => tableSingletons[table];

        public JavaScriptBindingReference TableRowReadSlot(MirTableDefinition table) => tableRowReadSlots[table];

        public JavaScriptBindingReference TableColumnSlot(MirTableColumnDefinition column) => tableColumnSlots[column];

        public JavaScriptBindingReference TableColumnToken(MirTableColumnDefinition column) => tableColumnTokens[column];

        public JavaScriptBindingReference TableStorage(MirTableColumnDefinition column) => tableStorages[column];

        public JavaScriptBindingReference TableColumnValue(MirTableColumnDefinition column) => tableColumnValues[column];

        public string SymbolDescription(JavaScriptBindingReference binding, string diagnosticDescription)
        {
            return Profile == JavaScriptEmissionProfile.Symbolic
                ? binding.Name
                : diagnosticDescription;
        }

        public JavaScriptBindingReference NextMatchScrutinee() => allocator.Allocate(
            JavaScriptBindingRole.Temporary,
            JavaScriptSymbolicBindingRole.MatchTemporary,
            "match");

        public JavaScriptBindingReference NextTemporary(string purpose) => allocator.Allocate(
            JavaScriptBindingRole.Temporary,
            JavaScriptSymbolicBindingRole.Temporary,
            purpose);

        public JavaScriptScopeId EnterFunction(MirFunction function)
        {
            JavaScriptScopeId scope = functionScopes[function];
            allocator.EnterScope(scope);
            return scope;
        }

        public void LeaveFunction()
        {
            allocator.EnterScope(Document.ProgramScope);
        }

        public static GeneratedNames Create(MirProgram program, EnumCatalog catalog, ResultCatalog results, bool usesUnwrap, bool usesTryExcept, JavaScriptEmissionProfile profile)
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

            var allocator = new NameAllocator(occupied, profile);
            var functionScopes = new Dictionary<MirFunction, JavaScriptScopeId>();
            foreach (MirFunction function in program.Functions)
            {
                functionScopes.Add(function, allocator.CreateFunctionScope());
            }
            var panic = allocator.Allocate(JavaScriptBindingRole.RuntimeHelper, JavaScriptSymbolicBindingRole.Panic, "panic");
            var unwrapPanic = usesUnwrap ? allocator.Allocate(JavaScriptBindingRole.RuntimeHelper, JavaScriptSymbolicBindingRole.UnwrapPanic, "panic_unwrap") : JavaScriptBindingReference.Empty;
            bool usesTaggedValues = catalog.Enums.Count > 0 || results.Types.Count > 0 || usesTryExcept;
            var makeValue = usesTaggedValues ? allocator.Allocate(JavaScriptBindingRole.Constructor, JavaScriptSymbolicBindingRole.ValueConstructor, "make") : JavaScriptBindingReference.Empty;
            var flowToken = usesTryExcept ? allocator.Allocate(JavaScriptBindingRole.Flow, JavaScriptSymbolicBindingRole.FlowToken, "flow_token") : JavaScriptBindingReference.Empty;
            var flowValue = usesTryExcept ? allocator.Allocate(JavaScriptBindingRole.Flow, JavaScriptSymbolicBindingRole.FlowValue, "flow_value") : JavaScriptBindingReference.Empty;
            var flowToHandler = usesTryExcept ? allocator.Allocate(JavaScriptBindingRole.Flow, JavaScriptSymbolicBindingRole.FlowHandler, "flow_handler") : JavaScriptBindingReference.Empty;
            var flowToFunction = usesTryExcept ? allocator.Allocate(JavaScriptBindingRole.Flow, JavaScriptSymbolicBindingRole.FlowFunction, "flow_function") : JavaScriptBindingReference.Empty;
            var validateFlow = usesTryExcept ? allocator.Allocate(JavaScriptBindingRole.Flow, JavaScriptSymbolicBindingRole.FlowValidator, "flow_validate") : JavaScriptBindingReference.Empty;
            var tsonRuntime = program.TsonEncodingPlans.Count > 0
                ? allocator.Allocate(JavaScriptBindingRole.RuntimeHelper, JavaScriptSymbolicBindingRole.TsonRuntime, "tson")
                : JavaScriptBindingReference.Empty;
            bool usesTsonTableEncoding = program.TsonEncodingPlans.Any(plan => plan.TablePlan is not null);
            var recordTypeTokens = new Dictionary<MirRecordDefinition, JavaScriptBindingReference>();
            var recordInstances = new Dictionary<MirRecordDefinition, JavaScriptBindingReference>();
            var recordConstructors = new Dictionary<MirRecordDefinition, JavaScriptBindingReference>();
            var recordValidators = new Dictionary<MirRecordDefinition, JavaScriptBindingReference>();
            var recordFieldSlots = new Dictionary<MirRecordFieldDefinition, JavaScriptBindingReference>();
            foreach (MirRecordDefinition record in catalog.Records)
            {
                string recordIdentity = JavaScriptIdentifierEncoder.Encode(record.Id.Value);
                recordTypeTokens.Add(record, allocator.Allocate(JavaScriptBindingRole.TypeToken, JavaScriptSymbolicBindingRole.RecordType, $"record_type_{recordIdentity}"));
                recordInstances.Add(record, allocator.Allocate(JavaScriptBindingRole.ProvenanceSet, JavaScriptSymbolicBindingRole.RecordInstances, $"record_instances_{recordIdentity}"));
                recordConstructors.Add(record, allocator.Allocate(JavaScriptBindingRole.Constructor, JavaScriptSymbolicBindingRole.RecordConstructor, $"record_make_{recordIdentity}"));
                recordValidators.Add(record, allocator.Allocate(JavaScriptBindingRole.Validator, JavaScriptSymbolicBindingRole.RecordValidator, $"record_require_{recordIdentity}"));
                foreach (MirRecordFieldDefinition field in record.Fields)
                {
                    string fieldIdentity = JavaScriptIdentifierEncoder.Encode(field.Id.Value);
                    recordFieldSlots.Add(field, allocator.Allocate(JavaScriptBindingRole.SymbolSlot, JavaScriptSymbolicBindingRole.RecordField, $"record_field_{fieldIdentity}"));
                }
            }

            var typeTokens = new Dictionary<EnumInfo, JavaScriptBindingReference>();
            var enumInstances = new Dictionary<EnumInfo, JavaScriptBindingReference>();
            var validators = new Dictionary<EnumInfo, JavaScriptBindingReference>();
            foreach (EnumInfo enumInfo in catalog.Enums)
            {
                typeTokens.Add(enumInfo, allocator.Allocate(JavaScriptBindingRole.TypeToken, JavaScriptSymbolicBindingRole.EnumType, "type"));
                enumInstances.Add(enumInfo, allocator.Allocate(JavaScriptBindingRole.ProvenanceSet, JavaScriptSymbolicBindingRole.EnumInstances, "instances"));
                validators.Add(enumInfo, allocator.Allocate(JavaScriptBindingRole.Validator, JavaScriptSymbolicBindingRole.EnumValidator, "validate"));
            }

            var resultTypeTokens = new Dictionary<ResultInfo, JavaScriptBindingReference>();
            var resultValidators = new Dictionary<ResultInfo, JavaScriptBindingReference>();
            foreach (ResultInfo result in results.Types)
            {
                resultTypeTokens.Add(result, allocator.Allocate(JavaScriptBindingRole.TypeToken, JavaScriptSymbolicBindingRole.ResultType, "result_type"));
                resultValidators.Add(result, allocator.Allocate(JavaScriptBindingRole.Validator, JavaScriptSymbolicBindingRole.ResultValidator, "result_validate"));
            }

            bool usesTables = catalog.Tables.Count > 0;
            var columnCarrierToken = usesTables ? allocator.Allocate(JavaScriptBindingRole.TypeToken, JavaScriptSymbolicBindingRole.ColumnType, "column_type") : JavaScriptBindingReference.Empty;
            var columnReadSlot = usesTables ? allocator.Allocate(JavaScriptBindingRole.SymbolSlot, JavaScriptSymbolicBindingRole.ColumnRead, "column_read") : JavaScriptBindingReference.Empty;
            var columnInstances = usesTsonTableEncoding ? allocator.Allocate(JavaScriptBindingRole.ProvenanceSet, JavaScriptSymbolicBindingRole.ColumnInstances, "column_instances") : JavaScriptBindingReference.Empty;
            var columnValuesSlot = usesTsonTableEncoding ? allocator.Allocate(JavaScriptBindingRole.SymbolSlot, JavaScriptSymbolicBindingRole.ColumnValues, "column_values") : JavaScriptBindingReference.Empty;
            var columnValidator = usesTables ? allocator.Allocate(JavaScriptBindingRole.Validator, JavaScriptSymbolicBindingRole.ColumnValidator, "column_require") : JavaScriptBindingReference.Empty;
            var tableRowTableSlot = usesTables ? allocator.Allocate(JavaScriptBindingRole.SymbolSlot, JavaScriptSymbolicBindingRole.TableRowTable, "table_row_table") : JavaScriptBindingReference.Empty;
            var tableRowIndexSlot = usesTables ? allocator.Allocate(JavaScriptBindingRole.SymbolSlot, JavaScriptSymbolicBindingRole.TableRowIndex, "table_row_index") : JavaScriptBindingReference.Empty;
            var tableTypeTokens = new Dictionary<MirTableDefinition, JavaScriptBindingReference>();
            var tableInstances = new Dictionary<MirTableDefinition, JavaScriptBindingReference>();
            var tableRowTypeTokens = new Dictionary<MirTableDefinition, JavaScriptBindingReference>();
            var tableValidators = new Dictionary<MirTableDefinition, JavaScriptBindingReference>();
            var tableRowValidators = new Dictionary<MirTableDefinition, JavaScriptBindingReference>();
            var tableCreates = new Dictionary<MirTableDefinition, JavaScriptBindingReference>();
            var tableCreateRows = new Dictionary<MirTableDefinition, JavaScriptBindingReference>();
            var tableSingletons = new Dictionary<MirTableDefinition, JavaScriptBindingReference>();
            var tableRowReadSlots = new Dictionary<MirTableDefinition, JavaScriptBindingReference>();
            var tableColumnSlots = new Dictionary<MirTableColumnDefinition, JavaScriptBindingReference>();
            var tableColumnTokens = new Dictionary<MirTableColumnDefinition, JavaScriptBindingReference>();
            var tableStorages = new Dictionary<MirTableColumnDefinition, JavaScriptBindingReference>();
            var tableColumnValues = new Dictionary<MirTableColumnDefinition, JavaScriptBindingReference>();
            foreach (MirTableDefinition table in catalog.Tables)
            {
                string identity = JavaScriptIdentifierEncoder.Encode(table.Id.Value);
                tableTypeTokens.Add(table, allocator.Allocate(JavaScriptBindingRole.TypeToken, JavaScriptSymbolicBindingRole.TableType, $"table_type_{identity}"));
                tableInstances.Add(table, usesTsonTableEncoding ? allocator.Allocate(JavaScriptBindingRole.ProvenanceSet, JavaScriptSymbolicBindingRole.TableInstances, $"table_instances_{identity}") : JavaScriptBindingReference.Empty);
                tableRowTypeTokens.Add(table, allocator.Allocate(JavaScriptBindingRole.TypeToken, JavaScriptSymbolicBindingRole.TableRowType, $"table_row_type_{identity}"));
                tableValidators.Add(table, allocator.Allocate(JavaScriptBindingRole.Validator, JavaScriptSymbolicBindingRole.TableValidator, $"table_require_{identity}"));
                tableRowValidators.Add(table, allocator.Allocate(JavaScriptBindingRole.Validator, JavaScriptSymbolicBindingRole.TableRowValidator, $"table_row_require_{identity}"));
                tableCreates.Add(table, allocator.Allocate(JavaScriptBindingRole.Constructor, JavaScriptSymbolicBindingRole.TableConstructor, $"table_create_{identity}"));
                tableCreateRows.Add(table, allocator.Allocate(JavaScriptBindingRole.Constructor, JavaScriptSymbolicBindingRole.TableRowConstructor, $"table_row_create_{identity}"));
                tableSingletons.Add(table, allocator.Allocate(JavaScriptBindingRole.Singleton, JavaScriptSymbolicBindingRole.TableValue, $"table_value_{identity}"));
                tableRowReadSlots.Add(table, allocator.Allocate(JavaScriptBindingRole.SymbolSlot, JavaScriptSymbolicBindingRole.TableRowSlot, $"table_rows_{identity}"));
                foreach (MirTableColumnDefinition column in table.Columns)
                {
                    string columnIdentity = JavaScriptIdentifierEncoder.Encode(column.Id.Value);
                    tableColumnSlots.Add(column, allocator.Allocate(JavaScriptBindingRole.SymbolSlot, JavaScriptSymbolicBindingRole.TableColumnSlot, $"table_column_{columnIdentity}"));
                    tableColumnTokens.Add(column, allocator.Allocate(JavaScriptBindingRole.TypeToken, JavaScriptSymbolicBindingRole.TableColumnToken, $"column_type_{columnIdentity}"));
                    tableStorages.Add(column, allocator.Allocate(JavaScriptBindingRole.Storage, JavaScriptSymbolicBindingRole.TableStorage, $"table_storage_{columnIdentity}"));
                    tableColumnValues.Add(column, allocator.Allocate(JavaScriptBindingRole.Storage, JavaScriptSymbolicBindingRole.TableColumnValue, $"table_column_value_{columnIdentity}"));
                }
            }

            var names = new GeneratedNames(
                allocator,
                panic,
                unwrapPanic,
                makeValue,
                flowToken,
                flowValue,
                flowToHandler,
                flowToFunction,
                validateFlow,
                tsonRuntime,
                usesTsonTableEncoding,
                typeTokens,
                enumInstances,
                validators,
                resultTypeTokens,
                resultValidators,
                recordTypeTokens,
                recordInstances,
                recordConstructors,
                recordValidators,
                recordFieldSlots,
                columnCarrierToken,
                columnInstances,
                columnReadSlot,
                columnValuesSlot,
                columnValidator,
                tableRowTableSlot,
                tableRowIndexSlot,
                tableTypeTokens,
                tableInstances,
                tableRowTypeTokens,
                tableValidators,
                tableRowValidators,
                tableCreates,
                tableCreateRows,
                tableSingletons,
                tableRowReadSlots,
                tableColumnSlots,
                tableColumnTokens,
                tableStorages,
                tableColumnValues,
                functionScopes,
                profile);
            allocator.Validate();
            return names;
        }
    }

    private sealed class NameAllocator
    {
        private readonly JavaScriptEmissionDocument document = new();
        private readonly JavaScriptNameAllocator allocator;
        private JavaScriptScopeId currentScope;

        public NameAllocator(HashSet<string> occupied, JavaScriptEmissionProfile profile)
        {
            allocator = new JavaScriptNameAllocator(document, document.ProgramScope, occupied, profile);
            currentScope = document.ProgramScope;
        }

        public JavaScriptEmissionDocument Document => document;

        public JavaScriptScopeId CreateFunctionScope()
        {
            return document.CreateScope(JavaScriptScopeKind.Function, document.ProgramScope);
        }

        public void EnterScope(JavaScriptScopeId scope)
        {
            currentScope = scope;
        }

        public JavaScriptBindingReference Allocate(
            JavaScriptBindingRole role,
            JavaScriptSymbolicBindingRole symbolicRole,
            string diagnosticPurpose)
        {
            return allocator.Allocate(currentScope, role, diagnosticPurpose, symbolicRole: symbolicRole).Reference;
        }

        public void Validate()
        {
            document.Validate();
        }
    }
}
