using System.Numerics;
using System.Text;
using Aurelian.Effects2D;
using TinyFarm.Core;
using TinyFarm.Runtime;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmEffectsM8Tests
{
    [Fact]
    public void AcceptedAttackProjectsShaderHitAndFlashWhileRejectedAttackProjectsNothing()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        var session = new TinyFarmSession(TinyFarmM21ControlStates.Create(definitions), definitions);
        var projector = new TinyFarmVisualEffectProjector();

        TinyFarmStepResult accepted = session.Step(
            new AttackIntent(TinyFarmIds.DungeonSlime),
            evaluateNpcDecisions: false);
        IReadOnlyList<VisualEffectEvent> effects = projector.Project(accepted.Results, session.State, definitions);
        Assert.Collection(
            effects,
            hit =>
            {
                Assert.Equal(VisualEffectIds.SwordHit, hit.EffectId);
                Assert.Equal(EffectCoordinateSpace.World, hit.Space);
                Assert.NotNull(hit.Position);
            },
            flash =>
            {
                Assert.Equal(VisualEffectIds.ScreenFlash, flash.EffectId);
                Assert.Equal(EffectCoordinateSpace.Screen, flash.Space);
            });

        TinyFarmStepResult rejected = session.Step(
            new AttackIntent(TinyFarmIds.DungeonSlime),
            evaluateNpcDecisions: false);
        Assert.Empty(projector.Project(rejected.Results, session.State, definitions));
    }

    [Fact]
    public void AcceptedPickupProjectsBurstAndDoesNotOwnInventoryMutation()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState state = TinyFarmM17ControlStates.Create(definitions);
        ItemState item = state.Item(TinyFarmIds.WildMint);
        var session = new TinyFarmSession(state, definitions);
        TinyFarmStepResult step = session.Step(new TakeIntent(item.Id), evaluateNpcDecisions: false);
        int inventoryCount = session.State.Actor(TinyFarmIds.Player).Inventory.Count;

        var runtime = new EffectRuntime(EffectCatalog.CreateSmallGameDefaults());
        VisualEffectEvent effect = Assert.Single(new TinyFarmVisualEffectProjector().Project(step.Results, session.State, definitions));
        Assert.Equal(VisualEffectIds.PickupSparkle, effect.EffectId);
        Assert.True(runtime.TryEmit(effect, out _));
        runtime.Update(TimeSpan.FromSeconds(2));
        Assert.Equal(inventoryCount, session.State.Actor(TinyFarmIds.Player).Inventory.Count);
    }

    [Fact]
    public void AcceptedHarvestProjectsPuffWhileRejectedHarvestProjectsNothing()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        TinyFarmState state = TinyFarmM16ControlStates.Create(definitions);
        FarmPlotState plot = state.FarmPlots.Single(item => item.Id == TinyFarmIds.PlotOne);
        int plotIndex = state.MutableFarmPlots.IndexOf(plot);
        state.MutableFarmPlots[plotIndex] = plot with
        {
            Crop = TinyFarmIds.TurnipCrop,
            GrowthStage = definitions.Crop(TinyFarmIds.TurnipCrop).GrowthDays
        };
        SceneLayoutRow placement = definitions.Scenes
            .Get(TinyFarmSceneIds.Farm)
            .Placement(new SceneObjectId(TinyFarmIds.PlotOne.Value));
        int actorIndex = state.MutableActorScenes.FindIndex(item => item.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[actorIndex] = state.MutableActorScenes[actorIndex] with
        {
            Scene = TinyFarmSceneIds.Farm,
            WorldPosition = ScenePosition.FromGrid(new GridPosition(placement.X - 1, placement.Y)),
            Facing = ActorFacing.Right
        };
        var session = new TinyFarmSession(state, definitions);
        var projector = new TinyFarmVisualEffectProjector();

        TinyFarmStepResult accepted = session.Step(new HarvestIntent(TinyFarmIds.PlotOne), evaluateNpcDecisions: false);
        VisualEffectEvent puff = Assert.Single(projector.Project(accepted.Results, session.State, definitions));
        Assert.Equal(VisualEffectIds.HarvestPuff, puff.EffectId);
        TinyFarmStepResult rejected = session.Step(new HarvestIntent(TinyFarmIds.PlotOne), evaluateNpcDecisions: false);
        Assert.Empty(projector.Project(rejected.Results, session.State, definitions));
    }

    [Fact]
    public void SaveExcludesTransientRuntimeAndAmbienceIsRederivedFromScene()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        var session = new TinyFarmSession(TinyFarmM21ControlStates.Create(definitions), definitions);
        TinyFarmStepResult attack = session.Step(new AttackIntent(TinyFarmIds.DungeonSlime), evaluateNpcDecisions: false);
        var projector = new TinyFarmVisualEffectProjector();
        var effects = new EffectRuntime(EffectCatalog.CreateSmallGameDefaults());
        foreach (VisualEffectEvent request in projector.Project(attack.Results, session.State, definitions))
        {
            Assert.True(effects.TryEmit(request, out _));
        }
        Assert.NotEqual(0, effects.ActiveEmitterCount);

        byte[] save = TinyFarmChunkedSaveCodec.Write(session, definitions);
        string saveText = Encoding.UTF8.GetString(save);
        Assert.DoesNotContain("sword-hit", saveText, StringComparison.Ordinal);
        Assert.DoesNotContain("particle", saveText, StringComparison.OrdinalIgnoreCase);
        TinyFarmSession loaded = TinyFarmChunkedSaveCodec.Read(save, definitions);
        var rebuilt = new EffectRuntime(EffectCatalog.CreateSmallGameDefaults());
        Assert.Equal(0, rebuilt.ActiveEmitterCount);
        Assert.True(rebuilt.TryEmit(projector.ProjectAmbience(loaded.State.CurrentScene!.Value), out _));
    }

    [Fact]
    public void ReplayEquivalentIntentProducesSameGameHashEventIdsAndSpawnTrace()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        TinyFarmState initial = TinyFarmM21ControlStates.Create(definitions);
        Run first = Execute(initial, definitions);
        Run replay = Execute(initial, definitions);

        Assert.Equal(first.GameHash, replay.GameHash);
        Assert.Equal(first.EventIds, replay.EventIds);
        Assert.Equal(first.Particles, replay.Particles);
    }

    [Fact]
    public void DisabledOrFailedEffectsCannotChangeGameplayHash()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        TinyFarmState initial = TinyFarmM21ControlStates.Create(definitions);
        Run enabled = Execute(initial, definitions);

        var disabledSession = new TinyFarmSession(initial, definitions);
        _ = disabledSession.Step(new AttackIntent(TinyFarmIds.DungeonSlime), evaluateNpcDecisions: false);
        string disabledHash = TinyFarmSemanticHash.Compute(disabledSession.State);
        Assert.Equal(enabled.GameHash, disabledHash);

        var failedEffects = new EffectRuntime(EffectCatalog.CreateSmallGameDefaults(), particleCapacity: 1);
        VisualEffectEvent hit = enabled.Events.First(effect => effect.EffectId == VisualEffectIds.SwordHit);
        Assert.False(failedEffects.TryEmit(hit, out _));
        Assert.Equal(enabled.GameHash, TinyFarmSemanticHash.Compute(disabledSession.State));
    }

    private static Run Execute(TinyFarmState initial, TinyFarmDefinitions definitions)
    {
        var session = new TinyFarmSession(initial, definitions);
        TinyFarmStepResult step = session.Step(new AttackIntent(TinyFarmIds.DungeonSlime), evaluateNpcDecisions: false);
        IReadOnlyList<VisualEffectEvent> events = new TinyFarmVisualEffectProjector().Project(step.Results, session.State, definitions);
        var runtime = new EffectRuntime(EffectCatalog.CreateSmallGameDefaults());
        foreach (VisualEffectEvent effectEvent in events)
        {
            runtime.TryEmit(effectEvent, out _);
        }
        return new Run(
            TinyFarmSemanticHash.Compute(session.State),
            events.Select(item => item.StableEventId).ToArray(),
            runtime.BuildParticleDrawData().ToArray(),
            events);
    }

    private sealed record Run(
        string GameHash,
        IReadOnlyList<VisualEffectEventId> EventIds,
        IReadOnlyList<ParticleSnapshot> Particles,
        IReadOnlyList<VisualEffectEvent> Events);
}
