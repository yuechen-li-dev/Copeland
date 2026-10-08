# Aurelian agent authoring

`Aurelian.World.Agents` generalizes TinyFarm's template → named spawn → validated
state creation path. Characters, objects, and creatures are semantic agent kinds,
with human, autonomous, or passive control. A `GameAgent<TState>` holds one authored
identity, its template, and one typed authoritative state. Behavior is supplied by
the game through InputMan or a live Dominatus brain; the renderer projects state.

```csharp
var runner = AgentTemplate.Character("arena.runner", AgentControl.Human);
var stalker = AgentTemplate.Creature("arena.stalker");
var beacon = AgentTemplate.Object("arena.beacon");

var declarations = new AgentSpawn<Vector3>[]
{
    new("player", "Runner", new Vector3(0, 0, 9), runner),
    new("stalker-1", "Stalker", new Vector3(-8, 0, -8), stalker),
    new("beacon-1", "Beacon", new Vector3(-7, 0, 3), beacon),
};

var agents = AgentAuthoring.CreateBatch(declarations, existingIds,
    ValidateArenaPlacement, spawn => CreateArenaState(spawn));
```

Creation orders declarations by stable authored ID, rejects duplicates and collisions
with existing IDs, validates kind/control/name/placement, then constructs the batch.
It publishes no partial batch. Placement validators and state factories must be pure
with respect to the live world. Immutable typed states may be replaced by the domain
resolver after accepted intents; domain state is never placed in a brain blackboard.

Authored IDs are local semantic names, as in TinyFarm. These do not replace the GUID
`AgentId` used by Aurelian's external host/body binding contracts or world `UnitId`.
No ECS, component registry, parallel save format, or generic mutable property bag is
introduced by this authoring capability.

TinyFarm now calls these engine-owned ordering, identity, template, health, and typed
creation rules while retaining its existing actor/placement/inventory owners and JSON
schema. Farm-specific RPG, containers, schedules, equipment, and save compatibility
stay in TinyFarm. Its existing public character/object templates remain compatible.
Creature authoring is available to new games; this change adds no farm creature sprites
or farm-specific behavior.

Beacon Run is the second real consumer. Its player is a human character agent; each
collectible is an object agent; each stalker is an autonomous creature agent. Every
living creature is observed before a tick of the existing Aurelian Dominatus world
runner. Persistent brains choose chase/attack intents. The arena resolver validates
movement and attack distance, applies cooldowns and damage, and updates typed state.
Projectiles and wave rules are game-owned. Local obstacle steering is bounded arena
behavior, not a general 3D navigation service.

See `BeaconAgents.cs`, `BeaconCreatureBrain.cs`, and `BeaconCombat.cs` for the complete
path. Agent-authoring tests cover all three kinds, deterministic creation, duplicate
and existing identities, and validation before state construction. FPS tests cover
live brain transitions, InputMan mouse deltas, combat, death/retry, and the three-wave
case. TinyFarm's existing agent/save suite checks the extraction's compatibility.
