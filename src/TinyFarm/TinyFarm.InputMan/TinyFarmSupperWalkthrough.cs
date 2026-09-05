using TinyFarm.Core;

namespace TinyFarm.InputMan;

/// <summary>Qualification driver: DotRecast proposes, ordinary resolver movement accepts.</summary>
public sealed class TinyFarmSupperWalkthrough(TinyFarmSupperGame game)
{
    private readonly DotRecastNavigationPlanner navigation = new();
    private readonly TinyFarmState initial = game.State.DeepCopy();
    private TinyFarmState replayState = game.State.DeepCopy();
    private readonly List<TinyFarmReplayRecord> tape = [];
    private readonly List<string> outcomes = [];
    public Action<string>? Checkpoint { get; set; }
    public IReadOnlyList<string> Outcomes => outcomes;
    public int RecordedIntents => tape.Count;

    public void Run()
    {
        game.Start();
        Checkpoint?.Invoke("02-farm-gameplay");
        Act(new CompleteSupperIntent(), expectAccepted: false);
        Walk(new GridPosition(6, 5));
        Face(1, 0);
        Act(new InteractIntent());
        if (game.State.Item(TinyFarmIds.WildMint).Owner != TinyFarmIds.Player)
        {
            throw new InvalidOperationException("The player-facing E interaction did not pick up the mint.");
        }
        Act(new SelectHotbarSlotIntent(new HotbarSlotId(1)));
        Act(new UseSelectedIntent());
        Checkpoint?.Invoke("04-farming-or-pickup");
        Portal("farm-exit");
        Portal("town-entrance");
        TalkToMara();
        Checkpoint?.Invoke("03-dialogue");
        FinishDialogue();
        Portal("town-exit");
        Portal("riverside-entrance");
        Approach(game.Definitions.ForageNode(TinyFarmIds.RiversideHenOfTheWoods).Position);
        Act(new InteractIntent());
        // Noon gives the live schedule a real location change during this session.
        Act(new WaitIntent(35));
        Portal("riverside-exit");
        Portal("dungeon-entrance");
        Checkpoint?.Invoke("06-secondary-scene");
        Walk(new GridPosition(7, 5));
        Face(1, 0);
        Act(new SelectHotbarSlotIntent(new HotbarSlotId(4)));
        Act(new UseSelectedIntent());
        Checkpoint?.Invoke("05-combat");
        Portal("dungeon-exit");
        Walk(new GridPosition(18, 8));
        Portal("farm-entrance");
        Walk(new GridPosition(16, 8));
        Walk(new GridPosition(6, 8));
        Portal("residence-entrance");
        Walk(new GridPosition(5, 4));
        Face(1, 0);
        Act(new InteractIntent());
        Checkpoint?.Invoke("mid-objective-save");
        FinishFromKitchen();
    }

    public void FinishFromKitchen()
    {
        game.Start();
        Walk(new GridPosition(5, 6));
        Walk(new GridPosition(9, 6));
        Portal("residence-exit");
        Walk(new GridPosition(6, 8));
        Walk(new GridPosition(16, 8));
        Portal("farm-exit");
        Portal("riverside-entrance");
        TalkToMara();
        FinishDialogue();
        if (!TinyFarmSupper.IsComplete(game.State) || game.Screen != SupperScreen.Complete)
        {
            throw new InvalidOperationException("The supper walkthrough did not complete.");
        }
        Checkpoint?.Invoke("07-completion");
    }

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

    private void TalkToMara()
    {
        ActorSceneState mara = game.State.ActorScene(TinyFarmIds.Mara);
        Approach(mara.WorldPosition);
        Act(new InteractIntent());
        if (!game.Dialogue.IsActive)
        {
            throw new InvalidOperationException("E did not start Mara's conversation.");
        }
    }

    private void FinishDialogue()
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
        Record(step.Results);
        IntentResult result = step.Results[0];
        if (expectAccepted && result.Status == IntentResultStatus.Rejected)
        {
            throw new InvalidOperationException($"{intent} rejected at {game.State.ActorScene(TinyFarmIds.Player)}: {result.Reason}");
        }
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

