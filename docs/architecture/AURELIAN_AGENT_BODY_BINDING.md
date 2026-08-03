# Aurelian agent/body binding

## Authority

An Aurelian agent is the atomic gameplay entity. It owns semantic identity,
persistent state, composed typed data, behavior, mailbox/events, capabilities,
actuator access, and presentation/body bindings. A Skyrim actor is a
materialized body; it is not the agent.

> Aurelian authors gameplay in C#. Marionette lowers agent observations and commands into Skyrim runtime operations.

> Skyrim is the materialization backend, not the authoritative gameplay model.

This is a compiler/runtime boundary: typed Aurelian policy and Dominatus flow
produce portable obligations. Marionette lowers those obligations over the
existing named-pipe and SKSE task-queue path. Skyrim executes the mutation and
returns observations.

## Identity layers

`AgentId(Guid Value)` is the semantic gameplay identity. It remains valid when
the agent releases a body and contains no FormID, handle, pointer, or generated
source location.

`BodyId(string Value)` is an opaque portable identity for a materialization.
Policy compares it but does not parse it.

`HostActorId(uint FormId, ulong Generation)` is a Skyrim backend identity. The
Marionette adapter maps a `BodyId` candidate to this record. The FormID never
becomes the agent identity, and no native pointer crosses the process boundary.
The candidate generation is checked before bind; the active host generation
returned by `begin_host_session` replaces it for move and release checks.

## Contract and lifecycle

The portable vocabulary is:

- `BodyBindingKind`: `ExclusiveControl`, plus explicitly unsupported M1 shapes
  for presentation-only and observation-only use;
- `BodyBindingState`: `Unbound`, `Binding`, `Bound`, `Releasing`, `Released`,
  `Lost`, `Failed`, and `RestoreRequired`;
- `BodyBinding(AgentId, BodyId, kind, state, generation)`;
- `BodyObservation`, with liveness, position, semantic capabilities, binding
  state, owner, generation, and observation sequence;
- `BindBodyArguments`, `QueryBodyBindingArguments`,
  `MoveBodyTowardArguments`, and `ReleaseBodyArguments` on the existing
  `HostCommandRequest` bus;
- `BodyCommandResult` and `BodyBindingObservation` on the existing
  `HostActionResult`/`HostRuntimeObservation` completion stream.

`BodyBindingRegistry` enforces the M1 invariants:

1. one exclusive body per agent;
2. one exclusive agent per body;
3. unsupported binding kinds are rejected rather than implied;
4. missing or stale generations are rejected;
5. unloaded/lost bodies become explicit `Lost` results;
6. release is idempotent;
7. failed binding removes partial active ownership;
8. the backend retains disconnect restoration;
9. release/restoration state is returned as an observation;
10. agent identity remains stable after release;
11. body lifetime remains backend-owned;
12. portable policy never inspects FormID.

## Skyrim lowering

`BodyBindingHostBackend` is the ownership seam. It wraps the existing
`IHostPresenterBackend`; it does not create a second command bus.

```text
BindBody
  -> validate candidate BodyId mapping and generation
  -> reserve exclusive ownership
  -> BeginHostSession
  -> begin_host_session wire request
  -> capture and mutate on Skyrim's main thread
  -> observe active HostActorId and BodyObservation
  -> Bound

MoveBodyToward
  -> validate AgentId owner, BodyId, Bound state, and generation
  -> lower to legacy typed MoveTowardArguments
  -> move_toward wire request
  -> SKSE task queue
  -> main-thread displacement
  -> observe terminal action and new position

ReleaseBody
  -> transition to Releasing
  -> EndHostSession
  -> restore_host_session wire request
  -> main-thread restoration
  -> observe Released
```

Movement is rejected before lowering when the caller is not the exclusive
owner, the binding is absent or released, the generation is stale, or the body
is unloaded. Native `actor_unloaded` and `target_invalid` results transition the
binding to `Lost`. A lost binding rejects body commands but retains its
exclusive reservation until release can restore the host session. A timeout or
failed release is not treated as proof that no
mutation occurred; it produces `RestoreRequired`, and the scenario retains its
emergency-restore fallback.

All engine mutations remain on Skyrim's main thread:

```text
Aurelian/Dominatus managed process
  -> authenticated named pipe
  -> Marionette worker thread
  -> bounded mutation dispatcher
  -> SKSE task queue
  -> Skyrim main thread
```

## Dominatus agent

`SkyrimBodyAgentFlow` is source-generated from ten explicit durable state IDs:

- root and `RequestBinding` establish ownership;
- `BoundIdle` performs a real utility choice among body loss, goal completion,
  movement, and inability to act;
- `ApproachParent` calls `ApproachTarget` and explicitly matches success,
  failure, and neutral return;
- release-success and release-after-failure states restore the session;
- `Completed`, `Failed`, and `RestoreRequired` are terminal domain states.

Bind, move, and release use stable pending-only operation sites. Their typed
payloads remain authored data, so child states use `Ai.Succeed` and `Ai.Fail`
meaningfully instead of globally converting arbitrary exceptions into domain
failure. Generated-flow inspection proves there are no generator-created
hidden states.

## Camera and restoration

M1 keeps camera effects session-local. Binding retargets the existing
third-person camera as part of `begin_host_session`; release restores the player
and camera through `restore_host_session`. This is observable in native results
but is not yet a camera agent. Camera-agent extraction is a later strangler
boundary.

The current session restores the fields already captured by the proven host
session: player position/AI-driven state, camera target/mode, and host actor
AI/position/death-related session state. M1 does not claim broader inventory,
perk, faction, quest, alias, crime, dialogue, or first-person-camera
restoration.

## Not possession

Binding does not call `SetPlayer` and does not transfer player reference
identity, input authority, inventory, perks, skills, factions, crime, quests,
aliases, dialogue identity, or first-person camera. It is bounded semantic body
control owned by an Aurelian agent.

Save/load durable actor identity and active-session replay are unsupported.
FormID plus process-local generation prevents stale runtime mutation but does
not survive load-order changes or claim durable actor identity.

Connection and restore health remain owned by the existing transport/session
boundary for M1. A future engine agent should own world lifecycle, cell/load
events, save/load coordination, backend health, and global restoration.

## Ownership after M1

| Capability | Skyrim | Marionette | Aurelian | Dominatus |
| --- | --- | --- | --- | --- |
| actor materialization | owns | adapts | observes | — |
| agent identity | — | maps | owns | stores/uses |
| body binding | materializes | lowers | owns contract | executes policy |
| movement decision | — | — | supplies observations | owns |
| movement mutation | executes | lowers | commands | awaits |
| restoration | executes | lowers | owns lifecycle result | branches |
| camera semantics | executes | session-local | observes | — |
