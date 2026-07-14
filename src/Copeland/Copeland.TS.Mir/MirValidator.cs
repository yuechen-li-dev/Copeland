namespace Copeland.TS.Mir;

public sealed record MirValidationDiagnostic(string Message);

public static class MirValidator
{
    public static IReadOnlyList<MirValidationDiagnostic> Validate(MirProgram program)
    {
        var diagnostics = new List<MirValidationDiagnostic>();
        ValidateRecordModel(program, diagnostics);
        ValidateTableModel(program, diagnostics);
        foreach (var function in program.Functions)
        {
            var handlerIds = new HashSet<MirHandlerId>();
            ValidateStatements(function.Body, [], handlerIds, diagnostics);
            ValidateFunctionPropagationTargets(function.Body, function.ReturnType, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidateTableModel(MirProgram program, List<MirValidationDiagnostic> diagnostics)
    {
        if (program.Tables.Count > 0)
        {
            ValidateTableBoundsErrorDefinition(program.Enums, diagnostics);
        }
        var tables = new Dictionary<MirTableId, MirTableDefinition>();
        var tableNames = new HashSet<string>(StringComparer.Ordinal);
        var rowTypeIds = new HashSet<string>(StringComparer.Ordinal);
        var columns = new Dictionary<MirTableColumnId, MirTableColumnDefinition>();
        foreach (var table in program.Tables)
        {
            if (string.IsNullOrWhiteSpace(table.Id.Value) || !tables.TryAdd(table.Id, table))
                diagnostics.Add(new MirValidationDiagnostic($"Table has a blank or duplicate identity '{table.Id}'."));
            if (string.IsNullOrWhiteSpace(table.Name) || !tableNames.Add(table.Name))
                diagnostics.Add(new MirValidationDiagnostic($"Table has a blank or duplicate name '{table.Name}'."));
            if (string.IsNullOrWhiteSpace(table.RowTypeId) || !rowTypeIds.Add(table.RowTypeId))
                diagnostics.Add(new MirValidationDiagnostic($"Table '{table.Name}' has a blank or duplicate row type identity '{table.RowTypeId}'."));
            if (table.Columns.Count == 0)
                diagnostics.Add(new MirValidationDiagnostic($"Table '{table.Name}' must have at least one column."));
            if (table.RowCount < 0)
                diagnostics.Add(new MirValidationDiagnostic($"Table '{table.Name}' has a negative row count."));

            var columnNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var column in table.Columns)
            {
                if (string.IsNullOrWhiteSpace(column.Id.Value) || !columns.TryAdd(column.Id, column))
                    diagnostics.Add(new MirValidationDiagnostic($"Table '{table.Name}' has a blank or duplicate column identity '{column.Id}'."));
                if (string.IsNullOrWhiteSpace(column.Name) || !columnNames.Add(column.Name))
                    diagnostics.Add(new MirValidationDiagnostic($"Table '{table.Name}' has a blank or duplicate column name '{column.Name}'."));
                if (column.ElementType.Identifier is "error" or "void")
                    diagnostics.Add(new MirValidationDiagnostic($"Table column '{table.Name}.{column.Name}' has an invalid element type."));
                if (column.Constants.Count != table.RowCount)
                    diagnostics.Add(new MirValidationDiagnostic($"Table column '{table.Name}.{column.Name}' has {column.Constants.Count} constants but row count is {table.RowCount}."));
                foreach (var constant in column.Constants)
                {
                    if (!MirTypeFacts.AreEquivalent(constant.Type, column.ElementType))
                        diagnostics.Add(new MirValidationDiagnostic($"Table constant in '{table.Name}.{column.Name}' does not match the column element type."));
                    ValidateTableConstant(constant, column.ElementType, program, diagnostics, $"{table.Name}.{column.Name}");
                }
            }
        }

        foreach (var function in program.Functions)
        {
            ValidateTableType(function.ReturnType, tables, rowTypeIds, diagnostics, $"function '{function.Name}' return");
            foreach (var parameter in function.Parameters) ValidateTableType(parameter.Type, tables, rowTypeIds, diagnostics, $"parameter '{parameter.Name}'");
            foreach (var local in function.Locals) ValidateTableType(local.Type, tables, rowTypeIds, diagnostics, $"local '{local.Name}'");
            ValidateTableStatements(function.Body, tables, rowTypeIds, columns, diagnostics);
        }
    }

    private static void ValidateTableBoundsErrorDefinition(IReadOnlyList<MirEnum> enums, List<MirValidationDiagnostic> diagnostics)
    {
        MirEnum[] matchingDefinitions = enums.Where(@enum => @enum.Name == "TableBoundsError").ToArray();
        if (matchingDefinitions.Length != 1)
        {
            diagnostics.Add(new MirValidationDiagnostic("Table MIR requires the compiler-owned TableBoundsError enum."));
            return;
        }
        MirEnum boundsError = matchingDefinitions[0];

        bool hasInvalidIndex = boundsError.Cases.Any(@case => @case.Name == "InvalidIndex"
            && @case.PayloadFields.Count == 1
            && @case.PayloadFields[0].Name == "index"
            && @case.PayloadFields[0].Type.Identifier == "number");
        bool hasOutOfBounds = boundsError.Cases.Any(@case => @case.Name == "OutOfBounds"
            && @case.PayloadFields.Count == 2
            && @case.PayloadFields[0].Name == "index"
            && @case.PayloadFields[0].Type.Identifier == "number"
            && @case.PayloadFields[1].Name == "rowCount"
            && @case.PayloadFields[1].Type.Identifier == "number");
        if (!hasInvalidIndex || !hasOutOfBounds || boundsError.Cases.Count != 2)
        {
            diagnostics.Add(new MirValidationDiagnostic("TableBoundsError does not have its required compiler-owned cases and payload types."));
        }
    }

    private static void ValidateTableConstant(MirTableConstant constant, MirType expectedType, MirProgram program, List<MirValidationDiagnostic> diagnostics, string context)
    {
        if (!MirTypeFacts.AreEquivalent(constant.Type, expectedType))
        {
            diagnostics.Add(new MirValidationDiagnostic($"Table constant in '{context}' does not match the column element type."));
        }

        switch (constant)
        {
            case MirTableLiteralConstant literal when IsValidTableLiteral(literal):
                return;
            case MirTableRecordConstant record:
                MirRecordDefinition? definition = program.Records.FirstOrDefault(candidate => candidate.Id == record.RecordTypeId);
                if (definition is null || record.Type is not MirRecordType type || type.RecordTypeId != record.RecordTypeId)
                {
                    diagnostics.Add(new MirValidationDiagnostic($"Table record constant in '{context}' has an unknown record identity."));
                    return;
                }
                if (record.Fields.Count != definition.Fields.Count)
                    diagnostics.Add(new MirValidationDiagnostic($"Table record constant in '{context}' does not provide every record field."));
                var seenFieldIds = new HashSet<MirRecordFieldId>();
                foreach (var field in record.Fields)
                {
                    if (!seenFieldIds.Add(field.FieldId))
                    {
                        diagnostics.Add(new MirValidationDiagnostic($"Table record constant in '{context}' has a duplicate field identity '{field.FieldId}'."));
                    }
                    MirRecordFieldDefinition? fieldDefinition = definition.Fields.FirstOrDefault(candidate => candidate.Id == field.FieldId);
                    if (fieldDefinition is null) diagnostics.Add(new MirValidationDiagnostic($"Table record constant in '{context}' has an unknown field identity '{field.FieldId}'."));
                    else ValidateTableConstant(field.Value, fieldDefinition.Type, program, diagnostics, context);
                }
                if (definition.Fields.Any(field => !seenFieldIds.Contains(field.Id)))
                {
                    diagnostics.Add(new MirValidationDiagnostic($"Table record constant in '{context}' does not provide every record field."));
                }
                return;
            case MirTableEnumConstant value:
                MirEnum? enumDefinition = program.Enums.FirstOrDefault(candidate => candidate.Name == value.EnumName);
                MirEnumCase? @case = enumDefinition?.Cases.FirstOrDefault(candidate => candidate.Name == value.CaseName);
                if (enumDefinition is null || @case is null || value.Type.Identifier != value.EnumName)
                {
                    diagnostics.Add(new MirValidationDiagnostic($"Table enum constant in '{context}' has an unknown enum or case."));
                    return;
                }
                if (value.Payloads.Count != @case.PayloadFields.Count)
                    diagnostics.Add(new MirValidationDiagnostic($"Table enum constant in '{context}' has an invalid payload count."));
                for (int index = 0; index < Math.Min(value.Payloads.Count, @case.PayloadFields.Count); index++)
                    ValidateTableConstant(value.Payloads[index], @case.PayloadFields[index].Type, program, diagnostics, context);
                return;
            case MirTableResultConstant result:
                if (result.Type.SuccessType.Identifier == "void")
                {
                    diagnostics.Add(new MirValidationDiagnostic($"Table Result constant in '{context}' cannot use a void success payload."));
                    return;
                }
                ValidateTableConstant(result.Payload, result.IsOk ? result.Type.SuccessType : result.Type.ErrorType, program, diagnostics, context);
                return;
            default:
                diagnostics.Add(new MirValidationDiagnostic($"Table constant in '{context}' is not a supported closed constant."));
                return;
        }
    }

    private static bool IsValidTableLiteral(MirTableLiteralConstant literal)
        => literal.Type.Identifier switch
        {
            "number" => literal.Value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal,
            "string" => literal.Value is string,
            "boolean" => literal.Value is bool,
            _ => false,
        };

    private static void ValidateTableType(MirType type, IReadOnlyDictionary<MirTableId, MirTableDefinition> tables, IReadOnlySet<string> rowTypeIds, List<MirValidationDiagnostic> diagnostics, string context)
    {
        switch (type)
        {
            case MirTableType table when !tables.ContainsKey(table.TableId):
                diagnostics.Add(new MirValidationDiagnostic($"Table type '{table.TableId}' used by {context} has no definition."));
                break;
            case MirTableRowType row when !rowTypeIds.Contains(row.RowTypeId):
                diagnostics.Add(new MirValidationDiagnostic($"Table row type '{row.RowTypeId}' used by {context} has no definition."));
                break;
            case MirColumnType column:
                ValidateTableType(column.ElementType, tables, rowTypeIds, diagnostics, context);
                break;
            case MirArrayType array:
                ValidateTableType(array.ElementType, tables, rowTypeIds, diagnostics, context);
                break;
            case MirResultType result:
                ValidateTableType(result.SuccessType, tables, rowTypeIds, diagnostics, context);
                ValidateTableType(result.ErrorType, tables, rowTypeIds, diagnostics, context);
                break;
        }
    }

    private static void ValidateTableStatements(IReadOnlyList<MirStatement> statements, IReadOnlyDictionary<MirTableId, MirTableDefinition> tables, IReadOnlySet<string> rowTypeIds, IReadOnlyDictionary<MirTableColumnId, MirTableColumnDefinition> columns, List<MirValidationDiagnostic> diagnostics)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case MirVariableDeclarationStatement declaration: ValidateTableExpression(declaration.Initializer, tables, rowTypeIds, columns, diagnostics); break;
                case MirExpressionStatement expression: ValidateTableExpression(expression.Expression, tables, rowTypeIds, columns, diagnostics); break;
                case MirReturnStatement { Expression: not null } returned: ValidateTableExpression(returned.Expression, tables, rowTypeIds, columns, diagnostics); break;
                case MirIfStatement conditional:
                    ValidateTableExpression(conditional.Condition, tables, rowTypeIds, columns, diagnostics);
                    ValidateTableStatements(conditional.ThenStatements, tables, rowTypeIds, columns, diagnostics);
                    if (conditional.ElseStatements is not null) ValidateTableStatements(conditional.ElseStatements, tables, rowTypeIds, columns, diagnostics);
                    break;
            }
        }
    }

    private static void ValidateTableExpression(MirExpression expression, IReadOnlyDictionary<MirTableId, MirTableDefinition> tables, IReadOnlySet<string> rowTypeIds, IReadOnlyDictionary<MirTableColumnId, MirTableColumnDefinition> columns, List<MirValidationDiagnostic> diagnostics)
    {
        ValidateTableType(expression.Type, tables, rowTypeIds, diagnostics, "expression");
        switch (expression)
        {
            case MirTableReferenceExpression reference:
                if (!tables.TryGetValue(reference.TableId, out var table) || reference.Type is not MirTableType type || type.TableId != reference.TableId)
                    diagnostics.Add(new MirValidationDiagnostic($"Table reference '{reference.TableId}' has an unknown identity or incorrect type."));
                break;
            case MirTableColumnAccessExpression access:
                ValidateTableExpression(access.Receiver, tables, rowTypeIds, columns, diagnostics);
                if (!tables.TryGetValue(access.TableId, out var owner) || !columns.TryGetValue(access.ColumnId, out var column) || !owner.Columns.Contains(column)
                    || access.Receiver.Type is not MirTableType receiverType || receiverType.TableId != access.TableId
                    || access.Type is not MirColumnType columnType || !MirTypeFacts.AreEquivalent(columnType.ElementType, column.ElementType))
                    diagnostics.Add(new MirValidationDiagnostic($"Table column access '{access.ColumnId}' has an invalid table identity, receiver, or type."));
                break;
            case MirTableRowAccessExpression access:
                ValidateTableExpression(access.Receiver, tables, rowTypeIds, columns, diagnostics);
                ValidateTableExpression(access.Index, tables, rowTypeIds, columns, diagnostics);
                if (!tables.TryGetValue(access.TableId, out var indexedTable)
                    || access.Receiver.Type is not MirTableType tableReceiver || tableReceiver.TableId != access.TableId
                    || access.Index.Type.Identifier != "number"
                    || access.Type is not MirResultType { SuccessType: MirTableRowType row, ErrorType: MirNamedType rowError }
                    || row.RowTypeId != indexedTable.RowTypeId || rowError.Identifier != "TableBoundsError")
                    diagnostics.Add(new MirValidationDiagnostic($"Table row access '{access.TableId}' has an invalid receiver, index, or Result bounds type."));
                break;
            case MirColumnElementAccessExpression access:
                ValidateTableExpression(access.Receiver, tables, rowTypeIds, columns, diagnostics);
                ValidateTableExpression(access.Index, tables, rowTypeIds, columns, diagnostics);
                if (access.Receiver.Type is not MirColumnType columnReceiver || access.Index.Type.Identifier != "number"
                    || access.Type is not MirResultType { ErrorType: MirNamedType columnError } result
                    || !MirTypeFacts.AreEquivalent(result.SuccessType, columnReceiver.ElementType) || columnError.Identifier != "TableBoundsError")
                    diagnostics.Add(new MirValidationDiagnostic("Column element access has an invalid receiver, index, or Result bounds type."));
                break;
            case MirTableRowFieldAccessExpression access:
                ValidateTableExpression(access.Receiver, tables, rowTypeIds, columns, diagnostics);
                if (access.Receiver.Type is not MirTableRowType rowReceiver || rowReceiver.RowTypeId != access.RowTypeId || !rowTypeIds.Contains(access.RowTypeId))
                    diagnostics.Add(new MirValidationDiagnostic($"Table row field access '{access.FieldId}' has an invalid row receiver or row type."));
                else
                {
                    MirTableDefinition? rowOwner = tables.Values.FirstOrDefault(table => table.RowTypeId == access.RowTypeId);
                    MirTableColumnDefinition? field = rowOwner?.Columns.FirstOrDefault(column => access.FieldId == column.Id.Value + ".f");
                    if (field is null)
                        diagnostics.Add(new MirValidationDiagnostic($"Table row field access '{access.FieldId}' has an unknown field identity."));
                    else if (!MirTypeFacts.AreEquivalent(field.ElementType, access.Type))
                        diagnostics.Add(new MirValidationDiagnostic($"Table row field access type does not match field '{access.FieldId}'."));
                }
                break;
            default:
                foreach (var child in EnumerateTableExpressionChildren(expression)) ValidateTableExpression(child, tables, rowTypeIds, columns, diagnostics);
                break;
        }
    }

    private static IEnumerable<MirExpression> EnumerateTableExpressionChildren(MirExpression expression)
        => expression switch
        {
            MirUnaryExpression unary => [unary.Operand],
            MirBinaryExpression binary => [binary.Left, binary.Right],
            MirAssignmentExpression assignment => [assignment.Expression],
            MirCallExpression call => call.Arguments,
            MirArrayExpression array => array.Elements,
            MirRecordConstructionExpression record => record.Initializers.Select(value => value.Value),
            MirRecordFieldAccessExpression access => [access.Receiver],
            MirRecordWithExpression update => update.Replacements.Select(value => value.Value).Prepend(update.Source),
            MirEnumValueExpression value => value.Arguments,
            MirMatchExpression match => match.Arms.Select(arm => arm.Expression).Prepend(match.Scrutinee),
            MirResultMatchExpression match => [match.Scrutinee, match.OkExpression, match.ErrExpression],
            MirIfExpression conditional => [conditional.Condition, conditional.ThenExpression, conditional.ElseExpression],
            MirOkExpression ok => [ok.Payload],
            MirErrExpression err => [err.Payload],
            MirPropagateExpression propagate => [propagate.Operand],
            MirUnwrapExpression unwrap => [unwrap.Operand],
            MirTryExpression value => value.Protected.PrefixStatements.OfType<MirExpressionStatement>().Select(statement => statement.Expression).Append(value.Protected.ValueExpression).Append(value.Handler.ValueExpression),
            _ => [],
        };

    private static void ValidateRecordModel(MirProgram program, List<MirValidationDiagnostic> diagnostics)
    {
        var recordsById = new Dictionary<MirRecordTypeId, MirRecordDefinition>();
        var recordNames = new HashSet<string>(StringComparer.Ordinal);
        var allFieldIds = new HashSet<MirRecordFieldId>();
        foreach (var record in program.Records)
        {
            if (string.IsNullOrWhiteSpace(record.Id.Value))
            {
                diagnostics.Add(new MirValidationDiagnostic("Record identity must not be blank."));
            }
            else if (!recordsById.TryAdd(record.Id, record))
            {
                diagnostics.Add(new MirValidationDiagnostic($"Duplicate record identity '{record.Id}'."));
            }
            if (string.IsNullOrWhiteSpace(record.Name) || !recordNames.Add(record.Name))
            {
                diagnostics.Add(new MirValidationDiagnostic($"Duplicate or blank record name '{record.Name}'."));
            }

            var fieldIds = new HashSet<MirRecordFieldId>();
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in record.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Id.Value) || !fieldIds.Add(field.Id) || !allFieldIds.Add(field.Id))
                {
                    diagnostics.Add(new MirValidationDiagnostic($"Record '{record.Name}' has a blank or duplicate field identity '{field.Id}'."));
                }
                if (string.IsNullOrWhiteSpace(field.Name) || !fieldNames.Add(field.Name))
                {
                    diagnostics.Add(new MirValidationDiagnostic($"Record '{record.Name}' has a blank or duplicate field name '{field.Name}'."));
                }
            }
        }

        foreach (var record in program.Records)
        {
            foreach (var field in record.Fields)
            {
                ValidateRecordTypeReference(field.Type, recordsById, diagnostics, $"field '{record.Name}.{field.Name}'");
            }
        }
        ValidateRecordDefinitionCycles(program.Records, program.Enums, recordsById, diagnostics);

        foreach (var @enum in program.Enums)
        {
            foreach (var field in @enum.Cases.SelectMany(@case => @case.PayloadFields))
            {
                ValidateRecordTypeReference(field.Type, recordsById, diagnostics, $"enum '{@enum.Name}' payload");
            }
        }
        foreach (var function in program.Functions)
        {
            ValidateRecordTypeReference(function.ReturnType, recordsById, diagnostics, $"function '{function.Name}' return");
            foreach (var parameter in function.Parameters) ValidateRecordTypeReference(parameter.Type, recordsById, diagnostics, $"parameter '{parameter.Name}'");
            foreach (var local in function.Locals) ValidateRecordTypeReference(local.Type, recordsById, diagnostics, $"local '{local.Name}'");
            ValidateRecordStatements(function.Body, recordsById, diagnostics);
        }
    }

    private static void ValidateRecordDefinitionCycles(
        IReadOnlyList<MirRecordDefinition> records,
        IReadOnlyList<MirEnum> enums,
        IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> recordsById,
        List<MirValidationDiagnostic> diagnostics)
    {
        var enumsByName = new Dictionary<string, MirEnum>(StringComparer.Ordinal);
        foreach (var @enum in enums)
        {
            enumsByName.TryAdd(@enum.Name, @enum);
        }
        var visiting = new HashSet<MirRecordTypeId>();
        var visited = new HashSet<MirRecordTypeId>();
        foreach (var record in records)
        {
            Visit(record);
        }

        void Visit(MirRecordDefinition record)
        {
            if (visited.Contains(record.Id)) return;
            if (!visiting.Add(record.Id))
            {
                diagnostics.Add(new MirValidationDiagnostic($"Recursive record definition involving '{record.Id}' is unsupported."));
                return;
            }
            foreach (var id in record.Fields.SelectMany(field => EnumerateCycleRecordTypeIds(field.Type, enumsByName, [])))
            {
                if (recordsById.TryGetValue(id, out var dependency)) Visit(dependency);
            }
            visiting.Remove(record.Id);
            visited.Add(record.Id);
        }
    }

    private static IEnumerable<MirRecordTypeId> EnumerateCycleRecordTypeIds(
        MirType type,
        IReadOnlyDictionary<string, MirEnum> enumsByName,
        HashSet<string> visitedEnums)
    {
        switch (type)
        {
            case MirRecordType recordType:
                yield return recordType.RecordTypeId;
                break;
            case MirArrayType arrayType:
                foreach (var id in EnumerateCycleRecordTypeIds(arrayType.ElementType, enumsByName, visitedEnums)) yield return id;
                break;
            case MirResultType resultType:
                foreach (var id in EnumerateCycleRecordTypeIds(resultType.SuccessType, enumsByName, visitedEnums)) yield return id;
                foreach (var id in EnumerateCycleRecordTypeIds(resultType.ErrorType, enumsByName, visitedEnums)) yield return id;
                break;
            case MirNamedType namedType when enumsByName.TryGetValue(namedType.Identifier, out var @enum) && visitedEnums.Add(namedType.Identifier):
                foreach (var payloadType in @enum.Cases.SelectMany(@case => @case.PayloadFields).Select(field => field.Type))
                {
                    foreach (var id in EnumerateCycleRecordTypeIds(payloadType, enumsByName, visitedEnums)) yield return id;
                }
                break;
        }
    }

    private static IEnumerable<MirRecordTypeId> EnumerateRecordTypeIds(MirType type)
    {
        switch (type)
        {
            case MirRecordType recordType:
                yield return recordType.RecordTypeId;
                break;
            case MirArrayType arrayType:
                foreach (var id in EnumerateRecordTypeIds(arrayType.ElementType)) yield return id;
                break;
            case MirResultType resultType:
                foreach (var id in EnumerateRecordTypeIds(resultType.SuccessType)) yield return id;
                foreach (var id in EnumerateRecordTypeIds(resultType.ErrorType)) yield return id;
                break;
        }
    }

    private static void ValidateRecordTypeReference(MirType type, IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records, List<MirValidationDiagnostic> diagnostics, string context)
    {
        foreach (var id in EnumerateRecordTypeIds(type))
        {
            if (!records.ContainsKey(id)) diagnostics.Add(new MirValidationDiagnostic($"Record type '{id}' used by {context} has no definition."));
        }
    }

    private static void ValidateRecordStatements(IReadOnlyList<MirStatement> statements, IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records, List<MirValidationDiagnostic> diagnostics)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case MirVariableDeclarationStatement declaration: ValidateRecordExpression(declaration.Initializer, records, diagnostics); break;
                case MirExpressionStatement expression: ValidateRecordExpression(expression.Expression, records, diagnostics); break;
                case MirReturnStatement { Expression: not null } returned: ValidateRecordExpression(returned.Expression, records, diagnostics); break;
                case MirIfStatement conditional:
                    ValidateRecordExpression(conditional.Condition, records, diagnostics);
                    ValidateRecordStatements(conditional.ThenStatements, records, diagnostics);
                    if (conditional.ElseStatements is not null) ValidateRecordStatements(conditional.ElseStatements, records, diagnostics);
                    break;
                case MirWhileStatement loop:
                    ValidateRecordExpression(loop.Condition, records, diagnostics);
                    ValidateRecordStatements(loop.BodyStatements, records, diagnostics);
                    break;
                case MirForStatement loop:
                    if (loop.Initializer is not null) ValidateRecordStatements([loop.Initializer], records, diagnostics);
                    if (loop.Condition is not null) ValidateRecordExpression(loop.Condition, records, diagnostics);
                    if (loop.Increment is not null) ValidateRecordExpression(loop.Increment, records, diagnostics);
                    ValidateRecordStatements(loop.BodyStatements, records, diagnostics);
                    break;
            }
        }
    }

    private static void ValidateRecordExpression(MirExpression expression, IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records, List<MirValidationDiagnostic> diagnostics)
    {
        ValidateRecordTypeReference(expression.Type, records, diagnostics, "expression");
        switch (expression)
        {
            case MirRecordConstructionExpression construction:
                ValidateRecordFieldValues(construction.RecordTypeId, construction.Initializers, records, diagnostics, requireComplete: true, allowEmpty: true);
                if (construction.Type is not MirRecordType constructionType || constructionType.RecordTypeId != construction.RecordTypeId) diagnostics.Add(new MirValidationDiagnostic("Record construction result type does not match its record identity."));
                foreach (var value in construction.Initializers) ValidateRecordExpression(value.Value, records, diagnostics);
                return;
            case MirRecordFieldAccessExpression access:
                ValidateRecordExpression(access.Receiver, records, diagnostics);
                if (access.Receiver.Type is not MirRecordType receiverType || receiverType.RecordTypeId != access.RecordTypeId)
                    diagnostics.Add(new MirValidationDiagnostic("Record field access receiver type does not match its record identity."));
                if (!records.TryGetValue(access.RecordTypeId, out var accessRecord) || accessRecord.Fields.All(field => field.Id != access.FieldId))
                    diagnostics.Add(new MirValidationDiagnostic($"Record field access uses unknown field identity '{access.FieldId}'."));
                else
                {
                    var field = accessRecord.Fields.Single(candidate => candidate.Id == access.FieldId);
                    if (!MirTypeFacts.AreEquivalent(field.Type, access.Type)) diagnostics.Add(new MirValidationDiagnostic($"Record field access type does not match field '{access.FieldId}'."));
                }
                return;
            case MirRecordWithExpression withExpression:
                ValidateRecordExpression(withExpression.Source, records, diagnostics);
                if (withExpression.Source.Type is not MirRecordType sourceType || sourceType.RecordTypeId != withExpression.RecordTypeId || withExpression.Type is not MirRecordType resultType || resultType.RecordTypeId != withExpression.RecordTypeId)
                    diagnostics.Add(new MirValidationDiagnostic("Record 'with' source or result type does not match its record identity."));
                ValidateRecordFieldValues(withExpression.RecordTypeId, withExpression.Replacements, records, diagnostics, requireComplete: false, allowEmpty: false);
                foreach (var value in withExpression.Replacements) ValidateRecordExpression(value.Value, records, diagnostics);
                return;
            case MirAssignmentExpression assignment: ValidateRecordExpression(assignment.Expression, records, diagnostics); return;
            case MirUnaryExpression unary: ValidateRecordExpression(unary.Operand, records, diagnostics); return;
            case MirBinaryExpression binary: ValidateRecordExpression(binary.Left, records, diagnostics); ValidateRecordExpression(binary.Right, records, diagnostics); return;
            case MirCallExpression call: foreach (var item in call.Arguments) ValidateRecordExpression(item, records, diagnostics); return;
            case MirArrayExpression array: foreach (var item in array.Elements) ValidateRecordExpression(item, records, diagnostics); return;
            case MirEnumValueExpression value: foreach (var item in value.Arguments) ValidateRecordExpression(item, records, diagnostics); return;
            case MirMatchExpression match: ValidateRecordExpression(match.Scrutinee, records, diagnostics); foreach (var arm in match.Arms) ValidateRecordExpression(arm.Expression, records, diagnostics); return;
            case MirResultMatchExpression match: ValidateRecordExpression(match.Scrutinee, records, diagnostics); ValidateRecordExpression(match.OkExpression, records, diagnostics); ValidateRecordExpression(match.ErrExpression, records, diagnostics); return;
            case MirIfExpression conditional: ValidateRecordExpression(conditional.Condition, records, diagnostics); ValidateRecordExpression(conditional.ThenExpression, records, diagnostics); ValidateRecordExpression(conditional.ElseExpression, records, diagnostics); return;
            case MirOkExpression ok: ValidateRecordExpression(ok.Payload, records, diagnostics); return;
            case MirErrExpression err: ValidateRecordExpression(err.Payload, records, diagnostics); return;
            case MirPropagateExpression propagation: ValidateRecordExpression(propagation.Operand, records, diagnostics); return;
            case MirUnwrapExpression unwrap: ValidateRecordExpression(unwrap.Operand, records, diagnostics); return;
            case MirTryExpression tryExpression:
                ValidateRecordStatements(tryExpression.Protected.PrefixStatements, records, diagnostics);
                ValidateRecordExpression(tryExpression.Protected.ValueExpression, records, diagnostics);
                ValidateRecordStatements(tryExpression.Handler.PrefixStatements, records, diagnostics);
                ValidateRecordExpression(tryExpression.Handler.ValueExpression, records, diagnostics);
                return;
        }
    }

    private static void ValidateRecordFieldValues(MirRecordTypeId recordTypeId, IReadOnlyList<MirRecordFieldValue> values, IReadOnlyDictionary<MirRecordTypeId, MirRecordDefinition> records, List<MirValidationDiagnostic> diagnostics, bool requireComplete, bool allowEmpty)
    {
        if (!records.TryGetValue(recordTypeId, out var record))
        {
            diagnostics.Add(new MirValidationDiagnostic($"Record operation uses unknown record identity '{recordTypeId}'."));
            return;
        }
        if (!allowEmpty && values.Count == 0) diagnostics.Add(new MirValidationDiagnostic("Record 'with' replacements must not be empty."));
        var seen = new HashSet<MirRecordFieldId>();
        foreach (var value in values)
        {
            if (!seen.Add(value.FieldId)) diagnostics.Add(new MirValidationDiagnostic($"Record operation duplicates field identity '{value.FieldId}'."));
            var field = record.Fields.FirstOrDefault(candidate => candidate.Id == value.FieldId);
            if (field is null) diagnostics.Add(new MirValidationDiagnostic($"Record operation uses unknown field identity '{value.FieldId}'."));
            else if (!MirTypeFacts.AreEquivalent(field.Type, value.Value.Type)) diagnostics.Add(new MirValidationDiagnostic($"Record field value type does not match field '{value.FieldId}'."));
        }
        if (requireComplete)
        {
            foreach (var missing in record.Fields.Where(field => !seen.Contains(field.Id))) diagnostics.Add(new MirValidationDiagnostic($"Record construction is missing field identity '{missing.Id}'."));
        }
    }

    private static void ValidateStatements(IReadOnlyList<MirStatement> statements, List<HandlerScope> activeHandlers, HashSet<MirHandlerId> handlerIds, List<MirValidationDiagnostic> diagnostics)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case MirVariableDeclarationStatement declaration:
                    ValidateExpression(declaration.Initializer, activeHandlers, handlerIds, diagnostics);
                    break;
                case MirExpressionStatement expression:
                    ValidateExpression(expression.Expression, activeHandlers, handlerIds, diagnostics);
                    break;
                case MirReturnStatement { Expression: not null } returnStatement:
                    ValidateExpression(returnStatement.Expression, activeHandlers, handlerIds, diagnostics);
                    break;
                case MirIfStatement conditional:
                    ValidateExpression(conditional.Condition, activeHandlers, handlerIds, diagnostics);
                    ValidateStatements(conditional.ThenStatements, activeHandlers, handlerIds, diagnostics);
                    if (conditional.ElseStatements is not null)
                    {
                        ValidateStatements(conditional.ElseStatements, activeHandlers, handlerIds, diagnostics);
                    }
                    break;
            }
        }
    }

    private static void ValidateExpression(MirExpression expression, List<HandlerScope> activeHandlers, HashSet<MirHandlerId> handlerIds, List<MirValidationDiagnostic> diagnostics)
    {
        switch (expression)
        {
            case MirTryExpression tryExpression:
                ValidateTryExpression(tryExpression, activeHandlers, handlerIds, diagnostics);
                return;
            case MirPropagateExpression propagation:
                ValidatePropagation(propagation, activeHandlers, diagnostics);
                ValidateExpression(propagation.Operand, activeHandlers, handlerIds, diagnostics);
                return;
            case MirAssignmentExpression assignment:
                ValidateExpression(assignment.Expression, activeHandlers, handlerIds, diagnostics);
                return;
            case MirUnaryExpression unary:
                ValidateExpression(unary.Operand, activeHandlers, handlerIds, diagnostics);
                return;
            case MirBinaryExpression binary:
                ValidateExpression(binary.Left, activeHandlers, handlerIds, diagnostics);
                ValidateExpression(binary.Right, activeHandlers, handlerIds, diagnostics);
                return;
            case MirCallExpression call:
                foreach (var argument in call.Arguments) ValidateExpression(argument, activeHandlers, handlerIds, diagnostics);
                return;
            case MirArrayExpression array:
                foreach (var element in array.Elements) ValidateExpression(element, activeHandlers, handlerIds, diagnostics);
                return;
            case MirRecordConstructionExpression construction:
                foreach (var initializer in construction.Initializers) ValidateExpression(initializer.Value, activeHandlers, handlerIds, diagnostics);
                return;
            case MirRecordFieldAccessExpression access:
                ValidateExpression(access.Receiver, activeHandlers, handlerIds, diagnostics);
                return;
            case MirRecordWithExpression withExpression:
                ValidateExpression(withExpression.Source, activeHandlers, handlerIds, diagnostics);
                foreach (var replacement in withExpression.Replacements) ValidateExpression(replacement.Value, activeHandlers, handlerIds, diagnostics);
                return;
            case MirEnumValueExpression value:
                foreach (var argument in value.Arguments) ValidateExpression(argument, activeHandlers, handlerIds, diagnostics);
                return;
            case MirMatchExpression match:
                ValidateExpression(match.Scrutinee, activeHandlers, handlerIds, diagnostics);
                foreach (var arm in match.Arms) ValidateExpression(arm.Expression, activeHandlers, handlerIds, diagnostics);
                return;
            case MirResultMatchExpression resultMatch:
                ValidateExpression(resultMatch.Scrutinee, activeHandlers, handlerIds, diagnostics);
                ValidateExpression(resultMatch.OkExpression, activeHandlers, handlerIds, diagnostics);
                ValidateExpression(resultMatch.ErrExpression, activeHandlers, handlerIds, diagnostics);
                return;
            case MirIfExpression conditional:
                ValidateExpression(conditional.Condition, activeHandlers, handlerIds, diagnostics);
                ValidateExpression(conditional.ThenExpression, activeHandlers, handlerIds, diagnostics);
                ValidateExpression(conditional.ElseExpression, activeHandlers, handlerIds, diagnostics);
                return;
            case MirOkExpression ok:
                ValidateExpression(ok.Payload, activeHandlers, handlerIds, diagnostics);
                return;
            case MirErrExpression err:
                ValidateExpression(err.Payload, activeHandlers, handlerIds, diagnostics);
                return;
            case MirUnwrapExpression unwrap:
                ValidateExpression(unwrap.Operand, activeHandlers, handlerIds, diagnostics);
                return;
        }
    }

    private static void ValidateTryExpression(MirTryExpression tryExpression, List<HandlerScope> activeHandlers, HashSet<MirHandlerId> handlerIds, List<MirValidationDiagnostic> diagnostics)
    {
        if (!handlerIds.Add(tryExpression.HandlerId))
        {
            diagnostics.Add(new MirValidationDiagnostic($"Duplicate try handler identity '{tryExpression.HandlerId}' in one function."));
        }

        var scope = new HandlerScope(tryExpression.HandlerId, tryExpression.HandledErrorType);
        activeHandlers.Add(scope);
        ValidateValueBlock(tryExpression.Protected, activeHandlers, handlerIds, diagnostics);
        activeHandlers.RemoveAt(activeHandlers.Count - 1);

        if (!scope.WasTargeted)
        {
            diagnostics.Add(new MirValidationDiagnostic($"Try handler '{tryExpression.HandlerId}' has no targeted propagation in its protected value block."));
        }

        ValidateValueBlock(tryExpression.Handler, activeHandlers, handlerIds, diagnostics);
    }

    private static void ValidateValueBlock(MirValueBlock block, List<HandlerScope> activeHandlers, HashSet<MirHandlerId> handlerIds, List<MirValidationDiagnostic> diagnostics)
    {
        foreach (var statement in block.PrefixStatements)
        {
            if (statement is not MirVariableDeclarationStatement and not MirExpressionStatement)
            {
                diagnostics.Add(new MirValidationDiagnostic("Try value blocks may contain only variable declarations and expression statements before their final value."));
            }

            ValidateStatements([statement], activeHandlers, handlerIds, diagnostics);
        }

        ValidateExpression(block.ValueExpression, activeHandlers, handlerIds, diagnostics);
    }

    private static void ValidatePropagation(MirPropagateExpression propagation, List<HandlerScope> activeHandlers, List<MirValidationDiagnostic> diagnostics)
    {
        if (propagation.Operand.Type is not MirResultType resultType)
        {
            diagnostics.Add(new MirValidationDiagnostic("Propagation operand must be a Result."));
            return;
        }

        if (propagation.Target is not MirPropagationTarget.LexicalExcept lexical)
        {
            return;
        }

        var scope = activeHandlers.LastOrDefault(handler => handler.HandlerId == lexical.HandlerId);
        if (scope is null)
        {
            diagnostics.Add(new MirValidationDiagnostic($"Lexical propagation target '{lexical.HandlerId}' is dangling, out of scope, or targets its own handler body."));
            return;
        }

        if (!MirTypeFacts.AreEquivalent(scope.ErrorType, resultType.ErrorType))
        {
            diagnostics.Add(new MirValidationDiagnostic($"Lexical propagation target '{lexical.HandlerId}' has incompatible error type '{resultType.ErrorType.Name}'."));
            return;
        }

        scope.WasTargeted = true;
    }

    private static void ValidateFunctionPropagationTargets(
        IReadOnlyList<MirStatement> statements,
        MirType functionReturnType,
        List<MirValidationDiagnostic> diagnostics)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case MirVariableDeclarationStatement declaration:
                    ValidateFunctionPropagationTarget(declaration.Initializer, functionReturnType, diagnostics);
                    break;
                case MirExpressionStatement expression:
                    ValidateFunctionPropagationTarget(expression.Expression, functionReturnType, diagnostics);
                    break;
                case MirReturnStatement { Expression: not null } returnStatement:
                    ValidateFunctionPropagationTarget(returnStatement.Expression, functionReturnType, diagnostics);
                    break;
                case MirIfStatement conditional:
                    ValidateFunctionPropagationTarget(conditional.Condition, functionReturnType, diagnostics);
                    ValidateFunctionPropagationTargets(conditional.ThenStatements, functionReturnType, diagnostics);
                    if (conditional.ElseStatements is not null)
                    {
                        ValidateFunctionPropagationTargets(conditional.ElseStatements, functionReturnType, diagnostics);
                    }
                    break;
            }
        }
    }

    private static void ValidateFunctionPropagationTarget(
        MirExpression expression,
        MirType functionReturnType,
        List<MirValidationDiagnostic> diagnostics)
    {
        switch (expression)
        {
            case MirPropagateExpression propagation:
                if (propagation.Target is MirPropagationTarget.FunctionReturn)
                {
                    if (functionReturnType is not MirResultType functionResult)
                    {
                        diagnostics.Add(new MirValidationDiagnostic("Function-return propagation requires a Result function return type."));
                    }
                    else if (propagation.Operand.Type is MirResultType operandResult
                        && !MirTypeFacts.AreEquivalent(functionResult.ErrorType, operandResult.ErrorType))
                    {
                        diagnostics.Add(new MirValidationDiagnostic(
                            $"Function-return propagation error type '{operandResult.ErrorType.Name}' does not match function Result error type '{functionResult.ErrorType.Name}'."));
                    }
                }

                ValidateFunctionPropagationTarget(propagation.Operand, functionReturnType, diagnostics);
                return;
            case MirTryExpression tryExpression:
                ValidateValueBlockFunctionPropagationTargets(tryExpression.Protected, functionReturnType, diagnostics);
                ValidateValueBlockFunctionPropagationTargets(tryExpression.Handler, functionReturnType, diagnostics);
                return;
            case MirAssignmentExpression assignment:
                ValidateFunctionPropagationTarget(assignment.Expression, functionReturnType, diagnostics);
                return;
            case MirUnaryExpression unary:
                ValidateFunctionPropagationTarget(unary.Operand, functionReturnType, diagnostics);
                return;
            case MirBinaryExpression binary:
                ValidateFunctionPropagationTarget(binary.Left, functionReturnType, diagnostics);
                ValidateFunctionPropagationTarget(binary.Right, functionReturnType, diagnostics);
                return;
            case MirCallExpression call:
                foreach (var argument in call.Arguments)
                {
                    ValidateFunctionPropagationTarget(argument, functionReturnType, diagnostics);
                }
                return;
            case MirArrayExpression array:
                foreach (var element in array.Elements)
                {
                    ValidateFunctionPropagationTarget(element, functionReturnType, diagnostics);
                }
                return;
            case MirRecordConstructionExpression construction:
                foreach (var initializer in construction.Initializers) ValidateFunctionPropagationTarget(initializer.Value, functionReturnType, diagnostics);
                return;
            case MirRecordFieldAccessExpression access:
                ValidateFunctionPropagationTarget(access.Receiver, functionReturnType, diagnostics);
                return;
            case MirRecordWithExpression withExpression:
                ValidateFunctionPropagationTarget(withExpression.Source, functionReturnType, diagnostics);
                foreach (var replacement in withExpression.Replacements) ValidateFunctionPropagationTarget(replacement.Value, functionReturnType, diagnostics);
                return;
            case MirEnumValueExpression value:
                foreach (var argument in value.Arguments)
                {
                    ValidateFunctionPropagationTarget(argument, functionReturnType, diagnostics);
                }
                return;
            case MirMatchExpression match:
                ValidateFunctionPropagationTarget(match.Scrutinee, functionReturnType, diagnostics);
                foreach (var arm in match.Arms)
                {
                    ValidateFunctionPropagationTarget(arm.Expression, functionReturnType, diagnostics);
                }
                return;
            case MirResultMatchExpression resultMatch:
                ValidateFunctionPropagationTarget(resultMatch.Scrutinee, functionReturnType, diagnostics);
                ValidateFunctionPropagationTarget(resultMatch.OkExpression, functionReturnType, diagnostics);
                ValidateFunctionPropagationTarget(resultMatch.ErrExpression, functionReturnType, diagnostics);
                return;
            case MirIfExpression conditional:
                ValidateFunctionPropagationTarget(conditional.Condition, functionReturnType, diagnostics);
                ValidateFunctionPropagationTarget(conditional.ThenExpression, functionReturnType, diagnostics);
                ValidateFunctionPropagationTarget(conditional.ElseExpression, functionReturnType, diagnostics);
                return;
            case MirOkExpression ok:
                ValidateFunctionPropagationTarget(ok.Payload, functionReturnType, diagnostics);
                return;
            case MirErrExpression err:
                ValidateFunctionPropagationTarget(err.Payload, functionReturnType, diagnostics);
                return;
            case MirUnwrapExpression unwrap:
                ValidateFunctionPropagationTarget(unwrap.Operand, functionReturnType, diagnostics);
                return;
        }
    }

    private static void ValidateValueBlockFunctionPropagationTargets(
        MirValueBlock block,
        MirType functionReturnType,
        List<MirValidationDiagnostic> diagnostics)
    {
        ValidateFunctionPropagationTargets(block.PrefixStatements, functionReturnType, diagnostics);
        ValidateFunctionPropagationTarget(block.ValueExpression, functionReturnType, diagnostics);
    }

    private sealed class HandlerScope(MirHandlerId handlerId, MirType errorType)
    {
        public MirHandlerId HandlerId { get; } = handlerId;
        public MirType ErrorType { get; } = errorType;
        public bool WasTargeted { get; set; }
    }
}
