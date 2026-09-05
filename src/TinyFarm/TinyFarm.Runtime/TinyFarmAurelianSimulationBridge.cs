using Aurelian.Simulation;

namespace TinyFarm.Core;

internal static class TinyFarmAurelianSimulationBridge
{
    public static SceneCatalog Project(TinyFarmSceneCatalog source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new SceneCatalog(source.All.Select(ProjectScene).ToArray());
    }

    public static NavigationRequestId AnchorRequest(SceneAnchorId anchor)
    {
        return new NavigationRequestId(NavigationRequestKind.Anchor, anchor.Value);
    }

    public static NavigationRequestId RouteRequest(SceneRouteId route)
    {
        return new NavigationRequestId(NavigationRequestKind.Route, route.Value);
    }

    private static SimulationScene ProjectScene(SceneDefinition scene)
    {
        SimulationAnchor[] anchors = scene.Anchors
            .Select(anchor => new SimulationAnchor(
                new SimulationAnchorId(anchor.Id.Value),
                new SimulationSceneId(anchor.Scene.Value),
                new SimulationPoint(anchor.Position.XUnits, anchor.Position.YUnits),
                anchor.ArrivalRadiusUnits))
            .ToArray();
        SimulationRoute[] routes = scene.Routes
            .Select(route => new SimulationRoute(
                new SimulationRouteId(route.Id.Value),
                new SimulationSceneId(route.SourceScene.Value),
                new SimulationSceneId(route.TargetScene.Value),
                new SimulationAnchorId(route.TargetAnchor.Value)))
            .ToArray();
        return new SimulationScene(
            new SimulationSceneId(scene.Id.Value),
            new SimulationBounds(
                checked((long)scene.Width * ScenePosition.UnitsPerTile),
                checked((long)scene.Height * ScenePosition.UnitsPerTile)),
            anchors,
            routes);
    }
}
