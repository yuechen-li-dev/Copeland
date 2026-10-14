# Platforming primitives

`Aurelian.Spatial3D` provides three pieces that 3D platformers and third-person games need. Each is pure: queries return facts, and the game owns state and timing. [Cloudstep](../../Games/Cloudstep/README.md) uses all three.

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

At slow speeds the forward probe advances at least `Radius · (1 − sin(MaximumSlope)) + 5 mm`, which is about 9 cm for a 0.3 m capsule at 45°. Below that distance the ledge corner would present a non-walkable normal. Faster movement advances by its own displacement. Stairs, kerbs and ledges up to `StepHeight` are covered by `PlatformingTests`.

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
