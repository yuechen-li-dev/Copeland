using Copeland.TS.Mir;

namespace Copeland.TS.Backend.CSharp;

public static partial class CSharpBackend
{
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
        for (int index = 0; index < recalculation.Steps.Count; index += 1)
        {
            MirTableRecalculationStep step = recalculation.Steps[index];
            string name = prefix + "_step_" + index;
            string arguments = string.Join(", ", step.Inputs.Select(input => values[input]));
            // Create is the same compiled relation implementation used by the singleton.
            writer.WriteLine($"var {name} = {TableTypeName(step.TableId)}.Create({arguments});");
            values.Add(step.TableId, name);
        }
        return values[recalculation.TableId];
    }
}
