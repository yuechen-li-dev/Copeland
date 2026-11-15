# Platforming primitives

`Aurelian.Spatial3D` provides collision, moving-platform, camera and movement facilities for 3D platformers and third-person games. Queries return facts, while the game owns time, rules and actor lifetimes. [Cloudstep](../../Games/Cloudstep/README.md) consumes these through the reusable character controller below.

## Platformer character controller

`PlatformerCharacter3D` extracts Cloudstep's movement implementation into the engine. It owns per-character movement state on top of the existing `CharacterMotor3D`: separate ground acceleration/deceleration and air control, facing, coyote time, jump buffering, variable jump height, fall-speed limiting, wall velocity rejection, edge slipping, platform carry/yaw and inherited platform momentum. Course geometry, camera-relative input rotation, animation, sound, damage, checkpoints and respawn rules remain in the game.

```csharp
var hero = new PlatformerCharacter3D(new PlatformerSettings3D
{
    RunSpeed = 7,
    CoyoteTime = .12f,
    JumpBuffer = .15f,
    JumpCut = .45f,
    Collision = new CharacterMotorOptions3D(Radius: .35f, Height: 1.4f,
        GroundSnap: .12f, StepHeight: .4f, StepDownHeight: .35f, LedgeAssistHeight: .45f),
});
hero.Place(spawnFeet, facing: 0);

// Move authored kinematic bodies before stepping characters. For a static query world, no platform phase is needed.
platformWorld.BeginStep();
platformWorld.SetTransform("lift", LiftPose(simulationTime));
PlatformerStep3D result = hero.Step(platformWorld, new(worldSpaceMove, jumpPressed, jumpHeld), fixedSeconds);
// hero.Feet / Facing are accepted output; result reports jump, landing, crush and step-up events.
PlatformerSnapshot3D saved = hero.Capture();
hero.Restore(saved);
```

`Step` accepts either an ordinary `ISpatialQueryWorld3D` or a `KinematicSpatialWorld3D`. Only the latter supplies automatic carry, push, yaw and point velocity. Input movement is in world XZ; rotate camera-relative controls before calling it. Steps must be finite and in (0, .05] seconds. Each character needs its own controller. `Place` clears momentum and grace timers; snapshots retain them for exact continuation. Restore the world's platform transforms with the character snapshot. The caller validates support identities against its own world, as Cloudstep does.

`PlatformerSettings3D.Collision` configures body, slope, step and ledge dimensions. Its computed `Motor` takes gravity and jump speed from the movement settings. Settings, placement, input and restored numeric state are validated before live state changes. Setting either grace interval to zero still permits a direct grounded jump. Releasing jump cuts upward speed once, rather than repeatedly reducing it every frame.

Cloudstep's `CloudstepPlayer` is now a small adapter, retaining `PlayerTuning`, `PlayerSnapshot`, game events and the published save JSON shape. Its nine game tests still cover the complete course, short/held and late jumps, hazards, crushing and exact mid-ride restoration. Independent engine tests cover buffered landing, grace expiry, no double jump, zero-grace jumping, platform momentum, static worlds and invalid-state rejection. The extraction's before/after headless observation traces are byte-identical. Its native tour produces 14 captures and the same final facts: zero falls, nine gems, and feet at (12.501, 7, -90.844). The script ends at course clock 34.7 s; the course-clear HUD shows finish time 0:32.5.

This is an authored platformer movement style, not a rigid-body character solver. It does not add crouch/stand-up rules, double jumps or arbitrary gravity. The existing motor remains available for games needing different movement rules.

## Step-up

```csharp
var motor = new CharacterMotor3D(new CharacterMotorOptions3D(Radius: 0.35f, Height: 1.4f, StepHeight: 0.4f));
CharacterMove3D move = motor.Step(world, state, horizontalVelocity, jump: false, seconds);
// move.SteppedUp > 0 when the character climbed a ledge this step.
```

`StepHeight` defaults to zero, so existing motors keep their behaviour. The motor tries a step only when three conditions hold:

- it started the step grounded;
- a wall-like contact blocked horizontal progress;
- there is headroom.

The step goes up, then forward, then down. It succeeds only if the landing is walkable, no higher than `StepHeight` above the feet, and the character still fits when standing on the ledge top. A low ceiling therefore prevents stepping rather than wedging the character on the corner.

The step starts from where the ordinary move stopped against the ledge. It continues along the requested direction, plus just enough straight into the ledge, measured along the wall normal, for the corner to become walkable: `Radius · (1 − sin(MaximumSlope)) + 5 mm`, about 9 cm for a 0.35 m capsule at 50°. Slow and glancing approaches therefore climb instead of stalling or sliding along the first tread, without popping sideways. `EdgeHandlingTests` covers stairs at 0–80° and 2–7 m/s.

## Edge handling and platformer assists

| Option or fact | Effect |
| --- | --- |
| Steep contacts | A surface too steep to stand on blocks upward climbing like a wall, but still lets the character slide down and off it. Falling onto a ledge corner never leaves it hovering. |
| Shallow overlaps | At the start of each step, `Depenetrate` pushes the capsule out of overlaps up to `MaximumCorrection` (5 cm) left by float error at seams and corners. Deeper overlaps stay `Embedded`, so crushes are still reported. An overlap first seen partway through a slide is treated as float error, not embedding. |
| `StepDownHeight` | Sticky ground. A walking character follows stairs and small drops down (it stays `Grounded`, with no hop and no landing) while its axis is above a walkable tread within that height. |
| `LedgeAssistHeight` | Forgiveness for short jumps. An airborne character at or past its apex (upward speed ≤ `LedgeAssistMaximumRise`) that pushes into a ledge whose top is within this height is lifted onto it. Reported as `LedgeAssisted`. |
| `CentreSupported` | When grounded: is there ground directly under the capsule's axis? False means it is perched on a corner with its centre over a drop. Games decide what to do; Cloudstep slips the hero off unless the player walks back onto the ledge. |

Coyote time, jump buffering and variable jump height now have a reusable implementation in `PlatformerCharacter3D`; games may also compose their own rules over the motor facts.

## Kinematic bodies (moving platforms)

```csharp
var world = new KinematicSpatialWorld3D(statics, [new KinematicBody3D("lift", CollisionMesh3D.Box(new(1.5f, 0.2f, 1.5f)))]);

// Every simulation step:
world.BeginStep();                                  // current pose becomes previous pose
world.SetTransform("lift", Pose(time));             // rigid transforms only
KinematicCarry3D carry = world.Carry(Capsule3D.AtFeet(feet), supportId);
CharacterMove3D move = motor.Step(world, state, velocity, jump, dt, carry: carry.Displacement, carryWorld: world.Statics);
supportId = move.GroundColliderId;                  // ride it again next step
bool crushed = move.Embedded || !world.Overlap(Capsule3D.AtFeet(move.State.Feet)).IsEmpty;
```

- Static geometry stays an immutable `SpatialWorld3D` and keeps its rebuild-on-change contract. Each kinematic body's triangles are built once in local space; queries are transformed into the body's frame. Raycast, sweep, overlap and slide results match an equivalent static world (tested).
- **Carry**: the support body moves the feet by `current(previous⁻¹(feet)) − feet`, and `carry.Yaw` reports its rotation. Turntables rotate riders about their centre exactly.
- **Push**: any other body that moved into the capsule pushes it by the body's motion at the capsule centre.
- The carry is swept against `world.Statics`, so a platform cannot push a character through a wall. If the character ends up embedded, that is a crush, and the game decides what happens (Cloudstep respawns).
- **Momentum**: `PointVelocity(id, point, dt)` gives a rider's platform velocity, so a jump can inherit it.
- Snapshots: `Restore(id, previous, current)` sets both poses exactly.

## Camera boom

```csharp
var boom = new CameraBoom3D(radius: 0.25f, margin: 0.1f, extendRate: 5);
Vector3 eye = boom.Update(world, pivot, directionBehindPlayer, desiredLength: 7.5f, frameSeconds);
```

The boom sweeps a sphere from the pivot toward the desired eye. It shortens immediately when blocked and extends again at `extendRate` metres per second once the obstruction clears. `Clearance` returns the unsmoothed distance. Call `Reset()` after cuts or respawns.
