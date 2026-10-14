namespace Copeland.TS.Mir;

public static partial class MirValidator
{
    private static void ValidateTableRecalculation(
        MirTableRecalculationExpression recalculation,
        IReadOnlyDictionary<MirTableId, MirTableDefinition> tables,
        List<MirValidationDiagnostic> diagnostics)
    {
        if (!tables.TryGetValue(recalculation.TableId, out MirTableDefinition? target)
            || target.DerivedPlan is null
            || recalculation.Type is not MirTableType resultType
            || resultType.TableId != target.Id)
        {
            diagnostics.Add(new MirValidationDiagnostic("Recalculation requires a declared derived table and its exact result type."));
            return;
        }
        var leaves = new HashSet<MirTableId>();
        var visited = new HashSet<MirTableId>();
        var active = new HashSet<MirTableId>();
        var expectedSteps = new List<MirTableRecalculationStep>();
        Visit(target.Id);
        MirTableId[] inputIds = recalculation.Inputs.Select(input => input.TableId).ToArray();
        if (inputIds.Distinct().Count() != inputIds.Length || !leaves.SetEquals(inputIds))
        {
            diagnostics.Add(new MirValidationDiagnostic("Recalculation inputs must cover every authored dependency once, with no unrelated or derived inputs."));
        }
        foreach (MirTableRecalculationInput input in recalculation.Inputs)
        {
            if (input.Value.Type is not MirTableType inputType || inputType.TableId != input.TableId)
            {
                diagnostics.Add(new MirValidationDiagnostic("Recalculation input snapshot does not match its nominal table identity."));
            }
        }
        if (!expectedSteps.Select(step => step.TableId).SequenceEqual(recalculation.Steps.Select(step => step.TableId)))
        {
            diagnostics.Add(new MirValidationDiagnostic("Recalculation must realize each derived dependency once in canonical dependency order."));
            return;
        }
        for (int index = 0; index < expectedSteps.Count; index += 1)
        {
            if (!expectedSteps[index].Inputs.SequenceEqual(recalculation.Steps[index].Inputs))
            {
                diagnostics.Add(new MirValidationDiagnostic("Recalculation step inputs must match the existing source and declared joins in order."));
            }
        }

        void Visit(MirTableId id)
        {
            if (active.Contains(id))
            {
                diagnostics.Add(new MirValidationDiagnostic("Recalculation dependency graph contains a cycle."));
                return;
            }
            if (!visited.Add(id))
            {
                return;
            }
            if (!tables.TryGetValue(id, out MirTableDefinition? table))
            {
                diagnostics.Add(new MirValidationDiagnostic("Recalculation references an unknown dependency."));
                return;
            }
            if (table.DerivedPlan is null)
            {
                leaves.Add(id);
                return;
            }
            active.Add(id);
            MirTableId[] dependencies = new[] { table.DerivedPlan.SourceTableId }
                .Concat(table.DerivedPlan.Joins.Select(join => join.JoinedTableId)).ToArray();
            foreach (MirTableId dependency in dependencies)
            {
                Visit(dependency);
            }
            active.Remove(id);
            expectedSteps.Add(new MirTableRecalculationStep(id, dependencies));
        }
    }
}
