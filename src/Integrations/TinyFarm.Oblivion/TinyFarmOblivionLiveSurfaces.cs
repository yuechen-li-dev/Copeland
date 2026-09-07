using Aurelian.Field2D;
using Aurelian.Spatial2D;
using Oblivion.Model;
using TinyFarm.Core;

namespace TinyFarm.Oblivion;

public sealed class TinyFarmOblivionLiveSurfaces
{
    private readonly TinyFarmSimulationHost host;

    public TinyFarmOblivionLiveSurfaces(TinyFarmSimulationHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public IReadOnlyList<string> RegisteredSurfaceIds { get; } =
    [
        "tinyfarm.live.field",
        "tinyfarm.live.combat",
        "tinyfarm.spatial.m24"
    ];

    public OblivionWorkspace Capture()
    {
        var workspaceId = new OblivionWorkspaceId("tinyfarm.live");
        var pageId = new OblivionPageId("tinyfarm.runtime");
        OblivionCard field = FieldCard(workspaceId, pageId);
        OblivionCard combat = CombatCard(workspaceId, pageId);
        OblivionCard spatial = SpatialCard(workspaceId, pageId);
        var page = new OblivionWorkspacePage(
            pageId,
            "TinyFarm live runtime",
            "Read-only semantic surfaces captured from the running TinyFarm session.",
            ["live", "read-only", "TinyFarm"],
            [field, combat, spatial]);
        return new OblivionWorkspace(
            workspaceId,
            "TinyFarm live",
            pageId,
            [new OblivionWorkspaceSection("runtime", "Runtime", [page])]);
    }

    private static OblivionCard SpatialCard(OblivionWorkspaceId workspaceId, OblivionPageId pageId)
    {
        SemanticWorldScene scene = TinyFarmSemanticSpatialScene.Create();
        CompiledSemanticWorld compiled = TinyFarmSemanticSpatialScene.Compile();
        string body = string.Join('\n',
        [
            $"semantic-scene: {scene.Id}",
            $"surfaces: {string.Join(", ", scene.Surfaces.Select(item => item.Id))}",
            $"paths: {string.Join(", ", scene.Paths.Select(item => item.Id))}",
            $"patches: {string.Join(", ", scene.Patches.Select(item => item.Id))}",
            $"objects: {string.Join(", ", scene.Objects.Select(item => item.Id))}",
            $"collision-shapes: {compiled.Collision.Count}",
            $"interaction-regions: {compiled.Interactions.Count}",
            $"occlusion-shapes: {compiled.Occlusion.Count}",
            $"navigation-cells: {compiled.Navigation.Count}",
            $"presentation-recipes: {string.Join(", ", scene.PresentationRecipes.Select(item => item.Id + "=" + item.AssetId))}",
            "toggles: surfaces, paths, object-footprints, collision, navigation, occlusion, presentation",
            "authority: semantic intent -> navigation/collision/occlusion/interaction/presentation",
        ]);
        return Card(
            "tinyfarm.spatial.m24",
            "TinyFarm M24 semantic spatial scene",
            OblivionCardStatus.Passing,
            ["spatial", "surfaces", "paths", "collision", "navigation", "occlusion", "presentation"],
            body,
            workspaceId,
            pageId);
    }

    private OblivionCard FieldCard(OblivionWorkspaceId workspaceId, OblivionPageId pageId)
    {
        TinyFarmFieldRuntime field = host.Session.Field;
        ReactiveFluidSnapshot snapshot = field.Fluid.Snapshot();
        int wetCells = snapshot.LiquidMask.Count(value => value != 0);
        string body = string.Join('\n',
        [
            $"owner: {field.Definition.SemanticOwner}",
            $"concept: {TinyFarmFieldRuntime.ChargeConceptPath}",
            $"definition: {field.Definition.Id}",
            $"dimensions: {snapshot.Width} x {snapshot.Height}",
            $"cell-size: {snapshot.CellSize:R}",
            $"tick: {snapshot.Tick}",
            $"hash: {field.SemanticHash}",
            $"snapshot-version: {TinyFarmFieldRuntime.SnapshotVersion}",
            $"wet-cells: {wetCells}",
            Range("height", snapshot.HeightField),
            Range("velocity", snapshot.Velocity),
            Range("foam", snapshot.Foam),
            Range("charge", snapshot.Charge),
            Range("flow-x", snapshot.FlowX),
            Range("flow-y", snapshot.FlowY),
            "presets: normal, charge-only, wave-height, liquid-topology, flow",
            "recent-disturbances:",
            .. field.RecentEvents.Select(item =>
                $"  {item.Tick}: {item.Kind} {item.Shape?.ToString() ?? "contact"} "
                + $"({item.X:R},{item.Y:R}) strength={item.Strength:R} cells={item.AffectedCells}")
        ]);
        return Card(
            "tinyfarm.live.field",
            "TinyFarm live pond field",
            field.ChargedWaterContactCount > 0 ? OblivionCardStatus.Warning : OblivionCardStatus.Passing,
            ["height", "velocity", "foam", "charge", "liquid-mask", "flow-x", "flow-y", "read-only"],
            body,
            workspaceId,
            pageId);
    }

    private OblivionCard CombatCard(OblivionWorkspaceId workspaceId, OblivionPageId pageId)
    {
        TinyFarmCombatInspection combat = host.Session.CombatInspection;
        string body = string.Join('\n',
        [
            $"concept: {combat.ConceptPath}",
            $"active-move: {combat.ActiveMove ?? "none"}",
            $"phase: {combat.Phase}",
            $"phase-tick: {combat.PhaseTick}",
            $"facing: {combat.Facing}",
            $"hit-ids: {string.Join(", ", combat.HitIds)}",
            $"contact-count: {combat.ContactCount}",
            "save-law: TinyFarm attacks resolve atomically at a safe boundary"
        ]);
        return Card(
            "tinyfarm.live.combat",
            "TinyFarm live combat",
            OblivionCardStatus.Passing,
            ["combat", "Dominatus", "read-only"],
            body,
            workspaceId,
            pageId);
    }

    private static OblivionCard Card(
        string id,
        string title,
        OblivionCardStatus status,
        IReadOnlyList<string> tags,
        string body,
        OblivionWorkspaceId workspaceId,
        OblivionPageId pageId)
    {
        return new OblivionCard(
            new OblivionCardId(id),
            OblivionCardKind.Status,
            status,
            title,
            "Live session projection",
            tags,
            new OblivionCardBody(OblivionCardBodyFormat.Plain, new OblivionPlainTextContent(body)),
            [],
            [],
            new OblivionProvenance(OblivionProvenanceSourceKind.Generated, id),
            pageId,
            workspaceId);
    }

    private static string Range(string name, float[] values)
    {
        return $"{name}: min={values.Min():R} max={values.Max():R}";
    }
}
