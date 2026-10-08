# Document scenes and typed agents

A scene is an immutable document of named nodes. Groups establish local transforms and instance namespaces. Fragments can be reused without sharing live state. Ordinary C# records and `with` customize an agent's configuration; mounting the document creates its runtime instances.

`Aurelian.World.Scenes` owns the portable authoring, compiler, typed state handles and explicit lifecycle. It requires neither graphics nor Dominatus. `Aurelian.NativeComposition.SceneGeometry3D` projects box and triangle facts through the existing native geometry/renderer path. `Aurelian.Runtime.Dominatus.Inspection.ScenePolicyBinding` attaches policy instances to the existing inspected Dominatus world; it introduces no additional clock or scheduler.

## Author a game scene

```csharp
using System.Numerics;
using Aurelian.Games;
using Aurelian.World.Scenes;

var target = new StarterTargetDefinition(Health: 3, HalfSize: new(0.8f, 1, 0.8f));
var checkpoint = Scene.Group("checkpoint",
    [Scene.Agent("target", target, at: new(0, 1, 0))]);

var document = Scene.World("training-range",
[
    Scene.Box("floor", new(24, 0.2f, 24), new(0.2f, 0.3f, 0.35f, 1), at: new(0, -0.1f, 0), collision: SceneCollision.Solid),
    Scene.Agent("player", new StarterPlayerDefinition(), at: new(0, 0, 7)),
    Scene.Instance("left", checkpoint, at: new(-3, 0, -4)),
    Scene.Instance("right", checkpoint, at: new(3, 0, -4)),
    Scene.Agent("captain", target with { Health = 6 }, at: new(0, 1, -6)),
]);

GameStarter.Run("my-game", GamePresets.FirstPersonShooter, args, sceneDocument: document);
```

The resulting agents are `player`, `left.target`, `right.target` and `captain`. The fragment definition's root label does not introduce an additional namespace; its transform does apply. The world root is metadata rather than an instance prefix. Every nested group and instance contributes a path segment. Segments use ASCII letters, digits, hyphens or underscores; dots separate segments and cannot appear inside them. Duplicate identities fail during compilation.

`Scene.Box` takes full dimensions, while its compiled fact exposes half sizes. Collision is explicit and defaults to `None`. `Scene.Mesh` takes immutable vertices with positions, unit normals and finite colors, with optional indexed topology, collision layer/mask, closed-solid declaration and source/face identities. An empty index list retains the original consecutive-triangle convention. Closed collision validates consistently oriented manifold edges. Authoring helpers copy input collections into `ImmutableArray` values.

Transforms use metres and Y up, with local scale followed by rotation and translation. Child transforms compose with their parent; full affine matrices preserve nested rotation and nonuniform scaling, including resulting shear. Positive scales and unit quaternions are required. Singular, mirrored, nonfinite or overflowing transforms fail explicitly. The graphics adapter applies inverse-transpose normal transforms.

## Define an agent with ordinary typed code

```csharp
public sealed record GuardState(Vector3 Position, int Health);

public sealed record GuardDefinition(int MaximumHealth)
    : AgentDefinition<GuardState>(AgentTemplate.Creature("my.guard"))
{
    public override string Identity => $"my.guard.v1:{MaximumHealth}";

    public override GuardState CreateState(ScenePlacement placement)
    {
        return new GuardState(placement.Position, MaximumHealth);
    }

    public override Matrix4x4 WorldTransform(GuardState state, ScenePlacement placement)
    {
        return placement.At(state.Position);
    }
}
```

Add the `Aurelian.World.Agents` and `Aurelian.World.Scenes` imports alongside `System.Numerics`. Override `ValidatePlacement` for pure application placement/configuration checks, `Present` for a visual body, and `Activate` for an owned behavior lease. `Identity` explicitly includes the schema revision and configuration that affect gameplay or presentation. It is not inferred from delegates or reflected fields. Include deterministic invariant formatting when configuration contains numeric values.

`with` copies configuration shallowly. Definitions must therefore contain immutable values, reusable immutable body definitions and factories. Each factory allocates fresh mutable per-agent state and a fresh brain. Shared service references are permitted; a captured live blackboard, mailbox or mutable agent state is not a prototype. Aurelian rejects registering the same live Dominatus brain twice in one world.

## Activate, inspect and dispose

```csharp
ScenePlan plan = SceneCompiler.Compile(document);
using SceneInstance scene = plan.Mount();
SceneAgent<int> captain = scene.Agent<int>("captain");
captain.State -= 1;

scene.Spawn("extra", target with { Health = 10 }, SceneTransform.At(new(5, 1, -4)));
scene.Despawn("extra");

SceneFrame frame = scene.Project();
```

Compilation validates the entire document before calling state or activation factories. Mount creates agents in canonical ID order, then activates them. A failed mount releases all already-created agents and leases. Failed single-agent spawning does not publish or reserve the identity. Factory/activation implementations must release any resources they allocate before throwing if they have not returned ownership to the scene.

Typed `SceneAgent<TState>` handles own the authoritative `GameAgent<TState>`. Updating `State` changes that instance; nesting does not define teams, targets or policy ownership. `WorldTransform` reads current domain state, so rendering never reinitializes placement or health. Body plans are retained while `Present` returns the same immutable body object. Bodies are presentation only: they cannot spawn agents or declare collision. A visibility predicate can omit bodies for first-person presentation without removing the agent.

For a Dominatus policy, an agent definition's activation hook can return:

```csharp
return ScenePolicyBinding.Bind(agent, runtime, MyFlow.Definition.CreateBrain);
```

Use the explicit advanced integration namespace `Aurelian.Runtime.Dominatus.Inspection`. The scene owns that lease. Despawn/unmount removes the policy from the existing runner and inspector; application cadence still calls the existing runtime's `Tick`. Stable scoped IDs identify observations and traces. Beacon Run uses this path for every creature and detaches dead creature policies before removing their scene instances at the next wave.

Unmount disposes activation leases and current disposable domain states. Immutable state replacement is ordinary assignment; replacing a resource-owning state requires application-managed retirement of the old state. Definitions and compiled plans can be mounted again to create independent worlds.

## Persistence and current scope

`ScenePlan.ContentIdentity` hashes compiled instance identities, transforms, box/mesh data, templates and explicit agent definition identities. It does not serialize runtime state or policy closures. Deliverance remains the owner of save slots; games provide typed source-generated snapshots and validation. The starter uses the content identity in its save compatibility checks and restores target health in canonical instance-ID order. Reordering agent declarations leaves this mapping stable. Reordering geometry may change draw order and the content identity.

Existing Dominatus policy checkpoints remain policy checkpoints. Exact game rewind continues through the existing input replay path. Scene composition does not make arbitrary application effects or external save writes transactional.

This C# implementation covers document groups, fragment instances, boxes, indexed meshes, typed agent factories and explicit lifecycle. The starter and Beacon Run consume it. `SceneCollision` is an authored fact projected into the shared `Aurelian.Spatial3D` query world by `SceneSpatial3D`; visuals do not silently become collision. Queries return contact facts and accepted displacement; games retain typed state and their existing cadence. Optional Vulkan rays use the same transformed geometry and provenance. See [the game starter](game-starter.md#aetheris-room-and-vulkan-ray-queries) for Aetheris baking, representation boundaries and hardware admission.

TinyFarm's existing semantic navigation/collision/occlusion compiler remains its owner; its scene migration and a Copeland TS scene frontend are follow-up work. Cameras and controls continue to use the existing composed game concepts and camera API.
