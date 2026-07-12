namespace Aurelian.World.Units;

public static class WorldUnitResolver
{
    public static WorldResolutionResult Resolve(WorldDocument document)
    {
        List<WorldResolutionDiagnostic> diagnostics = [];

        if (!document.Units.ContainsKey(document.RootId))
        {
            diagnostics.Add(new WorldResolutionDiagnostic(
                WorldResolutionDiagnosticCodes.RootUnitMissing,
                WorldResolutionDiagnosticSeverity.Error,
                $"Root unit '{document.RootId}' is not present in the world document.",
                document.RootId));
        }

        ValidateChildren(document, diagnostics);
        ValidateCycles(document, diagnostics);

        if (diagnostics.Any(x => x.Severity == WorldResolutionDiagnosticSeverity.Error))
        {
            return new WorldResolutionResult(null, diagnostics);
        }

        Dictionary<UnitId, ResolvedWorldUnit> units = [];
        Dictionary<UnitId, IReadOnlyList<UnitId>> children = [];
        Dictionary<UnitId, UnitId> parents = [];
        List<UnitId> preOrder = [];
        HashSet<UnitId> visited = [];

        AppendResolvedUnit(document.RootId);

        return new WorldResolutionResult(
            new ResolvedWorld(document.RootId, units, children, parents, preOrder),
            diagnostics);

        void AppendResolvedUnit(UnitId id)
        {
            if (!visited.Add(id))
            {
                return;
            }

            WorldUnitDescriptor descriptor = document.Units[id];
            ResolvedWorldUnit resolved = new(descriptor.Id, descriptor.Kind, descriptor.Logic);
            IReadOnlyList<UnitId> immediateChildren = descriptor.Composition.Children.Select(x => x.UnitId).ToArray();

            units.Add(id, resolved);
            children.Add(id, immediateChildren);
            preOrder.Add(id);

            foreach (UnitId child in immediateChildren)
            {
                parents.TryAdd(child, id);
                AppendResolvedUnit(child);
            }
        }
    }

    private static void ValidateChildren(WorldDocument document, List<WorldResolutionDiagnostic> diagnostics)
    {
        foreach (WorldUnitDescriptor descriptor in document.Units.Values.OrderBy(x => x.Id.Value))
        {
            HashSet<UnitId> seenChildren = [];
            HashSet<string> seenSlots = new(StringComparer.Ordinal);

            foreach (UnitChild child in descriptor.Composition.Children)
            {
                if (!document.Units.ContainsKey(child.UnitId))
                {
                    diagnostics.Add(new WorldResolutionDiagnostic(
                        WorldResolutionDiagnosticCodes.ChildUnitMissing,
                        WorldResolutionDiagnosticSeverity.Error,
                        $"Unit '{descriptor.Id}' declares missing child unit '{child.UnitId}'.",
                        descriptor.Id,
                        child.UnitId));
                }

                if (!seenChildren.Add(child.UnitId))
                {
                    diagnostics.Add(new WorldResolutionDiagnostic(
                        WorldResolutionDiagnosticCodes.DuplicateImmediateChild,
                        WorldResolutionDiagnosticSeverity.Error,
                        $"Unit '{descriptor.Id}' declares child unit '{child.UnitId}' more than once in its immediate composition.",
                        descriptor.Id,
                        child.UnitId));
                }

                if (child.Slot is not null && !seenSlots.Add(child.Slot))
                {
                    diagnostics.Add(new WorldResolutionDiagnostic(
                        WorldResolutionDiagnosticCodes.DuplicateChildSlot,
                        WorldResolutionDiagnosticSeverity.Error,
                        $"Unit '{descriptor.Id}' declares child slot '{child.Slot}' more than once in its immediate composition.",
                        descriptor.Id,
                        child.UnitId));
                }
            }
        }
    }

    private static void ValidateCycles(WorldDocument document, List<WorldResolutionDiagnostic> diagnostics)
    {
        HashSet<UnitId> complete = [];
        Stack<UnitId> path = new();
        HashSet<UnitId> pathSet = [];

        foreach (UnitId id in document.Units.Keys.OrderBy(x => x.Value))
        {
            Visit(id);
        }

        void Visit(UnitId id)
        {
            if (complete.Contains(id))
            {
                return;
            }

            if (!pathSet.Add(id))
            {
                UnitId parent = path.Count > 0 ? path.Peek() : id;
                diagnostics.Add(new WorldResolutionDiagnostic(
                    WorldResolutionDiagnosticCodes.CompositionCycle,
                    WorldResolutionDiagnosticSeverity.Error,
                    $"Composition cycle detected at unit '{id}'.",
                    parent,
                    id));
                return;
            }

            path.Push(id);

            if (document.Units.TryGetValue(id, out WorldUnitDescriptor? descriptor))
            {
                foreach (UnitId child in descriptor.Composition.Children.Select(x => x.UnitId))
                {
                    if (document.Units.ContainsKey(child))
                    {
                        Visit(child);
                    }
                }
            }

            path.Pop();
            pathSet.Remove(id);
            complete.Add(id);
        }
    }
}
