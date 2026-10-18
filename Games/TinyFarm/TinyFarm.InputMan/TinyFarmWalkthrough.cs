using TinyFarm.Core;

namespace TinyFarm.InputMan;

/// <summary>Qualification driver: DotRecast proposes, ordinary resolver movement accepts.</summary>
public sealed class TinyFarmWalkthrough(TinyFarmGame game)
{
    private readonly DotRecastNavigationPlanner navigation = new();
    private readonly TinyFarmState initial = game.State.DeepCopy();
    private TinyFarmState replayState = game.State.DeepCopy();
    private readonly List<TinyFarmReplayRecord> tape = [];
    private readonly List<string> outcomes = [];
    public Action<string>? Checkpoint { get; set; }
    public IReadOnlyList<string> Outcomes => outcomes;
    public int RecordedIntents => tape.Count;

    /// <summary>
    /// The Sleeping Spring through Mara's supper: the opening advice, Old Burrow, the supper request, a seed for
    /// tomorrow, mushrooms and mint at Riverside, the stove, and supper with Mara.
    /// </summary>
    public void Run()
    {
        game.Start();
        Checkpoint?.Invoke("02-farm-gameplay");
        Act(new CompleteSupperIntent(), expectAccepted: false);
        TalkToMara();
        Checkpoint?.Invoke("03-opening-dialogue");
        FinishDialogue();
        ClearOldBurrow();
        TalkToMara();
        Checkpoint?.Invoke("03-supper-request");
        FinishDialogue();
        if (!TinyFarmSupper.IsRequested(game.State))
        {
            throw new InvalidOperationException("Mara did not ask for supper after Old Burrow.");
        }
        PlantForTomorrow();
        Checkpoint?.Invoke("04-farming-or-pickup");
        GatherAtRiverside();
        Checkpoint?.Invoke("06-riverside");
        CookAtHome();
        Checkpoint?.Invoke("mid-objective-save");
        FinishWithMara();
    }

    public void ClearOldBurrow()
    {
        Portal("farm-exit");
        CrossBridge(eastward: true);
        Portal("dungeon-entrance");
        Checkpoint?.Invoke("05-old-burrow");
        Walk(new GridPosition(7, 5));
        Face(1, 0);
        int guard = 0;
        while (!TinyFarmSupper.BurrowCleared(game.State) && guard++ < 4)
        {
            Act(new SwordIntent());
            for (int tick = 0; tick < 18; tick++)
            {
                Act(new SliceTickIntent());
            }
        }
        if (!TinyFarmSupper.BurrowCleared(game.State))
        {
            throw new InvalidOperationException("The slime survived the walkthrough's swings.");
        }
        Portal("dungeon-exit");
        CrossBridge(eastward: false);
        Portal("farm-entrance");
    }

    /// <summary>The river splits the approach; the planner's point agent would hug the bridge rail, so walk its middle.</summary>
    private void CrossBridge(bool eastward)
    {
        var west = new ScenePosition(13 * ScenePosition.UnitsPerTile, 8 * ScenePosition.UnitsPerTile);
        var east = new ScenePosition(16 * ScenePosition.UnitsPerTile + ScenePosition.UnitsPerTile / 2, 8 * ScenePosition.UnitsPerTile);
        Walk(eastward ? west : east);
        Walk(eastward ? east : west);
    }

    public void PlantForTomorrow()
    {
        SceneDefinition farm = game.Definitions.Scenes.Get(TinyFarmSceneIds.Farm);
        SceneLayoutRow plot = farm.Layout.First(row =>
        {
            SceneObjectDefinition definition = farm.Object(row.ObjectId);
            return definition.Kind == SceneObjectKind.Plot
                && game.State.FarmPlots.Any(state => state.Id.Value == definition.SemanticReference && state.Crop is null);
        });
        Act(new SelectHotbarSlotIntent(new HotbarSlotId(1)));
        Approach(Center(plot));
        Act(new UseSelectedIntent());
    }

    public void GatherAtRiverside()
    {
        GoToRiverside();
        GatherAndReturnHome();
    }

    public void GoToRiverside()
    {
        Portal("farm-exit");
        CrossBridge(eastward: true);
        Portal("riverside-entrance");
    }

    public void GatherAndReturnHome()
    {
        ItemState mint = game.State.Item(TinyFarmIds.WildMint);
        Approach(mint.GroundPosition ?? throw new InvalidOperationException("The wild mint is not on the riverbank."));
        Act(new InteractIntent());
        Approach(game.Definitions.ForageNode(TinyFarmIds.RiversideHenOfTheWoods).Position);
        Act(new InteractIntent());
        Portal("riverside-exit");
        CrossBridge(eastward: false);
        Portal("farm-entrance");
    }

    public void CookAtHome()
    {
        Portal("residence-entrance");
        SceneDefinition house = game.Definitions.Scenes.Get(TinyFarmSceneIds.Residence);
        SceneLayoutRow stove = house.Layout.First(row => house.Object(row.ObjectId).Kind == SceneObjectKind.CookingStation);
        Approach(Center(stove));
        Act(new InteractIntent());
        Act(new CraftIntent(stove.ObjectId, [new CookingRecipeInput(TinyFarmIds.HenOfTheWoods, 1)],
            TinyFarmIds.SauteedHenOfTheWoodsRecipe));
        game.DispatchMenu("close");
        Portal("residence-exit");
    }

    public void FinishWithMara()
    {
        game.Start();
        TalkToMara();
        FinishDialogue();
        if (!TinyFarmSupper.IsComplete(game.State))
        {
            throw new InvalidOperationException("The supper walkthrough did not complete.");
        }
        Checkpoint?.Invoke("07-completion");
    }

    private static ScenePosition Center(SceneLayoutRow row) => new(
        row.X * ScenePosition.UnitsPerTile + row.Width * ScenePosition.UnitsPerTile / 2,
        row.Y * ScenePosition.UnitsPerTile + row.Height * ScenePosition.UnitsPerTile / 2);

    public TinyFarmReplayResult Replay()
    {
        TinyFarmReplayEnvelope envelope = TinyFarmSemanticReplay.Create(initial, game.Definitions.Identity,
            game.Host.CadenceConfigurationIdentity, tape);
        return TinyFarmSemanticReplay.Replay(TinyFarmSemanticReplay.Deserialize(TinyFarmSemanticReplay.Serialize(envelope)),
            game.Definitions, game.Host.CadenceConfigurationIdentity);
    }

    public void Portal(string id)
    {
        SceneDefinition scene = game.Definitions.Scenes.Get(game.State.ActorScene(TinyFarmIds.Player).Scene);
        SceneLayoutRow row = scene.Layout.Single(item => item.ObjectId.Value == id);
        Approach(ScenePosition.FromGrid(new GridPosition(row.X, row.Y)));
        Act(new InteractIntent(new SceneObjectId(id)));
    }

    public void TalkToMara()
    {
        ActorSceneState mara = game.State.ActorScene(TinyFarmIds.Mara);
        Approach(mara.WorldPosition);
        Act(new InteractIntent());
        if (!game.Dialogue.IsActive)
        {
            throw new InvalidOperationException("E did not start Mara's conversation.");
        }
    }

    public void FinishDialogue()
    {
        int guard = 0;
        int emitted = game.Dialogue.ConsequenceEmissionCount;
        while (game.Dialogue.IsActive && guard++ < 20)
        {
            game.ApplyDialogue(TinyFarmDialogueAction.Confirm);
            if (game.Dialogue.ConsequenceEmissionCount != emitted)
            {
                emitted = game.Dialogue.ConsequenceEmissionCount;
                Record([game.Dialogue.LastConsequenceResult!]);
            }
        }
        if (game.Dialogue.IsActive)
        {
            throw new InvalidOperationException("Conversation failed to finish within 20 advances.");
        }
    }

    public void Approach(ScenePosition target)
    {
        SceneDefinition scene = game.Definitions.Scenes.Get(game.State.ActorScene(TinyFarmIds.Player).Scene);
        (int X, int Y)[] sides = [(0, 1), (1, 0), (-1, 0), (0, -1)];
        foreach ((int x, int y) in sides)
        {
            var position = new ScenePosition(target.XUnits + x * 1024, target.YUnits + y * 1024);
            if (!TinyFarmScenes.IsInBounds(scene, position) || TinyFarmScenes.IsBlocked(scene, position))
            {
                continue;
            }
            Walk(position);
            Face(-x, -y);
            return;
        }
        throw new InvalidOperationException("No walkable approach to " + target);
    }

    public void Walk(GridPosition goal) => Walk(ScenePosition.FromGrid(goal));

    public void Walk(ScenePosition goal)
    {
        ActorSceneState player = game.State.ActorScene(TinyFarmIds.Player);
        NavigationPath path = navigation.FindPath(game.Definitions.Scenes.Get(player.Scene), player.WorldPosition, goal);
        if (!path.Succeeded)
        {
            throw new InvalidOperationException($"Walk to {goal} failed: {path.Failure} {path.FailureDetail}");
        }
        foreach (ScenePosition point in path.Waypoints.Skip(1))
        {
            int guard = 0;
            while (game.State.ActorScene(TinyFarmIds.Player).WorldPosition != point)
            {
                ScenePosition before = game.State.ActorScene(TinyFarmIds.Player).WorldPosition;
                int dx = point.XUnits - before.XUnits;
                int dy = point.YUnits - before.YUnits;
                bool horizontal = Math.Abs(dx) >= Math.Abs(dy);
                int distance = Math.Min(128, Math.Abs(horizontal ? dx : dy));
                Act(new SpatialMoveIntent(horizontal ? Math.Sign(dx) : 0, horizontal ? 0 : Math.Sign(dy), distance));
                if (game.State.ActorScene(TinyFarmIds.Player).WorldPosition == before || guard++ > 2000)
                {
                    throw new InvalidOperationException($"Resolver blocked walk from {before} toward {point}.");
                }
            }
        }
    }

    private void Face(int x, int y) => Act(new SpatialMoveIntent(x, y, 1));

    private void Act(GameIntent intent, bool expectAccepted = true)
    {
        TinyFarmStepResult step = game.Execute(intent);
        if (game.Host.Session.HasActiveCombat)
        {
            game.Advance(
                TimeSpan.FromMilliseconds(70),
                new global::InputMan.Core.InputFrame(
                    0,
                    0.07f,
                    new Dictionary<global::InputMan.Core.ActionId, global::InputMan.Core.ActionValue>(),
                    new Dictionary<global::InputMan.Core.AxisId, float>(),
                    new Dictionary<global::InputMan.Core.Axis2Id, System.Numerics.Vector2>(),
                    null),
                focused: true);
        }
        Record(step.Results);
        IntentResult result = step.Results[0];
        if (expectAccepted && result.Status == IntentResultStatus.Rejected)
        {
            throw new InvalidOperationException($"{intent} rejected at {game.State.ActorScene(TinyFarmIds.Player)}: {result.Reason}");
        }
    }

    private void FinishActiveCombat()
    {
        if (!game.Host.Session.HasActiveCombat)
        {
            return;
        }
        game.Advance(
            TimeSpan.FromMilliseconds(300),
            new global::InputMan.Core.InputFrame(
                0,
                0.3f,
                new Dictionary<global::InputMan.Core.ActionId, global::InputMan.Core.ActionValue>(),
                new Dictionary<global::InputMan.Core.AxisId, float>(),
                new Dictionary<global::InputMan.Core.Axis2Id, System.Numerics.Vector2>(),
                null),
            focused: true);
    }

    private void Record(IReadOnlyList<IntentResult> results)
    {
        var resolver = new TinyFarmResolver(game.Definitions);
        foreach (IntentResult result in results)
        {
            var reduction = resolver.Resolve(replayState, [result.Envelope]);
            replayState = reduction.State;
            if (reduction.Results.Single().Status != result.Status)
            {
                throw new InvalidOperationException("Replay acceptance differs for " + result.Envelope.Intent);
            }
            tape.Add(new TinyFarmReplayRecord(tape.Count, result.Envelope, TinyFarmSemanticHash.Compute(replayState)));
            if (result.Envelope.Intent is not SpatialMoveIntent)
            {
                outcomes.Add($"{result.Envelope.Intent}: {result.Status} / {result.Reason}");
            }
        }
        if (TinyFarmSemanticHash.Compute(replayState) != TinyFarmSemanticHash.Compute(game.State))
        {
            throw new InvalidOperationException("Semantic replay differs after an application step.");
        }
    }
}

