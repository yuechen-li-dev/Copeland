using Aurelian.Simulation;

namespace TinyFarm.Core;

public sealed record TinyFarmStepResult(
    TinyFarmState State,
    IReadOnlyList<IntentResult> Results,
    IReadOnlyList<NarrativeLine> Narrative);

public sealed class TinyFarmSession
{
    public const int NpcDistancePerLocomotionStep = ScenePosition.UnitsPerTile / 64;
    private readonly TinyFarmResolver resolver;
    private readonly TinyFarmDefinitions? definitions;
    private TinyFarmSceneCatalog Scenes => definitions?.Scenes
        ?? throw new InvalidOperationException("Scene execution requires loaded TinyFarm definitions.");
    private long nextSequence;
    private IReadOnlyList<GameEvent> recentEvents;
    private readonly INavigationPlanner navigationPlanner;
    private readonly TinyFarmNpcSchedule.Runtime scheduleRuntime;
    private readonly SceneCatalog simulationScenes;
    private readonly Dictionary<ActorId, NpcPathState> npcPaths = [];
    private readonly Dictionary<ActorId, SceneAnchorId> npcNavigationTargets = [];
    private readonly List<KeyValuePair<ActorId, SceneAnchorId>> activeNpcMovementOrder = [];
    private int navigationPlanCount;
    private int activationCount;
    private int deactivationCount;
    private long decisionEvaluationCount;
    private long npcLocomotionReductionCount;
    private long anchorArrivalCount;
    private bool fixedNpcLocomotionEnabled;

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
        simulationScenes = TinyFarmAurelianSimulationBridge.Project(definitions.Scenes);
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
    public long DecisionEvaluationCount => decisionEvaluationCount;
    public long NpcLocomotionReductionCount => npcLocomotionReductionCount;
    public long AnchorArrivalCount => anchorArrivalCount;
    public bool HasActiveNpcNavigation => npcNavigationTargets.Count > 0;

    public SceneAnchorId? NavigationTargetFor(ActorId actor)
    {
        return npcNavigationTargets.TryGetValue(actor, out SceneAnchorId target)
            ? target
            : null;
    }

    public int WaypointIndexFor(ActorId actor)
    {
        return npcPaths.TryGetValue(actor, out NpcPathState? path) ? path.Index : 0;
    }

    internal void EnableFixedNpcLocomotion()
    {
        fixedNpcLocomotionEnabled = true;
    }

    public TinyFarmStepResult Step(GameIntent humanIntent)
    {
        return Step(humanIntent, evaluateNpcDecisions: true);
    }

    public TinyFarmStepResult Step(GameIntent humanIntent, bool evaluateNpcDecisions)
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

        IReadOnlyList<IntentEnvelope> npcIntents = evaluateNpcDecisions
            ? TinyFarmNpcController.ObserveDecideAndSubmit(
                State,
                recentEvents,
                nextSequence,
                observationMinute,
                Scenes,
                definitions!.Schedules,
                scheduleRuntime)
            : [];
        if (evaluateNpcDecisions)
        {
            decisionEvaluationCount += State.Actors.Count(actor => !actor.IsPlayer);
        }
        envelopes.AddRange(npcIntents);
        nextSequence += npcIntents.Count;

        ForgetCompletedNavigationTargets(envelopes);
        IReadOnlyDictionary<ActorId, SceneAnchorId> semanticTargets = envelopes
            .Where(envelope => envelope.Intent is NavigateToAnchorIntent)
            .ToDictionary(
                envelope => envelope.Actor,
                envelope => ((NavigateToAnchorIntent)envelope.Intent).Anchor);
        envelopes = PreserveCommittedWanderGoals(envelopes);
        semanticTargets = envelopes
            .Where(envelope => envelope.Intent is NavigateToAnchorIntent)
            .ToDictionary(
                envelope => envelope.Actor,
                envelope => ((NavigateToAnchorIntent)envelope.Intent).Anchor);
        RememberActiveNavigationTargets(semanticTargets);
        envelopes = envelopes
            .Select(envelope => TranslateSemanticNavigationIntent(
                envelope,
                advancePath: !fixedNpcLocomotionEnabled))
            .ToList();

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

    internal TinyFarmStepResult AdvancePlayerLocomotion(
        int deltaX,
        int deltaY,
        int distance)
    {
        var intent = new SpatialMoveIntent(deltaX, deltaY, distance);
        var envelope = new IntentEnvelope(
            TinyFarmIds.Player,
            intent,
            State.Minute,
            nextSequence++,
            IntentSourceKind.Human);
        SpatialMoveReductionResult reduction = resolver.ResolveSpatialMoveCore(
            State,
            TinyFarmIds.Player,
            deltaX,
            deltaY,
            distance);
        IntentResult result = TinyFarmResolver.MaterializeSpatialMoveResult(envelope, reduction);
        recentEvents = result.Events;
        return new TinyFarmStepResult(State, [result], []);
    }

    private List<IntentEnvelope> PreserveCommittedWanderGoals(List<IntentEnvelope> envelopes)
    {
        return envelopes.Select(envelope =>
        {
            if (envelope.Intent is NavigateToAnchorIntent requested
                && TinyFarmAnchorIds.IsWander(requested.Anchor)
                && npcNavigationTargets.TryGetValue(envelope.Actor, out SceneAnchorId committed)
                && TinyFarmAnchorIds.IsWander(committed))
            {
                return envelope with { Intent = new NavigateToAnchorIntent(committed) };
            }
            return envelope;
        }).ToList();
    }

    private void ForgetCompletedNavigationTargets(IEnumerable<IntentEnvelope> envelopes)
    {
        foreach (IntentEnvelope envelope in envelopes)
        {
            if (envelope.Source == IntentSourceKind.Dominatus
                && envelope.Intent is not NavigateToAnchorIntent)
            {
                npcNavigationTargets.Remove(envelope.Actor);
                npcPaths.Remove(envelope.Actor);
            }
        }
    }

    public TinyFarmStepResult AdvanceActiveNpcLocomotion()
    {
        return AdvanceActiveNpcLocomotion(snapshotState: true);
    }

    internal TinyFarmStepResult AdvanceActiveNpcLocomotionWithoutStateSnapshot()
    {
        return AdvanceActiveNpcLocomotion(snapshotState: false);
    }

    private TinyFarmStepResult AdvanceActiveNpcLocomotion(bool snapshotState)
    {
        if (State.CurrentScene is not SceneId activeScene || npcNavigationTargets.Count == 0)
        {
            return new TinyFarmStepResult(snapshotState ? State.DeepCopy() : State, [], []);
        }

        var results = new List<IntentResult>();
        activeNpcMovementOrder.Clear();
        foreach (KeyValuePair<ActorId, SceneAnchorId> item in npcNavigationTargets)
        {
            activeNpcMovementOrder.Add(item);
        }
        activeNpcMovementOrder.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.Key.Value, right.Key.Value));
        foreach ((ActorId actorId, SceneAnchorId target) in activeNpcMovementOrder)
        {
            ActorSceneState placement = State.ActorScene(actorId);
            if (placement.Scene != activeScene)
            {
                npcNavigationTargets.Remove(actorId);
                npcPaths.Remove(actorId);
                continue;
            }

            IntentEnvelope semantic = new(
                actorId,
                new NavigateToAnchorIntent(target),
                State.Minute,
                nextSequence++,
                IntentSourceKind.Dominatus);
            IntentEnvelope movement = TranslateSemanticNavigationIntent(semantic, advancePath: true);
            if (movement.Intent is LookIntent)
            {
                continue;
            }

            IntentResult movementResult;
            if (movement.Intent is SpatialMoveIntent spatialMove)
            {
                SpatialMoveReductionResult reduction = resolver.ResolveSpatialMoveCore(
                    State,
                    actorId,
                    spatialMove.DeltaX,
                    spatialMove.DeltaY,
                    spatialMove.Distance);
                movementResult = TinyFarmResolver.MaterializeSpatialMoveResult(movement, reduction);
                movementResult = AppendAnchorArrivalEvent(movementResult, reduction, target);
            }
            else
            {
                ResolutionBatchResult batch = resolver.Resolve(State, [movement]);
                State = batch.State;
                movementResult = batch.Results[0];
            }
            results.Add(movementResult);

            if (movementResult.Status == IntentResultStatus.Accepted
                && movementResult.Envelope.Intent is SpatialMoveIntent)
            {
                npcLocomotionReductionCount++;
            }
            if (movementResult.Reason == IntentReason.MovementBlocked)
            {
                npcPaths.Remove(actorId);
            }

            GameEvent? arrival = movementResult.Events.FirstOrDefault(item =>
                item.Kind == GameEventKind.AnchorReached && item.Anchor == target);
            if (arrival is not null)
            {
                anchorArrivalCount++;
                npcNavigationTargets.Remove(actorId);
                npcPaths.Remove(actorId);
                IntentEnvelope reached = new(
                    actorId,
                    new AnchorReachedIntent(target),
                    State.Minute,
                    nextSequence++,
                    IntentSourceKind.Dominatus);
                ResolutionBatchResult reachedBatch = resolver.Resolve(State, [reached]);
                State = reachedBatch.State;
                results.AddRange(reachedBatch.Results);
            }
        }

        recentEvents = CollectEvents(results);
        return new TinyFarmStepResult(
            snapshotState ? State.DeepCopy() : State,
            results,
            TinyFarmNarrative.Project(recentEvents));
    }

    private static IReadOnlyList<GameEvent> CollectEvents(IReadOnlyList<IntentResult> results)
    {
        if (results.Count == 0)
        {
            return [];
        }
        if (results.Count == 1)
        {
            return results[0].Events;
        }

        int eventCount = 0;
        for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
        {
            eventCount += results[resultIndex].Events.Count;
        }
        var events = new GameEvent[eventCount];
        int eventIndex = 0;
        for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
        {
            IReadOnlyList<GameEvent> source = results[resultIndex].Events;
            for (int sourceIndex = 0; sourceIndex < source.Count; sourceIndex++)
            {
                events[eventIndex++] = source[sourceIndex];
            }
        }
        return events;
    }

    private IntentResult AppendAnchorArrivalEvent(
        IntentResult result,
        SpatialMoveReductionResult reduction,
        SceneAnchorId anchorId)
    {
        if (reduction.Status != IntentResultStatus.Accepted
            || reduction.PreviousPlacement is not ActorSceneState oldPlacement
            || reduction.CurrentPlacement is not ActorSceneState newPlacement
            || !Scenes.TryGetAnchor(anchorId, out SceneAnchorDefinition anchor))
        {
            return result;
        }

        long radiusSquared = (long)anchor.ArrivalRadiusUnits * anchor.ArrivalRadiusUnits;
        bool wasReached = oldPlacement.Scene == anchor.Scene
            && oldPlacement.WorldPosition.SquaredDistance(anchor.Position) <= radiusSquared;
        var goal = new NavigationGoal(
            TinyFarmAurelianSimulationBridge.AnchorRequest(anchor.Id),
            new SimulationSceneId(anchor.Scene.Value),
            new SimulationAnchorId(anchor.Id.Value));
        var destination = new SimulationAnchor(
            goal.Anchor,
            goal.Scene,
            new SimulationPoint(anchor.Position.XUnits, anchor.Position.YUnits),
            anchor.ArrivalRadiusUnits);
        NavigationFact navigation = NavigationCoordinator.ObservePosition(
            goal,
            new SimulationPoint(newPlacement.WorldPosition.XUnits, newPlacement.WorldPosition.YUnits),
            destination);
        bool isReached = newPlacement.Scene == anchor.Scene
            && navigation.Outcome == NavigationOutcome.Arrived;
        if (wasReached || !isReached)
        {
            return result;
        }

        var events = new GameEvent[result.Events.Count + 1];
        for (int index = 0; index < result.Events.Count; index++)
        {
            events[index] = result.Events[index];
        }
        events[^1] = new GameEvent(
            GameEventKind.AnchorReached,
            result.Envelope.Actor,
            Location: anchor.SemanticLocation,
            Scene: anchor.Scene,
            SceneObject: anchor.SemanticObject,
            Anchor: anchor.Id);
        return result with { Events = events };
    }

    private void RememberActiveNavigationTargets(
        IReadOnlyDictionary<ActorId, SceneAnchorId> semanticTargets)
    {
        if (State.CurrentScene is not SceneId activeScene)
        {
            return;
        }

        foreach ((ActorId actor, SceneAnchorId target) in semanticTargets)
        {
            if (actor == TinyFarmIds.Player)
            {
                continue;
            }
            if (State.ActorScene(actor).Scene != activeScene)
            {
                npcNavigationTargets.Remove(actor);
                npcPaths.Remove(actor);
                continue;
            }
            if (npcNavigationTargets.TryGetValue(actor, out SceneAnchorId existing)
                && existing != target)
            {
                npcPaths.Remove(actor);
            }
            npcNavigationTargets[actor] = target;
        }
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

    private IntentEnvelope TranslateSemanticNavigationIntent(IntentEnvelope envelope, bool advancePath)
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
        NavigationRequestId goalIdentity;
        if (destinationScene == actor.Scene)
        {
            goal = anchor.Position;
            goalIdentity = TinyFarmAurelianSimulationBridge.AnchorRequest(move.Anchor);
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
            goalIdentity = TinyFarmAurelianSimulationBridge.RouteRequest(route.Id);
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

        if (!advancePath)
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
        int maximumDistance = fixedNpcLocomotionEnabled
            ? NpcDistancePerLocomotionStep
            : ScenePosition.UnitsPerTile / 8;
        int distance = Math.Min(maximumDistance, Math.Max(Math.Abs(deltaX), Math.Abs(deltaY)));
        return distance == 0
            ? envelope with { Intent = new LookIntent() }
            : envelope with { Intent = new SpatialMoveIntent(stepX, stepY, distance) };
    }

    private NpcPathState GetOrPlan(
        ActorSceneState actor,
        SceneDefinition scene,
        ScenePosition goal,
        NavigationRequestId goalIdentity)
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
        var queue = new Queue<(SimulationSceneId Scene, SimulationRouteId? First)>();
        var visited = new HashSet<SimulationSceneId> { new(source.Value) };
        queue.Enqueue((new SimulationSceneId(source.Value), null));
        while (queue.Count > 0)
        {
            (SimulationSceneId sceneId, SimulationRouteId? first) = queue.Dequeue();
            foreach (SimulationRoute route in simulationScenes.GetScene(sceneId).Routes.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                SimulationRouteId firstRoute = first ?? route.Id;
                if (route.Destination == new SimulationSceneId(destination.Value))
                {
                    SceneRouteId result = new(firstRoute.Value);
                    return Scenes.Get(source).Routes.Single(item => item.Id == result);
                }
                if (visited.Add(route.Destination))
                {
                    queue.Enqueue((route.Destination, firstRoute));
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
        NavigationRequestId GoalIdentity,
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
