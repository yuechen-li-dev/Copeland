using Copeland.TS.Mir;

namespace Copeland.TS.Backend.CSharp;

public static partial class CSharpBackend
{
    private static string DerivedCheckParameterName(MirTableDefinition table)
    {
        var plan = table.DerivedPlan!;
        var occupied = plan.Joins.Select(join => CSharpNameMangler.Mangle(join.Alias))
            .Concat(plan.Locals.Select(local => CSharpNameMangler.Mangle(local.RuntimeName)))
            .Append(CSharpNameMangler.Mangle(plan.SourceAlias)).ToHashSet(StringComparer.Ordinal);
        string name = "__cope_check_relations";
        while (occupied.Contains(name)) name += "_";
        return name;
    }

    private static void EmitRelationGuard(CSharpTextWriter writer, string condition, string checkedParameter,
        string relation, bool duplicate, string message)
    {
        writer.WriteLine($"if ({condition})");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"if ({checkedParameter})");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"throw new __CopeRelationException({CSharpLiteralWriter.Write(relation)}, {duplicate.ToString().ToLowerInvariant()}, {CSharpLiteralWriter.Write(message)});");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine($"throw new global::System.InvalidOperationException({CSharpLiteralWriter.Write(message)});");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static string EmitTableRecalculation(
        CSharpTextWriter writer,
        MirTableRecalculationExpression recalculation,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics)
    {
        string prefix = "__cope_recalculate_" + tempIndex++;
        var values = new Dictionary<MirTableId, string>();
        foreach (MirTableRecalculationInput input in recalculation.Inputs)
        {
            string name = prefix + "_input_" + values.Count;
            string value = EmitExpression(writer, input.Value, function, enumNames, ref tempIndex, diagnostics);
            writer.WriteLine($"var {name} = {value};");
            values.Add(input.TableId, name);
        }
        if (recalculation.CheckedResult is not null)
        {
            writer.WriteLine($"{MapType(recalculation.CheckedResult)} {prefix}_checked()");
            writer.WriteLine("{");
            writer.Indent();
            writer.WriteLine("try");
            writer.WriteLine("{");
            writer.Indent();
        }
        for (int index = 0; index < recalculation.Steps.Count; index += 1)
        {
            MirTableRecalculationStep step = recalculation.Steps[index];
            string name = prefix + "_step_" + index;
            string arguments = string.Join(", ", step.Inputs.Select(input => values[input]));
            if (recalculation.CheckedResult is not null) arguments += ", true";
            // Create is the same compiled relation implementation used by the singleton.
            writer.WriteLine($"var {name} = {TableTypeName(step.TableId)}.Create({arguments});");
            values.Add(step.TableId, name);
        }
        if (recalculation.CheckedResult is null)
        {
            return values[recalculation.TableId];
        }
        string type = MapType(recalculation.CheckedResult);
        writer.WriteLine($"return {type}.Ok({values[recalculation.TableId]});");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("catch (__CopeRelationException error)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"if (error.Duplicate) return {type}.Err(new TableRelationError.NonUnique(error.Relation));");
        writer.WriteLine($"return {type}.Err(new TableRelationError.Unresolved(error.Relation));");
        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("}");
        return prefix + "_checked()";
    }
}
