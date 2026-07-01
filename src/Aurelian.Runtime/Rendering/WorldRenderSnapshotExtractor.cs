using Aurelian.Rendering.Contracts.Snapshots;
using Aurelian.World.Stores;
using Aurelian.World.Units;

namespace Aurelian.Runtime.Rendering;

public static class WorldRenderSnapshotExtractor
{
    public static RenderSnapshotResult Extract(
        WorldDataDocument document,
        WorldRenderSnapshotOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        WorldResolutionResult resolution = WorldUnitResolver.Resolve(document.World);
        if (!resolution.Success || resolution.World is null)
        {
            RenderSnapshot rejectedSnapshot = new(options.FrameId, [], []);
            RenderSnapshotDiagnostic[] diagnostics = resolution.Diagnostics
                .Where(x => x.Severity == WorldResolutionDiagnosticSeverity.Error)
                .OrderBy(x => x.Code, StringComparer.Ordinal)
                .ThenBy(x => x.UnitId?.Value ?? 0)
                .Select(x => new RenderSnapshotDiagnostic(
                    WorldRenderSnapshotDiagnosticCodes.WorldResolutionFailed,
                    RenderSnapshotDiagnosticSeverity.Error,
                    $"World resolution failed: {x.Code} {x.Message}"))
                .DefaultIfEmpty(new RenderSnapshotDiagnostic(
                    WorldRenderSnapshotDiagnosticCodes.WorldResolutionFailed,
                    RenderSnapshotDiagnosticSeverity.Error,
                    "World resolution failed."))
                .ToArray();

            return new RenderSnapshotResult(RenderSnapshotStatus.Rejected, rejectedSnapshot, diagnostics);
        }

        WorldDataSnapshot worldSnapshot = WorldDataSnapshotBuilder.Create(document);
        RenderCamera2D camera = new(
            options.DefaultCameraId,
            RenderTransform2.Identity,
            options.DefaultCameraWidth,
            options.DefaultCameraHeight);

        RenderItem2D[] items = worldSnapshot.Units
            .Where(x => x.Renderable is { Visible: true })
            .Select(ToRenderItem)
            .ToArray();

        RenderSnapshot snapshot = new(options.FrameId, [camera], items);
        if (items.Length == 0)
        {
            return new RenderSnapshotResult(
                RenderSnapshotStatus.Empty,
                snapshot,
                [new RenderSnapshotDiagnostic(
                    WorldRenderSnapshotDiagnosticCodes.NoRenderableUnits,
                    RenderSnapshotDiagnosticSeverity.Info,
                    "World snapshot contains no visible renderable units.")]);
        }

        return new RenderSnapshotResult(RenderSnapshotStatus.Ready, snapshot, []);
    }

    private static RenderItem2D ToRenderItem(UnitDataSnapshot unit)
    {
        Renderable2DData renderable = unit.Renderable!;
        return new RenderItem2D(
            unit.Id.ToString(),
            ToRenderTransform(unit.Transform),
            new RenderMeshRef(renderable.Mesh.Value),
            new RenderMaterialRef(renderable.Material.Value),
            renderable.SortOrder);
    }

    private static RenderTransform2 ToRenderTransform(Transform2 transform) =>
        new(
            transform.X,
            transform.Y,
            transform.RotationRadians,
            transform.ScaleX,
            transform.ScaleY);
}
