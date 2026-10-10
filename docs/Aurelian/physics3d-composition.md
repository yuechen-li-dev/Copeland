# Character/platform coupling and authored physics assemblies

`Aurelian.Physics3D` owns immutable assembly definitions, named attachment frames,
typed interfaces and character state. BEPU remains the optional solver and live
query backend. `Aurelian.NativeComposition` provides scene-agent activation,
publication and cleanup. No CAD dependency or backend handles enter game code.

## Assembly authoring

This follows Aetheris' useful separation: the occurrence tree describes ownership
and reuse; the interface graph describes mechanical connections. Containment
does not create a joint. Body-local ports use metres, Y up, with local Z as the
revolute axis and local X as the initial angular reference.

```csharp
using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Physics3D;

var pivot = new PhysicsPose3D(Vector3.Zero,
    Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2));
var ports = ImmutableDictionary<string, PhysicsPose3D>.Empty.Add("pivot", pivot);
var platform = new PhysicsAssembly3D("platform")
{
    Parts =
    [
        new(new("anchor", new PhysicsShape3D.Box(new(.2f)), PhysicsPose3D.At(Vector3.Zero))
        {
            MotionType = PhysicsMotionType3D.Kinematic,
        }) { Ports = ports },
        new(new("deck", new PhysicsShape3D.Box(new(4, .3f, 3)), PhysicsPose3D.At(Vector3.Zero)))
        {
            Ports = ports,
        },
    ],
    Interfaces =
    [
        new PhysicsInterface3D<Revolute3D>("bearing", new("anchor", "pivot"), new("deck", "pivot"))
        {
            Drive = new(.6f, 500),
        },
    ],
    Expose = ImmutableDictionary<string, PhysicsEndpoint3D>.Empty.Add("pivot", new("anchor", "pivot")),
};

CompiledPhysicsAssembly3D compiled = platform.Compile();
using PhysicsAssemblyLease3D occurrence = compiled.Mount(physics, "lift", PhysicsPose3D.At(new(0, .75f, 0)));
```

Available interface kinds are `Fixed3D` (BEPU weld), `Revolute3D` (hinge, optional
angular drive) and `Spherical3D` (ball socket). Generic dispatch is static and
explicit; there is no reflection discovery. A drive's positive velocity rotates
B relative to A about the port's Z, in radians/second; maximum torque uses N·m.
The adapter translates this into the raw BEPU motor's opposite sign.

Each interface aligns B's member frame to A's frame at compilation, including
orientation. B's authored placement is replaced; unconnected roots keep theirs.
A member can be a part or a whole child occurrence. Attaching a child moves all
its bodies together and preserves its internal joints. Placement cycles and
multiple drivers are rejected rather than invoking an implicit CAD solver.
Joint endpoints must be mobile and at least one must be dynamic; fixed world
anchors are kinematic bodies. Connected collision is disabled by default.

`Children` contains `PhysicsAssemblyOccurrence3D(id, definition, placement)`.
Repeated immutable definitions are compiled once per compile invocation, with
distinct paths such as `lift/left/deck` and `lift/right/deck`. Parents address a
direct child and one of its exposed aliases. Private internal paths fail with
`AUR-ASSEMBLY-007/008`. Exposed aliases retain the original body-local frame;
compiled `Connections` retain endpoint and underlying body bindings for inspection.

Compilation and mount preflight validate frames, identities, shapes and joint
endpoints before allocating bodies. Mount rolls back its additions on failure;
its lease releases only the occurrence's joints and bodies. Dispose leases before
their world, and avoid manually replacing bodies owned by an active lease.
`Capture()` returns ordinary typed state; `PortPose(alias)` resolves a live frame.

`PhysicsAssemblyAgentDefinition3D(world, definition, version)` mounts an assembly
as one object agent. `Visuals` maps relative body paths to presentation-only scene
fragments. Spawn/despawn owns the mount lease; `PhysicsScene3D.Publish` copies
solver output into the agent record. `with` changes to physical settings, ports,
drives and visuals change its explicit content identity.

## Character cadence

`PhysicsCharacterMotor3D` extends the existing grounded capsule motor with live
BEPU queries, support-body poses, inherited velocity and bounded horizontal push
impulses. The caller owns `PhysicsCharacterState3D`; the motor keeps no hidden
simulation state. The accepted character capsule is kinematic and upright.

```csharp
var character = new PhysicsCharacterAgentDefinition3D(physics, visualAtFeet, "player/v1");
var player = scene.Spawn("player", character, SceneTransform.At(spawnFeet));

// Once per fixed tick: command platforms, resolve physics, move characters, publish.
physics.Step(new(physics.Tick + 1));
character.Move(player, new(physics.Tick, horizontalVelocity, jumpPressed));
PhysicsScene3D.Publish(scene, physics);
SceneFrame frame = scene.Project();
```

The support's previously recorded pose and local foot position reconstruct the
bottom sphere's centre, which transforms through its resolved new pose before
recovering upright feet. This carries translation, elevation, yaw, pitch and roll without
double-integrating kinematic velocity. Carry is swept against other bodies, so
walls can block it. On jumping or leaving support, the character inherits its
carrier velocity and detaches. Walking and ground snapping use the shared slope
and sweep-and-slide policy; upward movement stops at ceilings.

Push impulses are capped at `MaximumPushForce * fixedDeltaSeconds` per contacted
dynamic body and affect the next solver step. This caps the controller's explicit
impulses; BEPU's normal contact response remains active. Initial embedded spawns
fail with `AUR-CHARACTER-001` and roll back agent activation. Missed/repeated ticks,
external relocation or mismatched capsule definitions fail explicitly. Use
`Initialize` after deliberate relocation. Publication catches characters whose
movement phase was skipped. Fixed steps must be at most 50 ms.

`CreateQueryWorld(excludedIds)` queries live boxes, spheres, capsules and static
meshes through `ISpatialQueryWorld3D`, using mutual layer/mask filtering. It never
steps physics or rebuilds a triangle world. Explicit pair/joint solver exclusions
are separate from query exclusions. Sweeps treat target geometry at its current
resolved pose; they do not predict other bodies' future trajectories. A zero-time
overlap has no invented normal, and the shared movement result preserves that
diagnostic instead of performing hidden depenetration. Mesh ray hits retain source
and triangle/face provenance; native sweep callbacks do not provide a face index.

## Qualification

Run the real solver and Vulkan scene specimen:

```powershell
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --physics-composition
```

It mounts two occurrences of one motorized platform definition, carries one scene
character through translation and rotation, jumps with inherited velocity, then
publishes and renders solver output. It checks that repeated renders do not advance
physics, and writes assembly bindings, character states, screenshots and fail-first
`evidence.json` under `artifacts/local/physics-composition`.

The focused tests also exercise tilting support, wall-blocked carry, crate pushing, support removal,
slopes, ceilings, invalid activation, cadence, query lifetime/thread ownership,
private ports, repeated children, whole-child placement, asymmetric fixed frames,
invalid graphs, definition identity and despawn cleanup. This is a bounded rigid
assembly and capsule controller path, not URDF import, ragdolls, step climbing or
automatic crush recovery.

Validated on 2026-10-10: `dotnet build Aurelian.slnx -m:1` passed, followed by
1,120 passing tests across 29 projects with `AURELIAN_NAGA` configured (49 in the
BEPU suite). The final Release Vulkan specimen passed on an RTX 3070 with
`VK_LAYER_KHRONOS_validation`: 180 ticks, 6 bodies, 4 joints, maximum grounded
platform-local drift 0.182 mm and jump peak 2.015 m. The small specimen's combined
step/character/publication p95 was 0.0402 ms, excluding rendering and its first
30 ticks; this does not qualify large character crowds. Local logs are
`artifacts/local/physics-composition-final-build.log`,
`physics-composition-final-tests.log` and `physics-composition-final-run.log`.
