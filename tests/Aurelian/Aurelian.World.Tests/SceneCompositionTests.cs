using System.Numerics;
using Aurelian.World.Agents;
using Aurelian.World.Scenes;
using Xunit;

namespace Aurelian.World.Tests;

public sealed class SceneCompositionTests
{
    [Fact]
    public void PresentationMaterialChangesInvalidateSceneIdentity()
    {
        var material = new Aurelian.Rendering.Contracts.Models.ModelMaterial("wax") { Metallic = 0 };
        string Identity(Aurelian.Rendering.Contracts.Models.ModelMaterial value)
        {
            return SceneCompiler.Compile(Scene.World("room", [
                Scene.Box("object", Vector3.One, Vector4.One) with { Material = value },
            ])).ContentIdentity;
        }
        string original = Identity(material);
        Assert.NotEqual(original, Identity(material with { AlphaBlend = true }));
        Assert.NotEqual(original, Identity(material with { SubsurfaceStrength = .5f }));
        Assert.NotEqual(original, Identity(material with { SubsurfaceRadius = .1f }));
        Assert.NotEqual(original, Identity(material with { SubsurfaceColor = new(.4f) }));
    }

    [Fact]
    public void ReusedFragmentsCreateScopedIdentitiesAndIndependentMutableState()
    {
        var definition = new TestDefinition(4);
        var fragment = Scene.Group("guard-post", [Scene.Agent("guard", definition, at: new(0, 0, -2))]);
        ScenePlan plan = SceneCompiler.Compile(Scene.World("arena",
            [Scene.Instance("north", fragment, at: new(0, 0, -10)),
             Scene.Instance("south", fragment, at: new(0, 0, 10))]));
        using SceneInstance first = plan.Mount();
        using SceneInstance second = plan.Mount();
        var north = first.Agent<MutableState>("north.guard");
        var south = first.Agent<MutableState>("south.guard");
        Assert.Equal(new Vector3(0, 0, -12), north.State.Position);
        Assert.Equal(new Vector3(0, 0, 8), south.State.Position);
        Assert.NotSame(north.State, south.State);
        Assert.NotSame(north.State, second.Agent<MutableState>("north.guard").State);
        north.State.Values.Add(9);
        Assert.Empty(south.State.Values);
        Assert.Equal(new[] { "north.guard", "south.guard" }, first.Agents.Select(agent => agent.Id));
    }

    [Fact]
    public void WithCustomizesConfigurationAndDoesNotCopyLiveState()
    {
        var weak = new TestDefinition(4);
        TestDefinition strong = weak with { Health = 40 };
        using var scene = SceneCompiler.Compile(Scene.World("arena",
            [Scene.Agent("weak", weak), Scene.Agent("strong", strong)])).Mount();
        Assert.Equal(4, scene.Agent<MutableState>("weak").State.Health);
        Assert.Equal(40, scene.Agent<MutableState>("strong").State.Health);
        Assert.NotSame(scene.Agent<MutableState>("weak").State, scene.Agent<MutableState>("strong").State);
        Assert.NotEqual(weak.Identity, strong.Identity);
    }

    [Fact]
    public void NestedScaleRotationAndTranslationUseLocalThenParentTransforms()
    {
        var local = new SceneTransform(new(1, 2, 3), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f), new(2, 3, 4));
        var parent = new SceneTransform(new(7, 8, 9), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f), new(3, 2, 1));
        var root = SceneTransform.At(new(100, 0, 0));
        var fragment = Scene.Group("fragment", [Scene.Box("body", Vector3.One, Vector4.One)]) with { Transform = local };
        var document = Scene.World("arena",
            [Scene.Instance("post", fragment) with { Transform = parent }]) with { Transform = root };
        var box = Assert.Single(SceneCompiler.Compile(document).Boxes);
        Assert.Equal("post.body", box.Id);
        Assert.Equal(local.Matrix() * parent.Matrix() * root.Matrix(), box.WorldTransform);
    }

    [Fact]
    public void ChildrenAreCopiedAndCannotBeChangedBehindACompiledDocument()
    {
        var source = new List<SceneNode> { Scene.Box("body", Vector3.One, Vector4.One) };
        SceneGroup document = Scene.World("arena", source);
        source.Clear();
        Assert.Single(SceneCompiler.Compile(document).Boxes);
        Assert.Throws<InvalidDataException>(() => SceneCompiler.Compile(new SceneGroup("arena", default)));
    }

    [Fact]
    public void InvalidDocumentsFailBeforeAnyFactoryRuns()
    {
        int creations = 0;
        var definition = new TestDefinition(4) { Created = _ => creations++ };
        var invalid = Scene.World("arena",
            [Scene.Agent("guard", definition), Scene.Box("guard", Vector3.One, Vector4.One)]);
        Assert.Contains("guard", Assert.Throws<InvalidDataException>(() => SceneCompiler.Compile(invalid)).Message);
        Assert.Equal(0, creations);
        Assert.Throws<InvalidDataException>(() => SceneCompiler.Compile(Scene.World("arena",
            [Scene.Agent("guard", definition), Scene.Box("bad", new(-1, 1, 1), Vector4.One)])));
        Assert.Throws<InvalidDataException>(() => SceneCompiler.Compile(Scene.World("arena",
            [Scene.Agent("guard", definition) with { Transform = default }])));
        Assert.Throws<InvalidDataException>(() => SceneCompiler.Compile(Scene.World("arena",
            [Scene.Agent("guard.bad", definition)])));
        Assert.Equal(0, creations);
    }

    [Fact]
    public void FailedActivationReleasesAllCreatedStatesAndEarlierLeases()
    {
        var states = new List<MutableState>();
        int releases = 0;
        var working = new TestDefinition(4)
        {
            Created = states.Add,
            Activation = _ => new Lease(() => releases++),
        };
        var failing = working with { Activation = _ => throw new InvalidOperationException("policy failure") };
        ScenePlan plan = SceneCompiler.Compile(Scene.World("arena",
            [Scene.Agent("a", working), Scene.Agent("b", failing)]));
        Assert.Equal("policy failure", Assert.Throws<InvalidOperationException>(() => plan.Mount()).Message);
        Assert.Equal(2, states.Count);
        Assert.All(states, state => Assert.True(state.Disposed));
        Assert.Equal(1, releases);
    }

    [Fact]
    public void FailedFactoryReleasesStatesAlreadyCreated()
    {
        var states = new List<MutableState>();
        var definition = new TestDefinition(4) { Created = states.Add };
        var broken = definition with { FailCreation = true };
        var plan = SceneCompiler.Compile(Scene.World("arena",
            [Scene.Agent("a", definition), Scene.Agent("b", broken)]));
        Assert.Throws<InvalidOperationException>(() => plan.Mount());
        Assert.True(Assert.Single(states).Disposed);
    }

    [Fact]
    public void UnmountContinuesAfterCleanupFailuresAndPreservesBothLeaseAndStateErrors()
    {
        var states = new List<MutableState>();
        var broken = new TestDefinition(4)
        {
            Created = state =>
            {
                state.FailDispose = true;
                states.Add(state);
            },
            Activation = _ => new Lease(() => throw new InvalidOperationException("lease failure")),
        };
        var healthy = new TestDefinition(4) { Created = states.Add };
        var scene = SceneCompiler.Compile(Scene.World("arena",
            [Scene.Agent("a", broken), Scene.Agent("b", healthy)])).Mount();
        AggregateException error = Assert.Throws<AggregateException>(() => scene.Dispose());
        Assert.All(states, state => Assert.True(state.Disposed));
        Assert.Equal(new[] { "lease failure", "state failure" }, error.Flatten().InnerExceptions.Select(exception => exception.Message));
        Assert.Empty(scene.Agents);
        scene.Dispose();
    }

    [Fact]
    public void SpawnFailureDoesNotPublishOrReserveTheIdentityAndDespawnIsExplicit()
    {
        int releases = 0;
        var definition = new TestDefinition(4) { Activation = _ => new Lease(() => releases++) };
        using var scene = SceneCompiler.Compile(Scene.World("arena", [])).Mount();
        Assert.Throws<InvalidOperationException>(() => scene.Spawn("guard",
            definition with { Activation = _ => throw new InvalidOperationException("fail") }, SceneTransform.Identity));
        Assert.Empty(scene.Agents);
        var guard = scene.Spawn("guard", definition, SceneTransform.Identity);
        Assert.Throws<InvalidDataException>(() => scene.Spawn("guard", definition, SceneTransform.Identity));
        Assert.True(scene.Despawn("guard"));
        Assert.False(scene.Despawn("guard"));
        Assert.Throws<ObjectDisposedException>(() => { _ = guard.State; });
        Assert.Equal(1, releases);
        scene.Spawn("guard", definition, SceneTransform.Identity);
        scene.Dispose();
        Assert.Equal(2, releases);
        Assert.Throws<ObjectDisposedException>(() => scene.Project());
    }

    [Fact]
    public void ProjectionReadsLiveStateWithoutRespawningAndRetainsAuthoredOrientation()
    {
        int creations = 0;
        var definition = new TestDefinition(4) { Created = _ => creations++ };
        var transform = new SceneTransform(new(2, 0, 3), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f), new(2, 2, 2));
        using var scene = SceneCompiler.Compile(Scene.World("arena",
            [Scene.Agent("guard", definition) with { Transform = transform }])).Mount();
        var agent = scene.Agent<MutableState>("guard");
        agent.State.Position = new(8, 0, 9);
        agent.State.Health = 2;
        var first = Assert.Single(scene.Project().Boxes);
        Assert.Equal(new Vector3(8, 0, 9), first.WorldTransform.Translation);
        Assert.Equal(transform.Matrix().M11, first.WorldTransform.M11);
        Assert.Equal(first, Assert.Single(scene.Project().Boxes));
        Assert.Equal(1, creations);
        Assert.Equal(2, agent.State.Health);
    }

    [Fact]
    public void PresentationCannotCreateAgentsOrSmuggleInGameplayCollision()
    {
        var body = Scene.Group("body", [Scene.Box("shape", Vector3.One, Vector4.One, collision: SceneCollision.Solid)]);
        var definition = new TestDefinition(4) { Body = body };
        using var scene = SceneCompiler.Compile(Scene.World("arena", [Scene.Agent("guard", definition)])).Mount();
        Assert.Throws<InvalidDataException>(() => scene.Project());
        Assert.Single(scene.Agents);
    }

    [Fact]
    public void ContentIdentityTracksPlacementGeometryAndExplicitAgentConfiguration()
    {
        var definition = new TestDefinition(4);
        var document = Scene.World("arena", [Scene.Agent("guard", definition), Scene.Box("floor", Vector3.One, Vector4.One)]);
        string original = SceneCompiler.Compile(document).ContentIdentity;
        Assert.Equal(original, SceneCompiler.Compile(document).ContentIdentity);
        Assert.NotEqual(original, SceneCompiler.Compile(document with { Transform = SceneTransform.At(Vector3.UnitX) }).ContentIdentity);
        Assert.NotEqual(original, SceneCompiler.Compile(Scene.World("arena",
            [Scene.Agent("guard", definition with { Health = 40 }), document.Children[1]])).ContentIdentity);
    }

    private sealed class MutableState(Vector3 position, int health) : IDisposable
    {
        public Vector3 Position { get; set; } = position;
        public int Health { get; set; } = health;
        public List<int> Values { get; } = [];
        public bool Disposed { get; private set; }
        public bool FailDispose { get; set; }
        public void Dispose()
        {
            Disposed = true;
            if (FailDispose)
            {
                throw new InvalidOperationException("state failure");
            }
        }
    }

    private sealed record TestDefinition(int Health) : AgentDefinition<MutableState>(AgentTemplate.Creature("test.guard"))
    {
        public Action<MutableState>? Created { get; init; }
        public Func<SceneAgent<MutableState>, IDisposable?>? Activation { get; init; }
        public bool FailCreation { get; init; }
        public SceneGroup Body { get; init; } = Scene.Group("body", [Scene.Box("shape", Vector3.One, Vector4.One)]);
        public override string Identity => $"test.guard.v1:{Health}";

        public override MutableState CreateState(ScenePlacement placement)
        {
            if (FailCreation)
            {
                throw new InvalidOperationException("factory failure");
            }
            var state = new MutableState(placement.Position, Health);
            Created?.Invoke(state);
            return state;
        }

        public override IDisposable? Activate(SceneAgent<MutableState> agent) => Activation?.Invoke(agent);
        public override Matrix4x4 WorldTransform(MutableState state, ScenePlacement placement) => placement.At(state.Position);
        public override SceneGroup Present(MutableState state) => Body;
    }

    private sealed class Lease(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
