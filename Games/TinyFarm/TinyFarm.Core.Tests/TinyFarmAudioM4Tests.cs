using System.Numerics;
using Aurelian.Audio;
using TinyFarm.Core;
using TinyFarm.Runtime;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmAudioM4Tests
{
    [Fact]
    public void AcceptedAttackProjectsOneSwordCueAndRejectedAttackProjectsNone()
    {
        var projector = new TinyFarmAudioProjector();
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        var session = new TinyFarmSession(TinyFarmM21ControlStates.Create(definitions), definitions);
        IntentResult accepted = session.Step(
            new AttackIntent(TinyFarmIds.DungeonSlime),
            evaluateNpcDecisions: false).Results.Single();
        IntentResult rejected = session.Step(
            new AttackIntent(TinyFarmIds.DungeonSlime),
            evaluateNpcDecisions: false).Results.Single();

        AudioCue cue = Assert.Single(projector.Project([accepted, rejected]));
        Assert.Equal(TinyFarmAudioAssets.SwordSwing, cue.Asset);
        Assert.Equal(
            new AudioEventId($"tinyfarm:{accepted.Envelope.Sequence}:0:EnemyDefeated"),
            cue.EventId);
        Assert.Equal(IntentReason.AlreadyDefeated, rejected.Reason);
    }

    [Fact]
    public void AcceptedPickupProjectsOneCueAndStableEventIdentityDedupesReprojection()
    {
        var projector = new TinyFarmAudioProjector();
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM14();
        var session = new TinyFarmSession(TinyFarmM17ControlStates.Create(definitions), definitions);
        IntentResult result = session.Step(
            new TakeIntent(TinyFarmIds.WildMint),
            evaluateNpcDecisions: false).Results.Single();
        AudioCue first = Assert.Single(projector.Project([result]));
        AudioCue second = Assert.Single(projector.Project([result]));
        using var resources = new AudioResourceScope();
        resources.Add(new AudioClipResource(first.Asset, "pickup", 48_000, 1, 48, new float[48]));
        using var runtime = new AurelianAudioRuntime(resources, new NullAudioOutputBackend());

        Assert.NotNull(runtime.Play(first));
        Assert.Null(runtime.Play(second));
        Assert.Equal(1, runtime.Inspect().DuplicateEvents);
    }

    [Fact]
    public void MusicAmbientAndPositionalCuesStayPresentationOnly()
    {
        var projector = new TinyFarmAudioProjector();
        AudioCue music = projector.FarmMusic(new AudioEventId("session:farm-music"));
        AudioCue ambient = projector.RiverAmbient(new AudioEventId("scene:river"), new Vector2(-8f, 2f));

        Assert.Equal(AudioBusId.Music, music.Bus);
        Assert.True(music.Loop);
        Assert.Equal(AudioBusId.Ambient, ambient.Bus);
        Assert.Equal(new Vector2(-8f, 2f), ambient.Position);
        Assert.DoesNotContain(typeof(GameEvent).Assembly.GetReferencedAssemblies(), name => name.Name == "Aurelian.Audio");
    }
}
