using System.Xml.Linq;
using TinyFarm.Core;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM3Tests
{
    private readonly TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();

    [Fact]
    public void Projection_ContainsAuthoritativePlayerNpcsPlotsInventoryAndMoney()
    {
        TinyFarmState state = TinyFarmContent.CreateWeekState(definitions);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(state, definitions);

        Assert.Equal(state.Actor(TinyFarmIds.Player).Money, frame.Money);
        Assert.Equal(state.Actor(TinyFarmIds.Player).Location, frame.CurrentLocation);
        Assert.Equal(state.Actors.Count, frame.Actors.Count);
        Assert.Equal(state.FarmPlots.Count, frame.Plots.Count);
        Assert.Contains(frame.Actors, actor => actor.Id == TinyFarmIds.Player && actor.IsPlayer);
        Assert.Contains(frame.Actors, actor => actor.Id == TinyFarmIds.Mara && !actor.IsPlayer);
        Assert.All(frame.Plots, plot => Assert.Null(plot.Crop));
    }

    [Fact]
    public void Projection_ReflectsCropAndProductState()
    {
        var session = new TinyFarmSession(TinyFarmContent.CreateWeekState(definitions), definitions);
        session.Step(new MoveIntent(TinyFarmIds.GeneralStore));
        session.Step(new WaitIntent(60));
        session.Step(new BuyProductIntent(TinyFarmIds.TurnipSeed));
        session.Step(new MoveIntent(TinyFarmIds.TownSquare));
        session.Step(new MoveIntent(TinyFarmIds.Farmhouse));
        session.Step(new PlantIntent(TinyFarmIds.PlotOne, TinyFarmIds.TurnipCrop));
        session.Step(new WaterIntent(TinyFarmIds.PlotOne));

        TinyFarmFrame frame = TinyFarmFrameProjector.Project(session.State, definitions);
        TinyFarmPlotView plot = frame.Plots.Single(candidate => candidate.Id == TinyFarmIds.PlotOne);
        Assert.Equal(TinyFarmIds.TurnipCrop, plot.Crop);
        Assert.True(plot.WateredToday);
        Assert.Equal(session.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.TurnipSeed),
            frame.Inventory.SingleOrDefault(item => item.Id == TinyFarmIds.TurnipSeed.Value)?.Count ?? 0);
    }

    [Fact]
    public void Projection_IsDeterministicForSameSnapshot()
    {
        TinyFarmState state = TinyFarmContent.CreateWeekState(definitions);
        string first = TinyFarmFrameProjector.ComputeHash(TinyFarmFrameProjector.Project(state, definitions));
        string second = TinyFarmFrameProjector.ComputeHash(TinyFarmFrameProjector.Project(state.DeepCopy(), definitions));
        Assert.Equal(first, second);
    }

    [Fact]
    public void HumanController_MapsToExistingClosedIntentFamily()
    {
        TinyFarmState state = TinyFarmContent.CreateWeekState(definitions);
        Assert.IsType<MoveIntent>(TinyFarmHumanController.Map(TinyFarmControl.MoveRight, state, definitions));
        Assert.IsType<LookIntent>(TinyFarmHumanController.Map(TinyFarmControl.Look, state, definitions));
        Assert.IsType<TalkIntent>(TinyFarmHumanController.Map(TinyFarmControl.Talk, state, definitions));
        Assert.IsType<WaitIntent>(TinyFarmHumanController.Map(TinyFarmControl.Wait, state, definitions));

        var session = new TinyFarmSession(state, definitions);
        session.Step(new TalkIntent(TinyFarmIds.Mara));
        Assert.IsType<GiveIntent>(TinyFarmHumanController.Map(TinyFarmControl.Give, session.State, definitions));
        session.Step(new MoveIntent(TinyFarmIds.GeneralStore));
        Assert.IsType<BuyProductIntent>(TinyFarmHumanController.Map(TinyFarmControl.Buy, session.State, definitions));
        session.State.MutableInventoryStacks.Add(new InventoryStack(TinyFarmIds.Player, TinyFarmIds.Turnip, 1));
        Assert.IsType<SellProductIntent>(TinyFarmHumanController.Map(TinyFarmControl.Sell, session.State, definitions));
        session.Step(new MoveIntent(TinyFarmIds.TownSquare));
        session.Step(new MoveIntent(TinyFarmIds.Riverside));
        Assert.IsType<TakeIntent>(TinyFarmHumanController.Map(TinyFarmControl.Take, session.State, definitions));
        session.Step(new MoveIntent(TinyFarmIds.TownSquare));
        session.Step(new MoveIntent(TinyFarmIds.Farmhouse));
        session.State.MutableInventoryStacks.Add(new InventoryStack(TinyFarmIds.Player, TinyFarmIds.TurnipSeed, 1));
        PlantIntent plant = Assert.IsType<PlantIntent>(TinyFarmHumanController.Map(TinyFarmControl.Plant, session.State, definitions));
        session.Step(plant);
        Assert.IsType<WaterIntent>(TinyFarmHumanController.Map(TinyFarmControl.Water, session.State, definitions));
        Assert.IsType<HarvestIntent>(TinyFarmHumanController.Map(TinyFarmControl.Harvest, session.State, definitions));
    }

    [Fact]
    public void HumanController_ActionMutatesOnlyThroughSessionResolver()
    {
        TinyFarmState state = TinyFarmContent.CreateWeekState(definitions);
        string before = TinyFarmSemanticHash.Compute(state);
        GameIntent intent = Assert.IsType<MoveIntent>(TinyFarmHumanController.Map(TinyFarmControl.MoveRight, state, definitions));
        Assert.Equal(before, TinyFarmSemanticHash.Compute(state));

        var session = new TinyFarmSession(state, definitions);
        TinyFarmStepResult step = session.Step(intent);
        Assert.NotEqual(before, TinyFarmSemanticHash.Compute(step.State));
        Assert.Contains(step.Results, result => result.Envelope.Source == IntentSourceKind.Human);
    }

    [Fact]
    public void NpcMovement_IsVisibleInProjectionAfterAuthoritativeAdvancement()
    {
        var session = new TinyFarmSession(TinyFarmContent.CreateWeekState(definitions), definitions);
        TinyFarmFrame previous = TinyFarmFrameProjector.Project(session.State, definitions);
        bool moved = false;
        for (int index = 0; index < 12 && !moved; index++)
        {
            TinyFarmStepResult step = session.Step(new WaitIntent(240));
            TinyFarmFrame current = TinyFarmFrameProjector.Project(step.State, definitions);
            moved = previous.Actors.Where(actor => !actor.IsPlayer).Any(actor =>
                current.Actors.Single(candidate => candidate.Id == actor.Id).Location != actor.Location);
            previous = current;
        }

        Assert.True(moved);
    }

    [Fact]
    public void SaveLoad_ReprojectsRestoredAuthoritativeState()
    {
        var session = new TinyFarmSession(TinyFarmContent.CreateWeekState(definitions), definitions);
        session.Step(new MoveIntent(TinyFarmIds.GeneralStore));
        byte[] save = session.CaptureWeekSave();
        string savedProjection = TinyFarmFrameProjector.ComputeHash(TinyFarmFrameProjector.Project(session.State, definitions));
        session.Step(new WaitIntent(240));
        Assert.NotEqual(savedProjection, TinyFarmFrameProjector.ComputeHash(TinyFarmFrameProjector.Project(session.State, definitions)));

        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(save, definitions);
        Assert.Equal(savedProjection, TinyFarmFrameProjector.ComputeHash(TinyFarmFrameProjector.Project(loaded.State, definitions)));
    }

    [Fact]
    public void M3Scenario_PreservesCanonicalProofsAndSemanticProjection()
    {
        TinyFarmM3Proof proof = TinyFarmGraphicalScenario.Prove().Proof;
        Assert.Equal("A", proof.Outcome);
        Assert.True(proof.M1HashPreserved);
        Assert.True(proof.M2HashPreserved);
        Assert.True(proof.NpcMovementProjected);
        Assert.True(proof.SaveLoadProjectionRestored);
        Assert.True(proof.SameSnapshotProjectionMatches);
        Assert.True(proof.FarmingLoopCompleted);
    }

    [Fact]
    public void ProjectTopology_ContainsGraphicsInLeafOnly()
    {
        string root = FindRepositoryRoot();
        string core = File.ReadAllText(Path.Combine(root, "src", "TinyFarm", "TinyFarm.Core", "TinyFarm.Core.csproj"));
        string runtime = File.ReadAllText(Path.Combine(root, "src", "TinyFarm", "TinyFarm.Runtime", "TinyFarm.Runtime.csproj"));
        string appPath = Path.Combine(root, "src", "TinyFarm", "TinyFarm.MonoGame", "TinyFarm.MonoGame.csproj");
        string app = File.ReadAllText(appPath);

        Assert.DoesNotContain("MonoGame", core, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MonoGame", runtime, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MonoGame.Framework.DesktopGL", app, StringComparison.Ordinal);
        XDocument project = XDocument.Load(appPath);
        Assert.Contains(project.Descendants("ProjectReference"), reference =>
            ((string?)reference.Attribute("Include"))?.Contains("TinyFarm.Core", StringComparison.Ordinal) == true);
        Assert.Contains(project.Descendants("ProjectReference"), reference =>
            ((string?)reference.Attribute("Include"))?.Contains("TinyFarm.Runtime", StringComparison.Ordinal) == true);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TinyFarm.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate TinyFarm repository root.");
    }
}
