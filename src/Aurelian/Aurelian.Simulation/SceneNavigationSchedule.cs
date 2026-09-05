namespace Aurelian.Simulation;

public readonly record struct SimulationSceneId
{
    public SimulationSceneId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Scene identity must not be empty.", nameof(value))
            : value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct SimulationAnchorId
{
    public SimulationAnchorId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Anchor identity must not be empty.", nameof(value))
            : value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct SimulationRouteId
{
    public SimulationRouteId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Route identity must not be empty.", nameof(value))
            : value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct SimulationPoint(long X, long Y)
{
    public long SquaredDistance(SimulationPoint other)
    {
        long x = X - other.X;
        long y = Y - other.Y;
        return checked((x * x) + (y * y));
    }
}

public readonly record struct SimulationBounds(long Width, long Height)
{
    public bool Contains(SimulationPoint point)
    {
        return Width > 0 && Height > 0
            && point.X >= 0 && point.X < Width
            && point.Y >= 0 && point.Y < Height;
    }
}

public readonly record struct SimulationAnchor(
    SimulationAnchorId Id,
    SimulationSceneId Scene,
    SimulationPoint Position,
    long ArrivalRadius = 0);

public sealed record SimulationRoute(
    SimulationRouteId Id,
    SimulationSceneId Source,
    SimulationSceneId Destination,
    SimulationAnchorId DestinationAnchor);

public sealed record SimulationScene(
    SimulationSceneId Id,
    SimulationBounds Bounds,
    IReadOnlyList<SimulationAnchor> Anchors,
    IReadOnlyList<SimulationRoute> Routes,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed class SceneCatalog
{
    private readonly IReadOnlyDictionary<SimulationSceneId, SimulationScene> scenes;
    private readonly IReadOnlyDictionary<SimulationAnchorId, SimulationAnchor> anchors;
    private readonly IReadOnlyDictionary<SimulationRouteId, SimulationRoute> routes;

    public SceneCatalog(IEnumerable<SimulationScene> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        SimulationScene[] materialized = scenes.OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToArray();
        Validate(materialized);
        All = materialized;
        this.scenes = materialized.ToDictionary(item => item.Id);
        anchors = materialized.SelectMany(item => item.Anchors).ToDictionary(item => item.Id);
        routes = materialized.SelectMany(item => item.Routes).ToDictionary(item => item.Id);
    }

    public IReadOnlyList<SimulationScene> All { get; }

    public SimulationScene GetScene(SimulationSceneId id) => scenes.TryGetValue(id, out SimulationScene? scene)
        ? scene
        : throw new KeyNotFoundException($"Unknown scene '{id}'.");

    public bool TryGetAnchor(SimulationAnchorId id, out SimulationAnchor anchor) => anchors.TryGetValue(id, out anchor!);

    public SimulationAnchor GetAnchor(SimulationAnchorId id) => anchors.TryGetValue(id, out SimulationAnchor anchor)
        ? anchor
        : throw new KeyNotFoundException($"Unknown anchor '{id}'.");

    public SimulationRoute GetRoute(SimulationRouteId id) => routes.TryGetValue(id, out SimulationRoute? route)
        ? route
        : throw new KeyNotFoundException($"Unknown route '{id}'.");

    private static void Validate(IReadOnlyList<SimulationScene> scenes)
    {
        RequireUnique(scenes.Select(item => item.Id), "scene");
        RequireUnique(scenes.SelectMany(item => item.Anchors).Select(item => item.Id), "anchor");
        RequireUnique(scenes.SelectMany(item => item.Routes).Select(item => item.Id), "route");
        var sceneIds = scenes.Select(item => item.Id).ToHashSet();
        var anchorIndex = scenes.SelectMany(item => item.Anchors).ToDictionary(item => item.Id);
        foreach (SimulationScene scene in scenes)
        {
            if (scene.Bounds.Width <= 0 || scene.Bounds.Height <= 0)
            {
                throw new InvalidDataException($"Scene '{scene.Id}' has invalid bounds.");
            }
            foreach (SimulationAnchor anchor in scene.Anchors)
            {
                if (anchor.Scene != scene.Id || anchor.ArrivalRadius < 0 || !scene.Bounds.Contains(anchor.Position))
                {
                    throw new InvalidDataException($"Anchor '{anchor.Id}' is invalid for scene '{scene.Id}'.");
                }
            }
            foreach (SimulationRoute route in scene.Routes)
            {
                if (route.Source != scene.Id
                    || !sceneIds.Contains(route.Destination)
                    || !anchorIndex.TryGetValue(route.DestinationAnchor, out SimulationAnchor target)
                    || target.Scene != route.Destination)
                {
                    throw new InvalidDataException($"Route '{route.Id}' has an invalid source or destination anchor.");
                }
            }
        }
    }

    private static void RequireUnique<T>(IEnumerable<T> values, string kind) where T : notnull
    {
        T[] materialized = values.ToArray();
        if (materialized.Distinct().Count() != materialized.Length)
        {
            throw new InvalidDataException($"Simulation {kind} identities must be unique.");
        }
    }
}

public readonly record struct SceneTransition(
    SimulationSceneId Source,
    SimulationRouteId Route,
    SimulationSceneId Destination,
    SimulationAnchorId DestinationAnchor);

public sealed class SceneTransitionBridge
{
    private readonly SceneCatalog catalog;

    public SceneTransitionBridge(SceneCatalog catalog)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public SceneTransition Propose(SimulationSceneId currentScene, SimulationRouteId routeId)
    {
        SimulationRoute route = catalog.GetRoute(routeId);
        if (route.Source != currentScene)
        {
            throw new InvalidOperationException($"Route '{routeId}' does not leave current scene '{currentScene}'.");
        }

        return new SceneTransition(route.Source, route.Id, route.Destination, route.DestinationAnchor);
    }

    public SceneActivationFact CompleteAccepted(
        SceneTransition transition,
        SceneSimulationDetail destinationDetail,
        ISceneResourceScopeHandoff? resources = null,
        ISceneTransitionPresentation? presentation = null)
    {
        SimulationRoute route = catalog.GetRoute(transition.Route);
        if (route.Source != transition.Source
            || route.Destination != transition.Destination
            || route.DestinationAnchor != transition.DestinationAnchor)
        {
            throw new InvalidOperationException("Accepted transition does not match the catalog route.");
        }

        resources?.Leave(transition.Source);
        resources?.Enter(transition.Destination);
        presentation?.CameraSnap(transition.Destination, transition.DestinationAnchor);
        return new SceneActivationFact(transition.Source, transition.Destination, destinationDetail);
    }
}

public interface ISceneResourceScopeHandoff
{
    void Leave(SimulationSceneId scene);
    void Enter(SimulationSceneId scene);
}

public interface ISceneTransitionPresentation
{
    void CameraSnap(SimulationSceneId scene, SimulationAnchorId anchor);
}

public enum SceneSimulationDetail
{
    Detailed,
    Coarse
}

public readonly record struct SceneActivationFact(
    SimulationSceneId? Previous,
    SimulationSceneId Current,
    SceneSimulationDetail Detail);

public enum NavigationRequestKind
{
    Goal,
    Anchor,
    Route
}

public readonly record struct NavigationRequestId(NavigationRequestKind Kind, string Value);

public readonly record struct NavigationGoal(
    NavigationRequestId Request,
    SimulationSceneId Scene,
    SimulationAnchorId Anchor);

public enum NavigationOutcome
{
    Proposed,
    Arrived,
    PathUnavailable,
    Blocked,
    Interrupted,
    ReplanRequested
}

public readonly record struct NavigationFact(NavigationRequestId Request, NavigationOutcome Outcome, string? Detail = null);

public static class NavigationCoordinator
{
    public static NavigationFact PathProposed(NavigationGoal goal)
    {
        return new NavigationFact(goal.Request, NavigationOutcome.Proposed);
    }

    public static NavigationFact ObservePosition(
        NavigationGoal goal,
        SimulationPoint position,
        SimulationAnchor destination)
    {
        if (goal.Scene != destination.Scene || goal.Anchor != destination.Id)
        {
            return new NavigationFact(goal.Request, NavigationOutcome.Interrupted, "goal no longer matches destination");
        }

        long radiusSquared = checked(destination.ArrivalRadius * destination.ArrivalRadius);
        NavigationOutcome outcome = position.SquaredDistance(destination.Position) <= radiusSquared
            ? NavigationOutcome.Arrived
            : NavigationOutcome.Proposed;
        return new NavigationFact(goal.Request, outcome);
    }

    public static NavigationFact MovementRejected(
        NavigationGoal goal,
        int consecutiveRejections,
        int replanThreshold)
    {
        if (consecutiveRejections <= 0 || replanThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(consecutiveRejections));
        }

        NavigationOutcome outcome = consecutiveRejections >= replanThreshold
            ? NavigationOutcome.ReplanRequested
            : NavigationOutcome.Blocked;
        return new NavigationFact(goal.Request, outcome);
    }

    public static NavigationFact PathUnavailable(NavigationGoal goal, string? detail = null)
    {
        return new NavigationFact(goal.Request, NavigationOutcome.PathUnavailable, detail);
    }
}

public sealed record ScheduleWindow<TGoal>(
    string Id,
    long StartInclusive,
    long EndExclusive,
    int Priority,
    TGoal Goal);

public readonly record struct ScheduleMatch<TGoal>(
    string WindowId,
    long SemanticTime,
    int Priority,
    TGoal Goal);

public static class DeterministicSchedule
{
    public static TWindow? Select<TWindow>(
        IEnumerable<TWindow> windows,
        long semanticTime,
        Func<TWindow, long, bool> matches,
        Func<TWindow, string> id,
        Func<TWindow, int> priority)
        where TWindow : class
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(priority);
        if (semanticTime < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(semanticTime));
        }

        TWindow? winner = null;
        foreach (TWindow window in windows)
        {
            if (!matches(window, semanticTime))
            {
                continue;
            }
            if (winner is null || priority(window) > priority(winner))
            {
                winner = window;
                continue;
            }
            if (priority(window) == priority(winner)
                && !StringComparer.Ordinal.Equals(id(window), id(winner)))
            {
                throw new InvalidOperationException(
                    $"Schedule windows '{id(winner)}' and '{id(window)}' tie at semantic time {semanticTime} and priority {priority(window)}.");
            }
        }

        return winner;
    }

    public static ScheduleMatch<TGoal>? Match<TGoal>(
        IEnumerable<ScheduleWindow<TGoal>> windows,
        long semanticTime)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (semanticTime < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(semanticTime));
        }

        ScheduleWindow<TGoal>? winner = null;
        foreach (ScheduleWindow<TGoal> window in windows)
        {
            if (window.StartInclusive < 0 || window.EndExclusive <= window.StartInclusive)
            {
                throw new InvalidDataException($"Schedule window '{window.Id}' has an invalid interval.");
            }
            if (semanticTime < window.StartInclusive || semanticTime >= window.EndExclusive)
            {
                continue;
            }
            if (winner is null || window.Priority > winner.Priority)
            {
                winner = window;
                continue;
            }
            if (window.Priority == winner.Priority && !StringComparer.Ordinal.Equals(window.Id, winner.Id))
            {
                throw new InvalidOperationException(
                    $"Schedule windows '{winner.Id}' and '{window.Id}' tie at semantic time {semanticTime} and priority {window.Priority}.");
            }
        }

        return winner is null
            ? null
            : new ScheduleMatch<TGoal>(winner.Id, semanticTime, winner.Priority, winner.Goal);
    }
}
