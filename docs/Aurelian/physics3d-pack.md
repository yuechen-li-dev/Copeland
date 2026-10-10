# Optional 3D rigid-body physics

Aurelian already provides CPU collision queries, grounded capsule movement,
slopes, sweep-and-slide and optional Vulkan ray queries through `Aurelian.Spatial3D`.
These are character/query tools. The optional BEPU backend adds rigid-body dynamics
without replacing them or making game logic depend on BEPU handles.

## Ownership and selection

Reference `src/Integrations/Aurelian.Physics3D.Bepu/Aurelian.Physics3D.Bepu.csproj`
when a game needs physics. This is an explicit optional dependency; ordinary
starter games do not reference BEPU or allocate a physics simulation.

`Aurelian.Physics3D` owns `IPhysicsWorld3D`, body descriptions, poses, velocities,
fixed-step requests, contact pairs and immutable inspection snapshots.
`Aurelian.Physics3D.Bepu` pins the stable NuGet release 2.4.0. BEPU and
its transitive BepuUtilities dependency use Apache-2.0; the upstream source and
license remain owned by [BEPU](https://github.com/bepu/bepuphysics2/tree/v2.4.0).
The callback adapter follows BEPU's public API and its
[self-contained example](https://github.com/bepu/bepuphysics2/blob/v2.4.0/Demos/Demos/SimpleSelfContainedDemo.cs).
There is no engine fork or GPU physics backend in this slice.

```csharp
using Aurelian.Physics3D;
using Aurelian.Physics3D.Bepu;
using System.Numerics;

using IPhysicsWorld3D physics = new BepuPhysicsWorld3D(new()
{
    FixedDeltaSeconds = 1f / 60,
    WorkerCount = 1,
    Substeps = 2,
});

physics.AddBody(new("floor", new PhysicsShape3D.Box(new(20, .5f, 20)),
    PhysicsPose3D.At(new(0, -.25f, 0)))
{
    MotionType = PhysicsMotionType3D.Static,
});
physics.AddBody(new("crate", new PhysicsShape3D.Box(Vector3.One),
    PhysicsPose3D.At(new(0, 3, 0)))
{
    SemanticOwnerId = "crate-agent",
    Mass = 2,
});

physics.ApplyImpulse("crate", new(4, 0, 0));
PhysicsStepResult3D step = physics.Step(new(physics.Tick + 1));
PhysicsSnapshot3D observation = physics.CaptureSnapshot();
```

Units are metres, kilograms and seconds, Y up. Boxes take full dimensions;
capsules take radius and the length of the straight segment, excluding caps.
Supported motion types are static, kinematic and dynamic. Mobile shapes are
boxes, spheres and capsules. Static triangle meshes reuse `CollisionMesh3D`.
The adapter converts Aurelian's counterclockwise outward triangle winding to
BEPU's clockwise one-sided convention. Raycasts reuse `Ray3D` and `QueryFilter3D`.
Physics collision filtering requires both bodies' layer/mask pairs to agree.
Impulse offsets are world-space offsets from the body's centre; velocities are
world-space vectors. `SetMotion` wakes a body and updates its query bounds.

Friction is per body and combined geometrically. Contact springs default to
30 Hz, damping ratio 1 and maximum recovery velocity 2 m/s, configurable through
`PhysicsWorldOptions3D.ContactSpring` and `MaximumRecoveryVelocity`. Restitution
is not exposed as an artificial independent coefficient. Sleeping is enabled by default.

## CCD and constraints

Bodies select `PhysicsContinuity3D.Discrete`, `Passive` or `Continuous`.
The default remains `Passive` with a 10 cm maximum speculative margin.
`Continuous` enables BEPU's swept collision detection; `MinimumSweepSeconds`
and `SweepConvergenceSeconds` default to 0.0001 seconds. These are time tolerances,
so their spatial error grows with speed. Detection does not make soft contact
response perfectly rigid. The lab preserves cases where swept CCD still fails
the penetration budget. See BEPU's [CCD documentation](https://github.com/bepu/bepuphysics2/blob/v2.4.0/Documentation/ContinuousCollisionDetection.md).

`PhysicsJoint3D` describes ball sockets, center distance limits, hinges and angular
motors without leaking BEPU constraint handles. Anchors and axes are body-local;
axes must be unit vectors. A spring has an explicit frequency and damping ratio.
At least one endpoint must be dynamic, and both endpoints must be mobile bodies:
use a kinematic body for a fixed joint anchor. Angular motor target velocity is
the relative velocity of A minus B along A's axis. For a stationary A, positive
target rotates B in the negative direction.
The motor's `MaximumTorque` is in newton-metres; `TargetVelocity` is in radians per second.

```csharp
physics.AddJoint(new PhysicsJoint3D.BallSocket(
    "hanging-crate", "anchor", "crate", Vector3.Zero, Vector3.UnitY)
{
    Spring = new(60, 1),
});
physics.AddJoint(new PhysicsJoint3D.AngularMotor(
    "drive", "anchor", "crate", Vector3.UnitY, 2, 100));
var authoredConstraints = physics.CaptureJoints();
physics.RemoveJoint("drive");
```

Connected collision is disabled by default (`CollideConnected = false`). Multiple
constraints on a pair retain that exclusion until the last excluding constraint
is removed. `SetCollisionEnabled(a, b, false)` adds an independent pair exclusion;
enabling that pair does not override a joint exclusion or collision layer/mask.
Removing a body removes its attached constraints and explicit exclusions before
releasing its handle. Reusing an author ID does not inherit exclusions.
`CaptureJoints` returns sorted authored descriptions, not solver warm-start state.

## Scene agents and cadence

`Aurelian.NativeComposition.PhysicsScene3D.AddStatics` adapts a
`SceneSpatial3D.Build(plan)` world, including authored Aetheris collision meshes.
It returns a lease that removes those bodies on disposal. Render-only meshes
are never implicitly made solid.

`PhysicsAgentDefinition3D` composes an ordinary `AgentDefinition<PhysicsBodyState3D>`:

```csharp
using var physics = new BepuPhysicsWorld3D();
using IDisposable statics = PhysicsScene3D.AddStatics(physics, SceneSpatial3D.Build(plan));
using SceneInstance scene = plan.Mount();

var crate = new PhysicsAgentDefinition3D(
    physics,
    new PhysicsBody3D("prototype", new PhysicsShape3D.Box(Vector3.One),
        PhysicsPose3D.At(Vector3.Zero)),
    Scene.Group("body", [Scene.Box("visual", Vector3.One, Vector4.One)]),
    "my-game/crate/v1");

scene.Spawn("crate-1", crate, SceneTransform.At(new(0, 3, 0)));
scene.Spawn("crate-2", crate with { Body = crate.Body with { Mass = 3 } },
    SceneTransform.At(new(2, 3, 0)));

physics.Step(new(physics.Tick + 1));
PhysicsScene3D.Publish(scene, physics);
SceneFrame renderFrame = scene.Project();
```

Mount/spawn registers one owned body with the scene agent's identity. Despawn or
unmount removes it. `DefinitionId` is an explicit author version. Definition identity
also hashes the typed body settings, collision shape, visual, backend and world
options; ordinary `with` customization changes scene content identity without
requiring a manual version bump. No reflection is used.
Agent placement is rigid: author size in the shape and visual. Scale/shear in
physics-agent placement fails with `AUR-PHYSICS-002`.

The simulation owns physical motion. After a physics step, `Publish` replaces
agent state with the backend's typed result; presentation reads this state.
Inspecting/projecting/rendering never advances physics. Agent logic stages
impulses or kinematic motion, the application resolves them in its explicit order,
then calls Step and publishes observations. The physics world must outlive its
scene and static-body leases.

Call Step exactly once per due tick in the existing `CadenceScheduler`, using a
dedicated physics cadence. The fixed delta is configured once; elapsed rendering
time is not a physics request. Tick gaps or duplicate ticks fail before simulation
mutation. Multithreading uses an owned BEPU dispatcher disposed with the world;
commands and inspection remain confined to the creating thread.

## Inspection and honest boundaries

Snapshots retain stable semantic IDs, poses, velocities, awake flags, backend
version and tick. They contain no BEPU handles. Step contact facts are sorted,
deduplicated touching pairs observed by collision detection during that step.
They are not solved impulse, trigger or begin/end events; sleeping pairs need
not be reported again. Set `CollectContacts = false` when this observation is
unnecessary.

The backend enables BEPU's deterministic mode. Tests qualify exact fresh-world
replay for a fixed sequence on this runtime/backend. This is not cross-platform
lockstep certification. A pose/velocity snapshot is inspection data, not an exact
BEPU checkpoint: it excludes solver warm starts, island/sleep history and caches.
Exact rewind requires replaying ordered commands from a fresh world or a future
qualified backend checkpoint seam. No automatic Deliverance save participant or
Dominatus solver-cache rewind is claimed.

This slice does not wrap compound/convex hull creation, sensors, ragdoll authoring,
BRep dynamics, GPU simulation or a dynamic character controller. Fast thin
collisions require an explicitly qualified detection/contact profile; the default
uses passive/speculative contacts. Dynamic/kinematic triangle meshes fail explicitly
with `AUR-PHYSICS-001`. Existing character movement remains independent; dynamic
body/character pushing is not inferred from two separate collision worlds.

## Validation and performance

```powershell
dotnet build Aurelian.slnx -m:1
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet test Aurelian.slnx --no-build -m:1 -- RunConfiguration.MaxCpuCount=1
dotnet build tools/Aurelian.GraphicsProof -c Release -m:1
$env:DOTNET_TieredCompilation = "0" # keep runtime compilation policy fixed for measurements
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --physics --output artifacts/local/physics
```

The proof exercises 256, 1,024 and 4,096 dynamic boxes on a floor, with one and up
to four workers, 120 warmup and 180 measured steps at 60 Hz. Sleeping is disabled
and verified: it does not report thousands of sleeping bodies as an active-body
benchmark. Contact collection, sorting and output allocations are included in
step cost. Snapshot inspection is timed separately. Initialization, rendering,
PNG capture and snapshot serialization are excluded from step cost.
The evidence records the runtime compilation policy; timings from different
policies should not be treated as an isolated worker-count comparison.

The native demonstration uses scene-agent composition and the shared Vulkan
renderer. It captures falling crates, stacked contacts, an off-centre impulse,
and the settled result; `snapshot.json` contains the resulting typed state.
`evidence.json` starts unaccepted and becomes accepted only after benchmarks,
scene-state agreement, ground support and native rendering checks pass. Timings
are local workload measurements, not a comparison against another engine or a
hardware-independent performance guarantee.

The qualified local Release run (`.NET 10.0.12`, x64, 16 logical processors,
fixed JIT policy) produced these CPU step measurements, with contact collection enabled:

| Awake dynamic boxes | One worker median | Four workers median | Four workers p95 |
| --- | ---: | ---: | ---: |
| 256 | 0.149 ms | 0.171 ms | 0.186 ms |
| 1,024 | 0.613 ms | 0.412 ms | 0.437 ms |
| 4,096 | 2.805 ms | 1.468 ms | 1.553 ms |

Small scenes can be cheaper on one worker. The 4,096-body four-worker path
allocated approximately 66 KB per step, including immutable contact output.
The complete Aurelian solution passed 1,085 tests, including 14 new backend and
agent-integration tests; the native proof passed with Vulkan validation enabled.
Local evidence is written to `artifacts/local/physics/evidence.json`.

The follow-up [physics qualification lab](physics3d-qualification-lab.md) adds
stress profiles, direct-BEPU controls, moving platforms, constraint/lifecycle
checks and one experimental cloth specimen. Run it with `--physics-lab`; select
a case with `--case ccd` (or `stacks`, `rotation`, `platform`, `joints`, `lifecycle`,
`sleep`, `replay`, `cloth`). `--headless` skips Vulkan captures. Its completed
experiment retains intentionally failed profiles; completion is not universal
physics certification.
