using System.Collections.Immutable;
using System.Numerics;
using Aetheris.Humanoid;
using Aurelian.Humanoid;
using Aurelian.Games;
using Aurelian.Runtime.Dominatus.Inspection;
using Aurelian.World.Scenes;
using InputMan.Core;
using Xunit;

namespace Aurelian.Humanoid.Tests;

public sealed class CharacterAnimationTests
{
    [Fact]
    public void ScenePlacementPreservesHeadingAndRejectsUnsupportedScale()
    {
        var canonical = HumanoidRestPoseNormalizer.Normalize(CanonicalAdultTemplate.Create());
        var body = new HumanoidGameplayBody(new(HumanoidGameplayBody.Schema, "fixture",
            canonical.Surface, canonical.Skeleton, [], canonical.Provenance));
        var definition = new HumanoidCharacterDefinition(body, new AurelianAgentRuntime(64), HumanoidAnimationSet.Basic);
        var world = Matrix4x4.CreateRotationY(.7f) * Matrix4x4.CreateTranslation(2, 0, -3);
        var placement = new ScenePlacement("test", "test", world);
        definition.ValidatePlacement(placement);
        var state = definition.CreateState(placement);
        Assert.InRange(MathF.Abs(state.Heading - .7f), 0, .00001f);
        Assert.Equal(new Vector3(2, 0, -3), state.Position);
        Assert.Equal(world, definition.WorldTransform(state, placement));
        Assert.Throws<InvalidDataException>(() => definition.ValidatePlacement(
            placement with { WorldTransform = Matrix4x4.CreateScale(2) }));
        Assert.Throws<InvalidDataException>(() => definition.ValidatePlacement(
            placement with { WorldTransform = Matrix4x4.CreateRotationX(.4f) }));
    }

    [Fact]
    public void LoopInterpolationAndSeamAreExplicit()
    {
        var first = ImmutableArray.Create(new AnatomicalJointRequest(HumanoidJointKind.LeftHip, 0));
        var middle = ImmutableArray.Create(new AnatomicalJointRequest(HumanoidJointKind.LeftHip, 60));
        var clip = new HumanoidAnimationClip("example", 1, true,
            [new(0, first), new(.5, middle), new(1, first)]);
        Assert.Equal(30, clip.Sample(.25).Single().FlexionDegrees);
        Assert.Equal(clip.Sample(0).ToArray(), clip.Sample(1).ToArray());
        Assert.Equal(clip.Sample(.25).ToArray(), clip.Sample(3.25).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => clip.Sample(double.NaN));
        Assert.Throws<ArgumentException>(() => new HumanoidAnimationClip("broken", 1, true,
            [new(0, first), new(1, middle)]));
    }

    [Fact]
    public void PauseAndSamplingPreserveAnimationState()
    {
        var clips = HumanoidAnimationSet.Basic;
        var state = clips.Advance(clips.Initial, CharacterMotion.Walk, .3, .8f);
        Assert.Same(state, clips.Advance(state, CharacterMotion.Aim, 0, 0));
        Assert.Equal(clips.Sample(state).ToArray(), clips.Sample(state).ToArray());
    }

    [Fact]
    public void InterruptedBlendStartsFromTheCurrentPose()
    {
        var clips = HumanoidAnimationSet.Basic;
        var state = clips.Advance(clips.Initial, CharacterMotion.Walk, .1, .8f);
        var prior = clips.Sample(state);
        var changed = clips.Advance(state, CharacterMotion.Aim, .000001, 0);
        var actual = clips.Sample(changed);
        for (int index = 0; index < prior.Length; index++)
        {
            Assert.InRange(Math.Abs(actual[index].FlexionDegrees - prior[index].FlexionDegrees), 0, .001);
            Assert.InRange(Math.Abs(actual[index].AbductionDegrees - prior[index].AbductionDegrees), 0, .001);
        }
    }

    [Fact]
    public void NamedAgentsHaveIndependentStateAndOwnedDominatusLifetime()
    {
        var canonical = HumanoidRestPoseNormalizer.Normalize(CanonicalAdultTemplate.Create());
        var body = new HumanoidGameplayBody(new(HumanoidGameplayBody.Schema, "fixture",
            canonical.Surface, canonical.Skeleton, [], canonical.Provenance));
        var policies = new AurelianAgentRuntime(128);
        var definition = new HumanoidCharacterDefinition(body, policies, HumanoidAnimationSet.Basic);
        using var scene = SceneCompiler.Compile(Scene.World("test", [
            Scene.Agent("a", definition), Scene.Agent("b", definition with { })])).Mount();
        var agents = scene.Agents.OfType<SceneAgent<HumanoidCharacterState>>().ToArray();
        definition.Observe(agents[0], new(.8f, false));
        definition.Observe(agents[1], new(0, true));
        // Dominatus transitions and action bodies run on the shared cadence;
        // wait a bounded number of ticks for the selected action to publish.
        for (int tick = 0; tick < 3; tick++)
        {
            policies.Tick(TimeSpan.FromSeconds(1.0 / 60));
            foreach (var agent in agents) definition.Advance(agent, 1.0 / 60);
        }
        Assert.Equal(CharacterMotion.Walk, agents[0].State.Animation.Motion);
        Assert.Equal(CharacterMotion.Aim, agents[1].State.Animation.Motion);
        Assert.NotEmpty(policies.Inspector.Observe().Trace);
        Assert.True(definition.Pose(agents[0]).SemanticResiduals.All(item => item.MaximumAbsoluteDegrees < .05));
        scene.Dispose();
        Assert.Equal(0, policies.Count);
        Assert.Throws<ObjectDisposedException>(() => definition.Pose(agents[0]));
    }

    [Fact]
    public void InputManCommandsDriveWalkAndAimObservations()
    {
        using var controls = new GameControls();
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), true);
        controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), true);
        var command = controls.Tick(1f / 60);
        Assert.True(command.Forward > 0);
        Assert.True(command.Fire);
        controls.Adapter.OnFocusChanged(false);
        command = controls.Tick(1f / 60);
        Assert.Equal(0, command.Forward);
        Assert.False(command.Fire);
    }

    [Fact]
    public void ReplayingTheSameObservationsReproducesEveryPose()
    {
        static string Replay()
        {
            var canonical = HumanoidRestPoseNormalizer.Normalize(CanonicalAdultTemplate.Create());
            var body = new HumanoidGameplayBody(new(HumanoidGameplayBody.Schema, "fixture",
                canonical.Surface, canonical.Skeleton, [], canonical.Provenance));
            var policies = new AurelianAgentRuntime(64);
            var definition = new HumanoidCharacterDefinition(body, policies, HumanoidAnimationSet.Basic);
            using var scene = SceneCompiler.Compile(Scene.World("test", [Scene.Agent("a", definition)])).Mount();
            var agent = scene.Agents.OfType<SceneAgent<HumanoidCharacterState>>().Single();
            var poses = new List<string>();
            for (int frame = 0; frame < 30; frame++)
            {
                definition.Observe(agent, new(frame < 15 ? .8f : 0, frame >= 15));
                policies.Tick(TimeSpan.FromSeconds(1.0 / 60));
                definition.Advance(agent, 1.0 / 60);
                poses.Add(string.Join(";", definition.Animations.Sample(agent.State.Animation)
                    .Select(item => FormattableString.Invariant($"{item.Joint}:{item.FlexionDegrees:R}:{item.AbductionDegrees:R}"))));
            }
            return string.Join("\n", poses);
        }
        Assert.Equal(Replay(), Replay());
    }
}
