namespace TinyFarm.Core;

public sealed record TinyFarmStepResult(
    TinyFarmState State,
    IReadOnlyList<IntentResult> Results,
    IReadOnlyList<NarrativeLine> Narrative);

public sealed class TinyFarmSession
{
    private readonly TinyFarmResolver resolver;
    private readonly TinyFarmDefinitions? definitions;
    private TinyFarmSceneCatalog Scenes => definitions?.Scenes
        ?? throw new InvalidOperationException("Scene execution requires loaded TinyFarm definitions.");
    private long nextSequence;
    private IReadOnlyList<GameEvent> recentEvents;
    private readonly INavigationPlanner navigationPlanner;
    private readonly TinyFarmNpcSchedule.Runtime scheduleRuntime;
    private readonly Dictionary<ActorId, NpcPathState> npcPaths = [];
    private int navigationPlanCount;
    private int activationCount;
    private int deactivationCount;

    public TinyFarmSession(TinyFarmState state)
        : this(state, TinyFarmDefinitionLoader.Load(), 0, [])
    {
    }

    public TinyFarmSession(TinyFarmState state, TinyFarmDefinitions definitions)
        : this(state, definitions, 0, [])
    {
    }

    public TinyFarmSession(
        TinyFarmState state,
        TinyFarmDefinitions definitions,
        INavigationPlanner navigationPlanner)
        : this(state, definitions, 0, [], navigationPlanner)
    {
    }

    internal TinyFarmSession(
        TinyFarmState state,
        TinyFarmDefinitions? definitions,
        long nextSequence,
        IReadOnlyList<GameEvent> recentEvents,
        INavigationPlanner? navigationPlanner = null)
    {
        State = state.DeepCopy();
        this.definitions = definitions;
        this.nextSequence = nextSequence;
        this.recentEvents = recentEvents.ToArray();
        resolver = new TinyFarmResolver(definitions);
        this.navigationPlanner = navigationPlanner ?? new DotRecastNavigationPlanner();
        scheduleRuntime = TinyFarmNpcSchedule.CreateRuntime(definitions!.Schedules);
    }

    public TinyFarmState State { get; private set; }

    public long NextSequence => nextSequence;

    public IReadOnlyList<GameEvent> RecentEvents => recentEvents;
    public SceneId? ActiveScene => State.CurrentScene;
    public TinyFarmSceneCatalog SceneCatalog => Scenes;
    public TinyFarmScheduleCatalog ScheduleCatalog => definitions?.Schedules
        ?? throw new InvalidOperationException("Schedule inspection requires loaded TinyFarm definitions.");
    public int NavigationPlanCount => navigationPlanCount;
    public int ActivationCount => activationCount;
    public int DeactivationCount => deactivationCount;

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
            observationMinute,
            Scenes,
            definitions!.Schedules,
            scheduleRuntime);
        envelopes.AddRange(npcIntents);
        nextSequence += npcIntents.Count;

        IReadOnlyDictionary<ActorId, SceneAnchorId> semanticTargets = envelopes
            .Where(envelope => envelope.Intent is NavigateToAnchorIntent)
            .ToDictionary(
                envelope => envelope.Actor,
                envelope => ((NavigateToAnchorIntent)envelope.Intent).Anchor);
        envelopes = envelopes.Select(TranslateSemanticNavigationIntent).ToList();

        SceneId? activeSceneBefore = State.CurrentScene;
        ResolutionBatchResult batch = resolver.Resolve(State, envelopes);
        batch = AppendAnchorArrivalEvents(State, batch, semanticTargets);
        foreach (IntentResult result in batch.Results.Where(result =>
                     result.Envelope.Source == IntentSourceKind.Dominatus
                     && result.Envelope.Intent is SpatialMoveIntent
                     && result.Reason == IntentReason.MovementBlocked))
        {
            npcPaths.Remove(result.Envelope.Actor);
        }
        State = batch.State;
        SceneId? activeSceneAfter = State.CurrentScene;
        if (activeSceneAfter != activeSceneBefore)
        {
            npcPaths.Clear();
            deactivationCount++;
            activationCount++;
        }
        recentEvents = batch.Results.SelectMany(result => result.Events).ToArray();
        IReadOnlyList<NarrativeLine> narrative = TinyFarmNarrative.Project(recentEvents);
        return new TinyFarmStepResult(State.DeepCopy(), batch.Results, narrative);
    }

    private ResolutionBatchResult AppendAnchorArrivalEvents(
        TinyFarmState before,
        ResolutionBatchResult batch,
        IReadOnlyDictionary<ActorId, SceneAnchorId> semanticTargets)
    {
        IntentResult[] results = batch.Results.Select(result =>
        {
            if (result.Status != IntentResultStatus.Accepted
                || result.Envelope.Intent is not SpatialMoveIntent
                || !semanticTargets.TryGetValue(result.Envelope.Actor, out SceneAnchorId anchorId)
                || !Scenes.TryGetAnchor(anchorId, out SceneAnchorDefinition anchor))
            {
                return result;
            }

            ActorSceneState oldPlacement = before.ActorScene(result.Envelope.Actor);
            ActorSceneState newPlacement = batch.State.ActorScene(result.Envelope.Actor);
            long radiusSquared = (long)anchor.ArrivalRadiusUnits * anchor.ArrivalRadiusUnits;
            bool wasReached = oldPlacement.Scene == anchor.Scene
                && oldPlacement.WorldPosition.SquaredDistance(anchor.Position) <= radiusSquared;
            bool isReached = newPlacement.Scene == anchor.Scene
                && newPlacement.WorldPosition.SquaredDistance(anchor.Position) <= radiusSquared;
            if (wasReached || !isReached)
            {
                return result;
            }

            GameEvent arrival = new(
                GameEventKind.AnchorReached,
                result.Envelope.Actor,
                Location: anchor.SemanticLocation,
                Scene: anchor.Scene,
                SceneObject: anchor.SemanticObject,
                Anchor: anchor.Id);
            return result with { Events = result.Events.Append(arrival).ToArray() };
        }).ToArray();
        return new ResolutionBatchResult(batch.State, results);
    }

    private IntentEnvelope TranslateSemanticNavigationIntent(IntentEnvelope envelope)
    {
        if (State.Version < TinyFarmState.ContinuousSceneSaveVersion
            || envelope.Intent is not NavigateToAnchorIntent move)
        {
            return envelope;
        }

        if (!Scenes.TryGetAnchor(move.Anchor, out SceneAnchorDefinition anchor))
        {
            return envelope;
        }

        ActorSceneState player = State.ActorScene(TinyFarmIds.Player);
        ActorSceneState actor = State.ActorScene(envelope.Actor);
        if (actor.Scene != player.Scene)
        {
            return envelope;
        }

        SceneDefinition scene = Scenes.Get(actor.Scene);
        SceneId destinationScene = anchor.Scene;
        SceneObjectId? portal = null;
        ScenePosition goal;
        string goalIdentity;
        if (destinationScene == actor.Scene)
        {
            goal = anchor.Position;
            goalIdentity = $"anchor:{move.Anchor.Value}";
            long radiusSquared = (long)anchor.ArrivalRadiusUnits * anchor.ArrivalRadiusUnits;
            if (actor.WorldPosition.SquaredDistance(goal) <= radiusSquared)
            {
                npcPaths.Remove(actor.Actor);
                return envelope with { Intent = new AnchorReachedIntent(move.Anchor) };
            }
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
            InteractionTarget? target = TinyFarmSpatialQueries.SelectInteractionTarget(State, actor.Actor, Scenes);
            if (target?.SceneObject == portal)
            {
                npcPaths.Remove(actor.Actor);
                return envelope with { Intent = new InteractIntent(portal) };
            }
        }

        NpcPathState pathState = GetOrPlan(actor, scene, goal, goalIdentity);
        if (!pathState.Path.Succeeded)
        {
            return envelope with
            {
                Intent = new AnchorNavigationFailedIntent(
                    move.Anchor,
                    $"{pathState.Path.Failure}:{pathState.Path.FailureDetail}")
            };
        }
        if (pathState.Index >= pathState.Path.Waypoints.Count)
        {
            return envelope with { Intent = new AnchorReachedIntent(move.Anchor) };
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
        navigationPlanCount++;
        var created = new NpcPathState(actor.Scene, goalIdentity, path, path.Waypoints.Count > 1 ? 1 : 0);
        npcPaths[actor.Actor] = created;
        return created;
    }

    private SceneRoute? FirstRouteToward(SceneId source, SceneId destination)
    {
        var queue = new Queue<(SceneId Scene, SceneRoute? First)>();
        var visited = new HashSet<SceneId> { source };
        queue.Enqueue((source, null));
        while (queue.Count > 0)
        {
            (SceneId sceneId, SceneRoute? first) = queue.Dequeue();
            foreach (SceneRoute route in Scenes.Get(sceneId).Routes.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
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
