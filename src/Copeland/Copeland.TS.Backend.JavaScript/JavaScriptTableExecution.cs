using Copeland.TS.Mir;

namespace Copeland.TS.Backend.JavaScript;

public static partial class JavaScriptBackend
{
    private static string TableRuntimeSymbol(GeneratedNames names, JavaScriptBindingReference binding, string identity)
    {
        if (ModuleFactoryEmission.Value)
        {
            return $"Symbol.for({JavaScriptLiteralWriter.WriteString("copeland.ts.table-runtime.v1:" + identity)})";
        }
        return $"Symbol({JavaScriptLiteralWriter.WriteString(names.SymbolDescription(binding, identity))})";
    }

    private static string SharedTableInstances(string identity)
    {
        return ModuleFactoryEmission.Value
            ? $"__cope_m3_record_registration({JavaScriptLiteralWriter.WriteString(identity)}).instances"
            : "new WeakSet()";
    }

    private static IEnumerable<EmittedLine> Indent(IEnumerable<EmittedLine> lines, int amount)
    {
        return lines.Select(line => new EmittedLine(line.Text, line.Indent + amount));
    }

    private static string MaterializeTableOperand(
        MirExpression expression,
        string purpose,
        List<EmittedLine> prelude,
        MirFunction function,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names,
        bool flowEnabled)
    {
        EmittedExpression emitted = EmitExpression(expression, function, catalog, results, names, flowEnabled);
        prelude.AddRange(emitted.Prelude);
        string temporary = names.NextTemporary(purpose);
        prelude.Add(new EmittedLine($"const {temporary} = {emitted.Value};", 0));
        return temporary;
    }

    private static EmittedExpression EmitTableWithRow(
        MirTableWithRowExpression update,
        MirFunction function,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names,
        bool flowEnabled)
    {
        MirTableDefinition table = catalog.GetTable(update.TableId);
        MirTableColumnDefinition keyColumn = catalog.GetTableColumn(update.KeyColumnId);
        var prelude = new List<EmittedLine>();
        string source = MaterializeTableOperand(update.Source, "edit_source", prelude, function, catalog, results, names, flowEnabled);
        string key = MaterializeTableOperand(update.Key, "edit_key", prelude, function, catalog, results, names, flowEnabled);
        var replacements = new Dictionary<MirTableColumnId, string>();
        foreach (MirTableCellReplacement field in update.Replacements)
        {
            replacements.Add(field.ColumnId, MaterializeTableOperand(field.Value, "edit_value", prelude, function, catalog, results, names, flowEnabled));
        }
        string index = names.NextTemporary("edit_index");
        string cursor = names.NextTemporary("edit_cursor");
        string result = names.NextTemporary("edit_result");
        prelude.Add(new EmittedLine($"let {index} = -1;", 0));
        prelude.Add(new EmittedLine($"for (let {cursor} = 0; {cursor} < {table.RowCount}; {cursor} += 1) {{", 0));
        prelude.Add(new EmittedLine($"if ({source}[{names.TableColumnSlot(keyColumn)}][{names.ColumnReadSlot}]({cursor}).$payload[0] === {key}) {{", 1));
        prelude.Add(new EmittedLine($"{index} = {cursor};", 2));
        prelude.Add(new EmittedLine("break;", 2));
        prelude.Add(new EmittedLine("}", 1));
        prelude.Add(new EmittedLine("}", 0));
        prelude.Add(new EmittedLine($"let {result};", 0));
        prelude.Add(new EmittedLine($"if ({index} < 0) {{", 0));
        EmittedExpression notFound = EmitEnumValueExpression(
            new MirEnumValueExpression("TableKeyError", "NotFound", [], new MirNamedType("TableKeyError")),
            function, catalog, results, names, flowEnabled);
        prelude.AddRange(Indent(notFound.Prelude, 1));
        prelude.Add(new EmittedLine($"{result} = {names.MakeValue}({names.TypeToken(results.Get(update.ResultType))}, \"err\", [{notFound.Value}]);", 1));
        prelude.Add(new EmittedLine("} else {", 0));
        var arrays = new List<string>();
        foreach (MirTableColumnDefinition column in table.Columns)
        {
            if (!replacements.TryGetValue(column.Id, out string? replacement))
            {
                arrays.Add("undefined");
                continue;
            }
            string values = names.NextTemporary("edit_column");
            prelude.Add(new EmittedLine($"const {values} = Array.from({{ length: {table.RowCount} }}, (_, position) => {source}[{names.TableColumnSlot(column)}][{names.ColumnReadSlot}](position).$payload[0]);", 1));
            prelude.Add(new EmittedLine($"{values}[{index}] = {replacement};", 1));
            arrays.Add(values);
        }
        prelude.Add(new EmittedLine($"{result} = {names.MakeValue}({names.TypeToken(results.Get(update.ResultType))}, \"ok\", [{names.TableCreate(table)}({source}, [{string.Join(", ", arrays)}])]);", 1));
        prelude.Add(new EmittedLine("}", 0));
        return new EmittedExpression(prelude, result);
    }

    private static EmittedExpression EmitTableRecalculation(
        MirTableRecalculationExpression recalculation,
        MirFunction function,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names,
        bool flowEnabled)
    {
        var prelude = new List<EmittedLine>();
        var values = new Dictionary<MirTableId, string>();
        foreach (MirTableRecalculationInput input in recalculation.Inputs)
        {
            values.Add(input.TableId, MaterializeTableOperand(input.Value, "recalculate_input", prelude, function, catalog, results, names, flowEnabled));
        }
        foreach (MirTableRecalculationStep step in recalculation.Steps)
        {
            string value = names.NextTemporary("recalculate_step");
            string arguments = string.Join(", ", step.Inputs.Select(id => values[id]));
            prelude.Add(new EmittedLine($"const {value} = {names.TableCreate(catalog.GetTable(step.TableId))}({arguments});", 0));
            values.Add(step.TableId, value);
        }
        return new EmittedExpression(prelude, values[recalculation.TableId]);
    }

    private static IReadOnlyList<string> EmitDerivedColumns(
        JavaScriptTextWriter writer,
        MirTableDefinition table,
        IReadOnlyList<string> inputs,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names)
    {
        MirDerivedTablePlan plan = table.DerivedPlan!;
        var context = new MirFunction("__derived_table", [], new MirNamedType("void"), [], []);
        string[] columns = table.Columns.Select(_ => names.NextTemporary("derived_column").ToString()).ToArray();
        string[] lookups = plan.Joins.Select(_ => names.NextTemporary("derived_lookup").ToString()).ToArray();
        string indexName = names.NextTemporary("derived_index");
        for (int index = 0; index < columns.Length; index++)
        {
            writer.WriteLine($"const {columns[index]} = new Array({table.RowCount});");
        }
        for (int joinIndex = 0; joinIndex < plan.Joins.Count; joinIndex++)
        {
            MirRelationJoin join = plan.Joins[joinIndex];
            MirTableDefinition joined = catalog.GetTable(join.JoinedTableId);
            MirTableColumnDefinition lookup = catalog.GetTableColumn(join.JoinedLookupColumnId);
            string position = names.NextTemporary("lookup_position");
            string lookupKey = names.NextTemporary("lookup_key");
            writer.WriteLine($"const {lookups[joinIndex]} = new Map();");
            writer.WriteLine($"for (let {position} = 0; {position} < {joined.RowCount}; {position} += 1) {{");
            writer.Indent();
            writer.WriteLine($"const {lookupKey} = {inputs[joinIndex + 1]}[{names.TableColumnSlot(lookup)}][{names.ColumnReadSlot}]({position}).$payload[0];");
            writer.WriteLine($"if ({lookups[joinIndex]}.has({lookupKey})) {{");
            writer.Indent();
            writer.WriteLine($"throw new Error({JavaScriptLiteralWriter.WriteString($"Derived join '{join.Alias}' requires unique lookup values.")});");
            writer.Unindent();
            writer.WriteLine("}");
            writer.WriteLine($"{lookups[joinIndex]}.set({lookupKey}, {position});");
            writer.Unindent();
            writer.WriteLine("}");
        }
        writer.WriteLine($"for (let {indexName} = 0; {indexName} < {table.RowCount}; {indexName} += 1) {{");
        writer.Indent();
        MirTableDefinition source = catalog.GetTable(plan.SourceTableId);
        writer.WriteLine($"const {JavaScriptIdentifierEncoder.Encode(plan.SourceAlias)} = {names.TableCreateRow(source)}({inputs[0]}, {indexName});");
        for (int joinIndex = 0; joinIndex < plan.Joins.Count; joinIndex++)
        {
            MirRelationJoin join = plan.Joins[joinIndex];
            MirTableDefinition lookupOwner = join.LookupAlias == plan.SourceAlias
                ? source
                : catalog.GetTable(plan.Joins.Single(candidate => candidate.Alias == join.LookupAlias).JoinedTableId);
            MirTableColumnDefinition lookupColumn = catalog.GetTableColumn(join.LookupColumnId);
            var access = new MirTableRowFieldAccessExpression(
                new MirVariableExpression(join.LookupAlias, new MirTableRowType(lookupOwner.RowTypeId, lookupOwner.Name + ".Row")),
                lookupOwner.RowTypeId, lookupColumn.Id.Value + ".f", lookupColumn.ElementType);
            EmittedExpression key = EmitExpression(access, context, catalog, results, names, false);
            WritePrelude(writer, key.Prelude);
            string rowIndex = names.NextTemporary("joined_row_index");
            writer.WriteLine($"const {rowIndex} = {lookups[joinIndex]}.get({key.Value});");
            writer.WriteLine($"if ({rowIndex} === undefined) {{");
            writer.Indent();
            writer.WriteLine($"throw new Error({JavaScriptLiteralWriter.WriteString($"Derived join '{join.Alias}' could not resolve its declared reference.")});");
            writer.Unindent();
            writer.WriteLine("}");
            MirTableDefinition joined = catalog.GetTable(join.JoinedTableId);
            writer.WriteLine($"const {JavaScriptIdentifierEncoder.Encode(join.Alias)} = {names.TableCreateRow(joined)}({inputs[joinIndex + 1]}, {rowIndex});");
        }
        foreach (MirDerivedTableLocal local in plan.Locals)
        {
            EmittedExpression value = EmitExpression(local.Expression, context, catalog, results, names, false);
            WritePrelude(writer, value.Prelude);
            writer.WriteLine($"const {JavaScriptIdentifierEncoder.Encode(local.RuntimeName)} = {value.Value};");
        }
        foreach (MirDerivedTableColumnPlan projection in plan.Columns)
        {
            int index = table.Columns.ToList().FindIndex(column => column.Id == projection.ColumnId);
            EmittedExpression value = EmitExpression(projection.Expression, context, catalog, results, names, false);
            WritePrelude(writer, value.Prelude);
            writer.WriteLine($"{columns[index]}[{indexName}] = {value.Value};");
        }
        writer.Unindent();
        writer.WriteLine("}");
        return columns;
    }

    private static EmittedExpression EmitTableSummary(
        MirTableSummaryExpression summary,
        MirFunction function,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names,
        bool flowEnabled)
    {
        (MirExpression tableSource, IReadOnlyList<MirExpression> filters) = UnwrapTableSelection(summary.Source);
        MirTableDefinition table = catalog.GetTable(summary.TableId);
        var prelude = new List<EmittedLine>();
        string source = MaterializeTableOperand(tableSource, "summary_source", prelude, function, catalog, results, names, flowEnabled);
        var predicates = new List<string>();
        foreach (MirExpression filter in filters)
        {
            predicates.Add(MaterializeTableOperand(filter, "summary_filter", prelude, function, catalog, results, names, flowEnabled));
        }
        string groups = names.NextTemporary("summary_groups");
        string trie = names.NextTemporary("summary_keys");
        string terminal = names.NextTemporary("summary_terminal");
        string row = names.NextTemporary("summary_row");
        string index = names.NextTemporary("summary_index");
        string group = names.NextTemporary("summary_group");
        prelude.Add(new EmittedLine($"const {groups} = [];", 0));
        if (summary.IsGrouped)
        {
            prelude.Add(new EmittedLine($"const {trie} = new Map();", 0));
            prelude.Add(new EmittedLine($"const {terminal} = Symbol();", 0));
        }
        else
        {
            prelude.Add(new EmittedLine($"{groups}.push([{string.Join(", ", summary.Fields.Select(_ => "0"))}]);", 0));
        }
        prelude.Add(new EmittedLine($"for (let {index} = 0; {index} < {table.RowCount}; {index} += 1) {{", 0));
        prelude.Add(new EmittedLine($"const {row} = {names.TableCreateRow(table)}({source}, {index});", 1));
        foreach (string predicate in predicates)
        {
            string identity = CallableTypeIdentity((MirCallableType)filters[predicates.IndexOf(predicate)].Type);
            prelude.Add(new EmittedLine($"if (!__cope_callable_invoke({predicate}, {JavaScriptLiteralWriter.WriteString(identity)}, [{row}])) continue;", 1));
        }
        var reads = new Dictionary<MirTableColumnId, string>();
        IEnumerable<MirTableColumnId> readColumns = summary.GroupKeys.Select(key => key.ColumnId)
            .Concat(summary.Fields.Where(field => field.Input is not null).Select(field => field.Input!.Value));
        foreach (MirTableColumnId columnId in readColumns.Distinct())
        {
            MirTableColumnDefinition column = catalog.GetTableColumn(columnId);
            string value = names.NextTemporary("summary_cell");
            prelude.Add(new EmittedLine($"const {value} = {source}[{names.TableColumnSlot(column)}][{names.ColumnReadSlot}]({index}).$payload[0];", 1));
            reads.Add(columnId, value);
        }
        if (summary.IsGrouped)
        {
            string cursor = names.NextTemporary("summary_key_cursor");
            string atom = names.NextTemporary("summary_key_atom");
            string atoms = names.NextTemporary("summary_key_atoms");
            prelude.Add(new EmittedLine($"const {atoms} = [];", 1));
            foreach (MirTableSummaryKey key in summary.GroupKeys)
            {
                EmitSummaryKeyAtoms(key.Type, reads[key.ColumnId], atoms, prelude, 1, catalog, names);
            }
            prelude.Add(new EmittedLine($"let {cursor} = {trie};", 1));
            prelude.Add(new EmittedLine($"for (const {atom} of {atoms}) {{", 1));
            prelude.Add(new EmittedLine($"if (!{cursor}.has({atom})) {cursor}.set({atom}, new Map());", 2));
            prelude.Add(new EmittedLine($"{cursor} = {cursor}.get({atom});", 2));
            prelude.Add(new EmittedLine("}", 1));
            prelude.Add(new EmittedLine($"let {group} = {cursor}.get({terminal});", 1));
            prelude.Add(new EmittedLine($"if ({group} === undefined) {{", 1));
            prelude.Add(new EmittedLine($"{group} = {groups}.length;", 2));
            prelude.Add(new EmittedLine($"{cursor}.set({terminal}, {group});", 2));
            string initial = string.Join(", ", summary.Fields.Select(field => field.Aggregate is null ? reads[field.Input!.Value] : "0"));
            prelude.Add(new EmittedLine($"{groups}.push([{initial}]);", 2));
            prelude.Add(new EmittedLine("}", 1));
        }
        else
        {
            prelude.Add(new EmittedLine($"const {group} = 0;", 1));
        }
        for (int fieldIndex = 0; fieldIndex < summary.Fields.Count; fieldIndex++)
        {
            MirTableSummaryField field = summary.Fields[fieldIndex];
            if (field.Aggregate is null) continue;
            string target = $"{groups}[{group}][{fieldIndex}]";
            string value = field.Aggregate == MirTableAggregateKind.Count ? "1" : reads[field.Input!.Value];
            string addition = field.Type.Name == "int" ? $"(({target} + {value}) | 0)" : $"({target} + {value})";
            prelude.Add(new EmittedLine($"{target} = {addition};", 1));
        }
        prelude.Add(new EmittedLine("}", 0));
        string output = names.NextTemporary("summary_output");
        string item = names.NextTemporary("summary_item");
        MirRecordDefinition record = catalog.GetRecord(summary.RecordTypeId);
        string arguments = string.Join(", ", record.Fields.Select((_, fieldIndex) => $"{item}[{fieldIndex}]"));
        if (summary.IsGrouped)
        {
            prelude.Add(new EmittedLine($"const {output} = {groups}.map({item} => {names.RecordConstructor(record)}({arguments}));", 0));
        }
        else
        {
            prelude.Add(new EmittedLine($"const {item} = {groups}[0];", 0));
            prelude.Add(new EmittedLine($"const {output} = {names.RecordConstructor(record)}({arguments});", 0));
        }
        return new EmittedExpression(prelude, output);
    }

    private static void EmitSummaryKeyAtoms(
        MirType type,
        string value,
        string atoms,
        List<EmittedLine> lines,
        int indent,
        EnumCatalog catalog,
        GeneratedNames names)
    {
        if (catalog.TryGetEnum(type.Name, out EnumInfo enumInfo))
        {
            lines.Add(new EmittedLine($"{atoms}.push({value}.$tag);", indent));
            lines.Add(new EmittedLine($"switch ({value}.$tag) {{", indent));
            foreach (MirEnumCase enumCase in enumInfo.Definition.Cases)
            {
                lines.Add(new EmittedLine($"case {JavaScriptLiteralWriter.WriteString(enumCase.Name)}:", indent + 1));
                for (int index = 0; index < enumCase.PayloadFields.Count; index++)
                {
                    string payload = IsProduction(names)
                        ? $"{value}.{ProductionEnumPayloadName(index)}"
                        : $"{value}.$payload[{index}]";
                    EmitSummaryKeyAtoms(enumCase.PayloadFields[index].Type, payload, atoms, lines, indent + 2, catalog, names);
                }
                lines.Add(new EmittedLine("break;", indent + 2));
            }
            lines.Add(new EmittedLine("}", indent));
            return;
        }
        if (type is MirResultType result)
        {
            lines.Add(new EmittedLine($"{atoms}.push({value}.$tag);", indent));
            lines.Add(new EmittedLine($"if ({value}.$tag === \"ok\") {{", indent));
            EmitSummaryKeyAtoms(result.SuccessType, $"{value}.$payload[0]", atoms, lines, indent + 1, catalog, names);
            lines.Add(new EmittedLine("} else {", indent));
            EmitSummaryKeyAtoms(result.ErrorType, $"{value}.$payload[0]", atoms, lines, indent + 1, catalog, names);
            lines.Add(new EmittedLine("}", indent));
            return;
        }
        // CLR enum payload equality delegates to payload equality. Immutable record
        // classes and arrays retain identity; enum and Result payloads are values.
        lines.Add(new EmittedLine($"{atoms}.push({value});", indent));
    }

}
