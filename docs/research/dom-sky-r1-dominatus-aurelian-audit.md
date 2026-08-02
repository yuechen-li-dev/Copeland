# DOM-SKY-R1 Dominatus and Aurelian audit

## Result

Source inspection and executable tests show that the existing architecture can
host a Skyrim-bound agent without making Dominatus or Aurelian depend on
Skyrim. The reusable seam is `IHostPresenterBackend`: Dominatus selects an
ordinary immutable `MoveToward` command, Aurelian owns correlation and action
lifecycle, and a backend projects host observations and results.

The live experiment reached **Outcome B**. The complete immutable loop ran in
Skyrim, but the selected actuator is a bounded direct displacement, not
animation-aware locomotion.

## Inspected sources

This audit read implementation and tests rather than inferring behavior from
names. The main sources were:

- Dominatus `HfsmGraph`, `HfsmInstance`, `Ai.Decide`, `Ai.Act`, `Ai.Await`,
  `ActuatorHost`, blackboard, trace, and replay implementations at Dominatus
  revision `9b43e7912332856e6095d62c530f58049b1b5150`;
- FishTank, TinyTown, and MonoGame RTS samples in the Dominatus repository;
- Aurelian Actuation host contracts, backends, and tests;
- Aurelian Marionette transport framing, authentication, scenario clients, and
  tests;
- Marionette presenter protocol, policy, dispatcher, host-transfer runtime,
  and native tests.

## Existing reusable contract

### Dominatus

- Agent definition is an explicit `HfsmGraph` plus an `HfsmInstance`; state
  nodes yield typed `AiStep` values.
- Utility selection is `Ai.Decide` over ordered `Ai.Option` values and
  `Consideration` scores. `DecisionReport` records every option score, the
  winner, switching decision, and reason.
- Transitions are explicit `Ai.Goto` effects. Stable states yield `Ai.Steady`.
- External work is an ordered `Ai.Act` followed by `Ai.Await`; the
  `ActuatorHost` correlates the result through an `ActuationId` and immutable
  blackboard payload.
- Replay is already a first-class concern through `ReplayDriver`; it replays
  recorded value inputs against the same deterministic graph.
- The samples confirm the intended boundary. In particular, TinyTown leaves
  navigation/body realization inside the engine adapter while Dominatus owns
  goal and option selection.

There was no Skyrim actor abstraction in Dominatus, and none was added.

### Aurelian.Actuation

- `HostCommandRequest` is an immutable correlated command with request ID,
  expected host generation, bounded timeout, semantic command kind, and typed
  immutable arguments.
- `IHostPresenterBackend.SubmitAsync` returns an immutable receipt;
  `ObserveAsync` produces ordered immutable observations.
- `HostActionRunner` implements receipt rejection, completion observation,
  timeout, and caller cancellation without host-specific types.
- Existing presenter/session commands, generation checks, and value-only
  observation envelopes are reusable.
- The existing transport is authenticated named-pipe protocol v1. This spike
  adds a message kind to that protocol; it does not create a second protocol.

There was no general capability snapshot, actor value identity, semantic
goal-directed movement request, fake host backend, or presenter replay backend.

## Small required extension

The spike adds these host-independent Aurelian values:

```csharp
public readonly record struct HostActorId(uint FormId, ulong Generation);

public sealed record HostActorObservation(
    HostActorId ActorId,
    HostPosition3 Position,
    float? HeadingRadians,
    HostVelocity3? Velocity,
    HostActorLifeState LifeState,
    HostActorMovementState MovementState,
    bool Loaded,
    uint? CurrentCellFormId,
    HostActorId? CurrentTarget,
    float? DistanceToGoal,
    HostActionState ActionState,
    HostCapabilitySnapshot Capabilities,
    ulong Sequence);
```

Unavailable data is nullable. Velocity and current target are not fabricated
by the live adapter. Sequence is monotonic runtime observation sequence.

```csharp
public sealed record MoveTowardArguments(
    HostActorId ActorId,
    HostPosition3 TargetPosition,
    float StoppingDistance,
    float MaximumDistance,
    HostMovementSpeedPolicy SpeedPolicy,
    ulong ExpectedObservationSequence);
```

Validation requires a nonzero matching generation, finite target, stopping
distance from 0 through 256 units, maximum displacement greater than 0 and no
more than 64 units, a nonzero observation sequence, and a timeout no longer
than ten seconds. No key press, pointer, CommonLib value, script, or arbitrary
method name crosses the contract.

Action terminal states now distinguish rejected, failed, timed out, blocked,
interrupted, target invalid, actor unloaded, unsupported, and engine refused.
Accepted and running remain observable nonterminal states.

## Capability contract

`HostCapabilitySnapshot` independently describes bounded direct displacement,
animated locomotion, goal-directed movement, camera following, actor
activation, attack, jump, and sneak as `Unsupported`, `Experimental`, or
`Supported`. The experiment reports:

| Capability | Live classification | Evidence |
|---|---:|---|
| Bounded direct displacement | Supported | Existing and DOM-SKY-R1 live runs |
| Animated locomotion | Unsupported | No proven controller/pathing implementation |
| Goal-directed movement | Experimental | Semantic command composes one bounded displacement |
| Camera following | Supported | Active-host session and restoration proofs |
| Activation, attack, jump, sneak | Unsupported | Not implemented or exercised |

The approach option cannot score true unless `CanMoveToward` is available.

## Ownership boundary

| Owner | State and transitions |
|---|---|
| Dominatus | Goal, utility scores, selected semantic action, immutable domain state, one-retry policy, interruption/fallback policy, completion-driven HFSM transition |
| Aurelian.Actuation | Immutable command/observation/result types, request ID, actor/host generation, capability snapshot, expected observation sequence, accepted/running/terminal lifecycle, timeout and cancellation semantics, backend abstraction |
| MarionetteSSE | FormID resolution, loaded/dead/process availability, safe task dispatch, position/heading/cell projection, camera and active-host state, native application, engine failure projection, restoration |
| Skyrim backend local state machine | For this spike: validate -> resolve -> measure -> bounded displacement -> verify -> complete/refuse. Future locomotion should locally own idle/moving/stopping, grounded/jumping/falling, sneak transitions, animation recovery, and path/controller refusal |

Low-level body-control transitions do not belong in Dominatus unless a future
semantic goal genuinely needs to reason about them.

## Actor binding

`SkyrimActorBinding(FormId, RuntimeGeneration)` is an immutable value binding.
The command repeats both values and Marionette re-resolves the actor on the
runtime task. Zero identity, stale generation, wrong active-host FormID,
unloaded/deleted actor, or dead actor fail explicitly. The binding is not tied
to PlayerCharacter; the live proof used the Eternal Dragonborn presented host,
and the same shape can represent a selected ordinary NPC later.

## Fake, replay, and tiny agent proof

`DeterministicHostPresenterBackend` emits immutable observations, advances by
at most the command bound, emits accepted/running/completed, and injects
terminal failures. `ReplayHostPresenterBackend` replays captured observation
envelopes through the identical interface.

The ordinary C# authoring proof is:

```csharp
var actor = SkyrimAgent.Define(
    id: "skyrim-approach-spike",
    binding: new SkyrimActorBinding(formId, runtimeGeneration),
    goal: new ReachTargetGoal(target, stoppingDistance: 16.0f),
    option: new ApproachTargetOption(
        maximumDistance: 64.0f,
        HostMovementSpeedPolicy.Walk,
        maximumRetries: 1));
```

Internally it uses the existing HFSM, utility, blackboard, ordered act/await,
and actuator host. It does not introduce a second agent framework or DSL.

Tests prove immutable equality, deterministic utility choice, deterministic
fake movement, completion transition, blocked retry/fallback, capability
gating, stale observation rejection, generation mismatch, replay determinism,
and cancellation/timeout projection.

## Skyrim-specific backend concern

The C# pipe adapter maps only scalar protocol values and implements the same
`IHostPresenterBackend` used by fake and replay. CommonLib types, actor
pointers, native handles, and engine objects stay in Marionette. Native access
occurs only after `SKSE::TaskInterface` dispatch.

The synchronous native command reports all lifecycle states together because
the selected displacement completes within one runtime task. The Aurelian
backend projects that ordered lifecycle to Dominatus and then queries a fresh
actor snapshot for terminal state and sequence.

## Speculative future work

- Collision-aware multi-frame locomotion using a proven character-controller
  or Skyrim pathing surface.
- Captured live observation traces as durable replay fixtures.
- Binding ordinary NPCs outside an active-host session.
- More semantic actions only after each capability is independently proven.
- Agent persistence, multi-agent coordination, combat, dialogue, schedules,
  quests, inventory reasoning, and LLM inference remain outside DOM-SKY-R1.
