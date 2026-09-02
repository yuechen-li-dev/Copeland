namespace TinyFarm.Core;

public sealed record TinyFarmStepResult(
    TinyFarmState State,
    IReadOnlyList<IntentResult> Results,
    IReadOnlyList<NarrativeLine> Narrative);

public sealed class TinyFarmSession
{
    private readonly TinyFarmResolver resolver;
    private readonly TinyFarmDefinitions? definitions;
    private long nextSequence;
    private IReadOnlyList<GameEvent> recentEvents;
    private readonly INavigationPlanner navigationPlanner;
    private readonly Dictionary<ActorId, NpcPathState> npcPaths = [];

    public TinyFarmSession(TinyFarmState state)
        : this(state, null, 0, [])
    {
    }

    public TinyFarmSession(TinyFarmState state, TinyFarmDefinitions definitions)
        : this(state, definitions, 0, [])
    {
    }

    internal TinyFarmSession(
        TinyFarmState state,
        TinyFarmDefinitions? definitions,
        long nextSequence,
        IReadOnlyList<GameEvent> recentEvents)
    {
        State = state.DeepCopy();
        this.definitions = definitions;
        this.nextSequence = nextSequence;
        this.recentEvents = recentEvents.ToArray();
        resolver = new TinyFarmResolver(definitions);
        navigationPlanner = new DotRecastNavigationPlanner();
    }

    public TinyFarmState State { get; private set; }

    public long NextSequence => nextSequence;

    public IReadOnlyList<GameEvent> RecentEvents => recentEvents;

    public TinyFarmStepResult Step(GameIntent humanIntent)
    {
        ArgumentNullException.ThrowIfNull(humanIntent);

        int observationMinute = State.Minute;
        if (humanIntent is WaitIntent wait && wait.Minutes > 0 && wait.Minutes <= 240)
        {
            observationMinute += wait.Minutes;
        }

        var envelopes = new List<IntentEnvelope>
        {
            new(
                TinyFarmIds.Player,
                humanIntent,
                State.Minute,
                nextSequence++,
                IntentSourceKind.Human)
        };

        IReadOnlyList<IntentEnvelope> npcIntents = TinyFarmNpcController.ObserveDecideAndSubmit(
            State,
            recentEvents,
            nextSequence,
            observationMinute);
        envelopes.AddRange(npcIntents.Select(TranslateVisibleNpcIntent));
        nextSequence += npcIntents.Count;

        ResolutionBatchResult batch = resolver.Resolve(State, envelopes);
        foreach (IntentResult result in batch.Results.Where(result =>
                     result.Envelope.Source == IntentSourceKind.Dominatus
                     && result.Envelope.Intent is SpatialMoveIntent
                     && result.Reason == IntentReason.MovementBlocked))
        {
            npcPaths.Remove(result.Envelope.Actor);
        }
        State = batch.State;
        recentEvents = batch.Results.SelectMany(result => result.Events).ToArray();
        IReadOnlyList<NarrativeLine> narrative = TinyFarmNarrative.Project(recentEvents);
        return new TinyFarmStepResult(State.DeepCopy(), batch.Results, narrative);
    }

    private IntentEnvelope TranslateVisibleNpcIntent(IntentEnvelope envelope)
    {
        if (State.Version < TinyFarmState.ContinuousSceneSaveVersion
            || envelope.Intent is not MoveIntent move)
        {
            return envelope;
        }

        ActorSceneState player = State.ActorScene(TinyFarmIds.Player);
        ActorSceneState actor = State.ActorScene(envelope.Actor);
        if (actor.Scene != player.Scene)
        {
            return envelope;
        }

        SceneDefinition scene = TinyFarmScenes.Get(actor.Scene);
        SceneId destinationScene = TinyFarmScenes.SceneForLocation(move.Destination);
        SceneObjectId? portal = null;
        ScenePosition goal;
        string goalIdentity;
        if (destinationScene == actor.Scene)
        {
            goal = GoalForLocation(move.Destination);
            goalIdentity = $"location:{move.Destination.Value}";
        }
        else
        {
            SceneRoute? route = FirstRouteToward(actor.Scene, destinationScene);
            if (route is null)
            {
                return envelope with { Intent = new LookIntent() };
            }
            portal = route.TriggerObject;
            goal = Center(scene.Placement(route.TriggerObject));
            goalIdentity = $"route:{route.Id.Value}";
            InteractionTarget? target = TinyFarmSpatialQueries.SelectInteractionTarget(State, actor.Actor);
            if (target?.SceneObject == portal)
            {
                npcPaths.Remove(actor.Actor);
                return envelope with { Intent = new InteractIntent(portal) };
            }
        }

        NpcPathState pathState = GetOrPlan(actor, scene, goal, goalIdentity);
        if (!pathState.Path.Succeeded || pathState.Index >= pathState.Path.Waypoints.Count)
        {
            return envelope with { Intent = new LookIntent() };
        }

        ScenePosition waypoint = pathState.Path.Waypoints[pathState.Index];
        if (actor.WorldPosition.SquaredDistance(waypoint) <= 64L * 64L)
        {
            pathState = pathState with { Index = pathState.Index + 1 };
            npcPaths[actor.Actor] = pathState;
            if (pathState.Index >= pathState.Path.Waypoints.Count)
            {
                return envelope with { Intent = new LookIntent() };
            }
            waypoint = pathState.Path.Waypoints[pathState.Index];
        }

        int deltaX = waypoint.XUnits - actor.WorldPosition.XUnits;
        int deltaY = waypoint.YUnits - actor.WorldPosition.YUnits;
        int stepX = 0;
        int stepY = 0;
        if (Math.Abs(deltaX) >= Math.Abs(deltaY))
        {
            stepX = Math.Sign(deltaX);
        }
        else
        {
            stepY = Math.Sign(deltaY);
        }
        int distance = Math.Min(ScenePosition.UnitsPerTile / 8, Math.Max(Math.Abs(deltaX), Math.Abs(deltaY)));
        return distance == 0
            ? envelope with { Intent = new LookIntent() }
            : envelope with { Intent = new SpatialMoveIntent(stepX, stepY, distance) };
    }

    private NpcPathState GetOrPlan(
        ActorSceneState actor,
        SceneDefinition scene,
        ScenePosition goal,
        string goalIdentity)
    {
        if (npcPaths.TryGetValue(actor.Actor, out NpcPathState? cached)
            && cached.Scene == actor.Scene
            && cached.GoalIdentity == goalIdentity)
        {
            return cached;
        }

        NavigationPath path = navigationPlanner.FindPath(scene, actor.WorldPosition, goal);
        var created = new NpcPathState(actor.Scene, goalIdentity, path, path.Waypoints.Count > 1 ? 1 : 0);
        npcPaths[actor.Actor] = created;
        return created;
    }

    private static SceneRoute? FirstRouteToward(SceneId source, SceneId destination)
    {
        var queue = new Queue<(SceneId Scene, SceneRoute? First)>();
        var visited = new HashSet<SceneId> { source };
        queue.Enqueue((source, null));
        while (queue.Count > 0)
        {
            (SceneId sceneId, SceneRoute? first) = queue.Dequeue();
            foreach (SceneRoute route in TinyFarmScenes.Get(sceneId).Routes.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                SceneRoute firstRoute = first ?? route;
                if (route.TargetScene == destination)
                {
                    return firstRoute;
                }
                if (visited.Add(route.TargetScene))
                {
                    queue.Enqueue((route.TargetScene, firstRoute));
                }
            }
        }
        return null;
    }

    private static ScenePosition GoalForLocation(LocationId location)
    {
        if (location == TinyFarmIds.Farmhouse)
        {
            return ScenePosition.FromGrid(new GridPosition(4, 7));
        }
        if (location == TinyFarmIds.GeneralStore)
        {
            return ScenePosition.FromGrid(new GridPosition(5, 3));
        }
        if (location == TinyFarmIds.Riverside)
        {
            return ScenePosition.FromGrid(new GridPosition(5, 5));
        }
        return ScenePosition.FromGrid(new GridPosition(12, 7));
    }

    private static ScenePosition Center(SceneLayoutRow row)
    {
        return new ScenePosition(
            (row.X * ScenePosition.UnitsPerTile) + (row.Width * ScenePosition.UnitsPerTile / 2),
            (row.Y * ScenePosition.UnitsPerTile) + (row.Height * ScenePosition.UnitsPerTile / 2));
    }

    private sealed record NpcPathState(
        SceneId Scene,
        string GoalIdentity,
        NavigationPath Path,
        int Index);

    public TinyFarmSave CaptureSave()
    {
        return new TinyFarmSave(
            "tiny-farm-m1@1",
            State.DeepCopy(),
            new TinyFarmRuntimeSave(nextSequence, recentEvents.ToList()),
            new TinyFarmAgentSave("dominatus-1.0.0", "schedule decisions are observation-pure"),
            new TinyFarmNarrativeSave("ariadne-1.0.0", "surface prose is derived from semantic dialogue topics"));
    }

    public byte[] CaptureWeekSave()
    {
        if (definitions is null)
        {
            throw new InvalidOperationException("M2 chunked saves require the loaded definition set.");
        }

        return TinyFarmChunkedSaveCodec.Write(this, definitions);
    }
}
