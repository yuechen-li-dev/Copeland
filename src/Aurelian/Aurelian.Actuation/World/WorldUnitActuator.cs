using Aurelian.Actuation.World.Requests;
using Aurelian.World.Units;

namespace Aurelian.Actuation.World;

public static class WorldUnitActuator
{
    public static WorldActuationResult Apply(WorldDocument document, SpawnUnitRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Unit);

        if (document.Units.ContainsKey(request.Unit.Id))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.UnitAlreadyExists,
                $"Unit '{request.Unit.Id}' already exists."));
        }

        Dictionary<UnitId, WorldUnitDescriptor> units = CloneUnits(document);
        units.Add(request.Unit.Id, request.Unit);

        return ResolveApplied(document, new WorldDocument(document.RootId, units));
    }

    public static WorldActuationResult Apply(WorldDocument document, DestroyUnitRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        if (request.UnitId == document.RootId)
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.CannotDestroyRoot,
                $"Root unit '{request.UnitId}' cannot be destroyed."));
        }

        if (!document.Units.TryGetValue(request.UnitId, out WorldUnitDescriptor? unit))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.UnitNotFound,
                $"Unit '{request.UnitId}' was not found."));
        }

        if (unit.Composition.Children.Count > 0)
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.CannotDestroyUnitWithChildren,
                $"Unit '{request.UnitId}' cannot be destroyed because it has immediate children."));
        }

        Dictionary<UnitId, WorldUnitDescriptor> units = CloneUnits(document);
        units.Remove(request.UnitId);

        foreach (WorldUnitDescriptor descriptor in document.Units.Values.OrderBy(x => x.Id.Value))
        {
            if (descriptor.Id == request.UnitId)
            {
                continue;
            }

            UnitChild[] children = descriptor.Composition.Children
                .Where(x => x.UnitId != request.UnitId)
                .ToArray();

            if (children.Length != descriptor.Composition.Children.Count)
            {
                units[descriptor.Id] = descriptor with { Composition = new UnitComposition(children) };
            }
        }

        return ResolveApplied(document, new WorldDocument(document.RootId, units));
    }

    public static WorldActuationResult Apply(WorldDocument document, AttachChildRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Child);

        if (!document.Units.TryGetValue(request.ParentId, out WorldUnitDescriptor? parent))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.ParentNotFound,
                $"Parent unit '{request.ParentId}' was not found."));
        }

        if (!document.Units.ContainsKey(request.Child.UnitId))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.ChildNotFound,
                $"Child unit '{request.Child.UnitId}' was not found."));
        }

        if (parent.Composition.Children.Any(x => x.UnitId == request.Child.UnitId))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.ChildAlreadyAttached,
                $"Child unit '{request.Child.UnitId}' is already attached to parent unit '{request.ParentId}'."));
        }

        if (request.Child.Slot is not null
            && parent.Composition.Children.Any(x => string.Equals(x.Slot, request.Child.Slot, StringComparison.Ordinal)))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.DuplicateChildSlot,
                $"Parent unit '{request.ParentId}' already has a child in slot '{request.Child.Slot}'."));
        }

        Dictionary<UnitId, WorldUnitDescriptor> units = CloneUnits(document);
        UnitChild[] children = parent.Composition.Children.Concat([request.Child]).ToArray();
        units[request.ParentId] = parent with { Composition = new UnitComposition(children) };

        return ResolveApplied(document, new WorldDocument(document.RootId, units));
    }

    public static WorldActuationResult Apply(WorldDocument document, DetachChildRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        if (!document.Units.TryGetValue(request.ParentId, out WorldUnitDescriptor? parent))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.ParentNotFound,
                $"Parent unit '{request.ParentId}' was not found."));
        }

        if (!parent.Composition.Children.Any(x => x.UnitId == request.ChildId))
        {
            return new WorldActuationResult(
                WorldActuationStatus.NoOp,
                document,
                [new WorldActuationDiagnostic(
                    WorldActuationDiagnosticCodes.ChildNotAttached,
                    WorldActuationDiagnosticSeverity.Info,
                    $"Child unit '{request.ChildId}' is not attached to parent unit '{request.ParentId}'.")]);
        }

        Dictionary<UnitId, WorldUnitDescriptor> units = CloneUnits(document);
        UnitChild[] children = parent.Composition.Children
            .Where(x => x.UnitId != request.ChildId)
            .ToArray();
        units[request.ParentId] = parent with { Composition = new UnitComposition(children) };

        return ResolveApplied(document, new WorldDocument(document.RootId, units));
    }

    public static WorldActuationResult Apply(WorldDocument document, ReplaceUnitDescriptorRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Unit);

        if (!document.Units.ContainsKey(request.Unit.Id))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.UnitNotFound,
                $"Unit '{request.Unit.Id}' was not found."));
        }

        Dictionary<UnitId, WorldUnitDescriptor> units = CloneUnits(document);
        units[request.Unit.Id] = request.Unit;

        return ResolveApplied(document, new WorldDocument(document.RootId, units));
    }

    private static WorldActuationResult ResolveApplied(WorldDocument originalDocument, WorldDocument mutatedDocument)
    {
        WorldResolutionResult resolution = WorldUnitResolver.Resolve(mutatedDocument);
        if (resolution.Success)
        {
            return new WorldActuationResult(WorldActuationStatus.Applied, mutatedDocument, []);
        }

        WorldActuationDiagnostic[] diagnostics =
        [
            Diagnostic(
                WorldActuationDiagnosticCodes.InvalidMutationWouldBreakWorld,
                "Mutation was rejected because the resulting world document did not resolve successfully."),
            .. resolution.Diagnostics
                .OrderBy(x => x.Code, StringComparer.Ordinal)
                .ThenBy(x => x.UnitId?.Value ?? 0)
                .ThenBy(x => x.RelatedUnitId?.Value ?? 0)
                .Select(MapResolverDiagnostic)
        ];

        return new WorldActuationResult(WorldActuationStatus.Rejected, originalDocument, diagnostics);
    }

    private static WorldActuationResult Rejected(WorldDocument document, WorldActuationDiagnostic diagnostic) =>
        new(WorldActuationStatus.Rejected, document, [diagnostic]);

    private static WorldActuationDiagnostic Diagnostic(string code, string message) =>
        new(code, WorldActuationDiagnosticSeverity.Error, message);

    private static WorldActuationDiagnostic MapResolverDiagnostic(WorldResolutionDiagnostic diagnostic) =>
        new(
            diagnostic.Code,
            diagnostic.Severity == WorldResolutionDiagnosticSeverity.Error
                ? WorldActuationDiagnosticSeverity.Error
                : WorldActuationDiagnosticSeverity.Warning,
            diagnostic.Message);

    private static Dictionary<UnitId, WorldUnitDescriptor> CloneUnits(WorldDocument document) =>
        document.Units
            .OrderBy(x => x.Key.Value)
            .ToDictionary(x => x.Key, x => x.Value);
}
