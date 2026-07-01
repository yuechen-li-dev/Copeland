using Aurelian.Actuation.World.Requests;
using Aurelian.World.Stores;
using Aurelian.World.Units;

namespace Aurelian.Actuation.World;

public static class WorldDataActuator
{
    public static WorldActuationResult<WorldDataDocument> Apply(
        WorldDataDocument document,
        SetUnitNameRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        if (!document.World.Units.ContainsKey(request.UnitId))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.UnitNotFound,
                $"Unit '{request.UnitId}' was not found."));
        }

        if (request.Name is null || string.IsNullOrWhiteSpace(request.Name.Value))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.InvalidUnitName,
                $"Unit '{request.UnitId}' cannot be assigned an empty name."));
        }

        return Applied(document.WithNames(document.Names.Set(request.UnitId, request.Name)));
    }

    public static WorldActuationResult<WorldDataDocument> Apply(
        WorldDataDocument document,
        RemoveUnitNameRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        if (!document.World.Units.ContainsKey(request.UnitId))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.UnitNotFound,
                $"Unit '{request.UnitId}' was not found."));
        }

        if (!document.Names.Names.ContainsKey(request.UnitId))
        {
            return NoOp(document, new WorldActuationDiagnostic(
                WorldActuationDiagnosticCodes.UnitNameNotSet,
                WorldActuationDiagnosticSeverity.Info,
                $"Unit '{request.UnitId}' has no name to remove."));
        }

        return Applied(document.WithNames(document.Names.Remove(request.UnitId)));
    }

    public static WorldActuationResult<WorldDataDocument> Apply(
        WorldDataDocument document,
        SetUnitTransform2Request request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        if (!document.World.Units.ContainsKey(request.UnitId))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.UnitNotFound,
                $"Unit '{request.UnitId}' was not found."));
        }

        if (!IsFinite(request.Transform))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.InvalidTransform,
                $"Unit '{request.UnitId}' cannot be assigned a transform with non-finite values."));
        }

        return Applied(document.WithTransforms(document.Transforms.Set(request.UnitId, request.Transform)));
    }

    public static WorldActuationResult<WorldDataDocument> Apply(
        WorldDataDocument document,
        RemoveUnitTransform2Request request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        if (!document.World.Units.ContainsKey(request.UnitId))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.UnitNotFound,
                $"Unit '{request.UnitId}' was not found."));
        }

        if (!document.Transforms.Transforms.ContainsKey(request.UnitId))
        {
            return NoOp(document, new WorldActuationDiagnostic(
                WorldActuationDiagnosticCodes.UnitTransformNotSet,
                WorldActuationDiagnosticSeverity.Info,
                $"Unit '{request.UnitId}' has no transform to remove."));
        }

        return Applied(document.WithTransforms(document.Transforms.Remove(request.UnitId)));
    }

    public static WorldActuationResult<WorldDataDocument> Apply(
        WorldDataDocument document,
        SetRenderable2DRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        if (!document.World.Units.ContainsKey(request.UnitId))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.UnitNotFound,
                $"Unit '{request.UnitId}' was not found."));
        }

        if (string.IsNullOrWhiteSpace(request.Renderable.Mesh.Value))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.InvalidMeshRef,
                $"Unit '{request.UnitId}' cannot be assigned an empty mesh reference."));
        }

        if (string.IsNullOrWhiteSpace(request.Renderable.Material.Value))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.InvalidMaterialRef,
                $"Unit '{request.UnitId}' cannot be assigned an empty material reference."));
        }

        return Applied(document.WithRenderables(document.Renderables.Set(request.UnitId, request.Renderable)));
    }

    public static WorldActuationResult<WorldDataDocument> Apply(
        WorldDataDocument document,
        RemoveRenderable2DRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);

        if (!document.World.Units.ContainsKey(request.UnitId))
        {
            return Rejected(document, Diagnostic(
                WorldActuationDiagnosticCodes.UnitNotFound,
                $"Unit '{request.UnitId}' was not found."));
        }

        if (!document.Renderables.Renderables.ContainsKey(request.UnitId))
        {
            return NoOp(document, new WorldActuationDiagnostic(
                WorldActuationDiagnosticCodes.RenderableMissing,
                WorldActuationDiagnosticSeverity.Info,
                $"Unit '{request.UnitId}' has no renderable to remove."));
        }

        return Applied(document.WithRenderables(document.Renderables.Remove(request.UnitId)));
    }

    private static bool IsFinite(Transform2 transform) =>
        double.IsFinite(transform.X)
        && double.IsFinite(transform.Y)
        && double.IsFinite(transform.RotationRadians)
        && double.IsFinite(transform.ScaleX)
        && double.IsFinite(transform.ScaleY);

    private static WorldActuationResult<WorldDataDocument> Applied(WorldDataDocument document) =>
        new(WorldActuationStatus.Applied, document, []);

    private static WorldActuationResult<WorldDataDocument> NoOp(
        WorldDataDocument document,
        WorldActuationDiagnostic diagnostic) =>
        new(WorldActuationStatus.NoOp, document, [diagnostic]);

    private static WorldActuationResult<WorldDataDocument> Rejected(
        WorldDataDocument document,
        WorldActuationDiagnostic diagnostic) =>
        new(WorldActuationStatus.Rejected, document, [diagnostic]);

    private static WorldActuationDiagnostic Diagnostic(string code, string message) =>
        new(code, WorldActuationDiagnosticSeverity.Error, message);
}
