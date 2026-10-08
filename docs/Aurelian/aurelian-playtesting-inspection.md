# Aurelian playtesting and agent inspection

The kit assembles TinyFarm's existing playtest runner and Dominatus's existing AI
trace, blackboard journal, and policy checkpoint machinery. TinyFarm and Beacon Run
are real consumers. Application state still belongs to each game; policy execution
still belongs to Dominatus; input bindings and focus reset still belong to InputMan.

## Ownership and reuse

| Capability | Existing authority | Shared entry point |
| --- | --- | --- |
| Validated JSON scripts, stdin commands, physical input edges, fixed cadence, seeded fuzz, artifact paths | Extracted from `TinyFarm.Playtesting` | `Aurelian.Playtesting` |
| Domain commands, semantic assertions, observations, persistence settling, native capture | Each game adapter | `IPlaytestTarget<TObservation>` |
| Named live agents and sequential ticks | Existing `AiWorld`, `AiAgent`, `SequentialAurelianDominatusWorldRunner` | `AurelianAgentRuntime` |
| State entry, exit, transition, yield and child return callbacks | `HfsmInstance.Trace` / `IAiTraceSink` | `DominatusInspector` |
| Utility scores, chosen option and switching reason | Native `DecisionReport` callback | `InspectedDecision` |
| Active path, typed scalar values, expiry metadata, blackboard changes | `GetActivePath`, `Bb.EnumerateSnapshotEntries`, `BbChangeTracker` | `AgentInspection` |
| Cold policy checkpoint | `DominatusCheckpointBuilder`, `BbJsonCodec`, `EventCursorCodec` | `CapturePolicyCheckpoint` / `RestorePolicyCheckpoint` |
| Whole-game rewind | Fresh game plus recorded owned input | `IReplayablePlaytestTarget`, `checkpoint` / `rewind` |

The Dominatus audit also found the existing `ReplayDriver` for nondeterministic
actuation completions, the MonoGame agent overlay, and Aurelian's advanced runtime
session access. These remain their owners' facilities. There is no new scheduler,
utility evaluator, persistence codec, or game-state patcher here.

## Add a game

Reference `src/Integrations/Aurelian.Playtesting/Aurelian.Playtesting.csproj` and
implement `IPlaytestTarget<YourObservation>`. Route physical events into the game's
InputMan adapter, and `Frame(elapsed)` through its normal application cadence. Keep
game commands and semantic assertions in the adapter. Define source-generated JSON
metadata for the observation, including any `AgentInspection` projections.

```csharp
using var runner = new PlaytestRunner<YourObservation>(
    target,
    outputDirectory,
    YourJsonContext.Default.YourObservation);

runner.Run(PlaytestScripts.Read(scriptPath));
// Or, for an owned process with stdin:
PlaytestConsole.Shell(runner);
```

For new agent collections, import `Aurelian.Runtime.Dominatus.Inspection` and
`Aurelian.Runtime.Inspection`, then register existing authored Dominatus brains by semantic
identity. The generated OptFlow authoring used by Beacon and TinyFarm remains the
state/utility authoring API; the wrapper adds application names and inspection.

```csharp
var agents = new AurelianAgentRuntime(traceCapacity: 4096);
var creature = agents.Add("stalker-1", CreatureFlow.Definition.CreateBrain());
creature.Bb.Set(distanceKey, observedDistance);
agents.Tick(elapsed);
AgentInspection inspection = agents.Inspector.Observe();
```

For an existing application runtime, attach `DominatusInspector` to its existing
agents instead of moving scheduling. TinyFarm does exactly this for its schedule
and passive brains. `TinyFarmSession.EnableAgentInspection()` enables callbacks;
`InspectAgents()` reads only brains that already exist. Reading never evaluates a
decision or creates an agent. A pre-existing trace sink still receives callbacks,
and detaching restores it. Observe on the owning runtime's stable tick boundary.

Trace and projected change buffers have a configured capacity and dropped-entry
counters. The named runtime drains its own kernel journals after each tick.
Inspectors attached to externally owned agents never clear their journals. Unknown
blackboard objects are shown as opaque types, without invoking arbitrary domain
`ToString` implementations or computed getters.

Direct kernel APIs live in the explicit advanced namespace
`Aurelian.Runtime.Dominatus.Inspection`. Their six kernel-exposing symbols are
individually allowlisted by the existing compiled public-surface boundary test.
The observation records and JSON metadata live in `Aurelian.Runtime.Inspection` and
contain only Aurelian-owned data. Ordinary session, compositor, Core and Machina
surfaces retain their existing boundary.

## Commands and artifacts

JSON schema version 1 preserves TinyFarm's original keyboard, pointer, click,
click-action, text, scroll, command, focus, resize, wait, and capture steps. Additional
steps are `mouse-delta`, `mouse-press`, `mouse-release`, `mouse-hold`, `checkpoint`,
and `rewind`. A target explicitly rejects capabilities it does not support.

The stdin equivalents include:

```text
click-action new-game
wait-seconds 2
press w
checkpoint encounter
mouse-delta 80 -20
mouse-hold primary 0.3
inspect brains
rewind encounter
release w
exit
```

Durations advance at 60 Hz with integer `TimeSpan` ticks, rounding each wait up to
a frame. Zero-time frames dispatch input edges and menus. Beacon retains pending
mouse deltas, jump, reload, and fire edges until a positive simulation tick; it does
not advance physics on an event-only frame. Failures release keys and mouse buttons.
Focus loss clears InputMan state and cancels pending pointer/gameplay actions.

Each run writes:

- `script.json`: validated original request for script runs.
- `trace.jsonl`: one step, domain observation, and error per line.
- `final.json`: final domain observation.
- `inputs.json`: the current branch's successful input/command prefix, replayable as
  a fresh script. Bookmarks, rewind operations, and pixel captures are excluded.
- Requested captures: restricted to the owned output directory.

Use `PlaytestScripts.Fuzz(seed, count, profile)` with game-owned physical keys and
initial steps. `TinyFarmPlaytestProfiles.Fuzz` retains TinyFarm's original xorshift
sequence and CLI behavior. TinyFarm observations retain their existing world/field,
menu, inventory and agent fields, and add `brains`.

## Run Beacon

```powershell
dotnet build samples/Integrations/Aurelian.Beacon3D -c Release -m:1
$beacon = 'samples/Integrations/Aurelian.Beacon3D/bin/Release/net10.0/Aurelian.Beacon3D.dll'
$script = 'samples/Integrations/Aurelian.Beacon3D/Playtests/inspection.json'
dotnet $beacon --headless --playtest-script $script --output artifacts/beacon-inspection-headless
dotnet $beacon --playtest-script $script --output artifacts/beacon-inspection-native
dotnet $beacon --headless --playtest-stdio --output artifacts/beacon-inspection-shell
```

Omit `--headless` to render through the real Vulkan world/HUD/menu presenters.
Native script runs are hidden by default; add `--visible` to watch. `capture NAME.png`
requires the native target. Native Beacon retains its fixed viewport; headless menu
resizing is supported. Automated modes own their input rather than sending desktop
keys or mouse events.

Beacon observations include a game-owned simulation hash and a policy hash of the
active paths, blackboards, retained native trace and changes. These support meaningful
backend/replay comparisons. The simulation hash includes private combat timers,
vertical velocity, agents, projectiles and other gameplay fields; it excludes pixels.
The policy hash is a projection, not a serialized coroutine instruction pointer.

## Checkpoint boundaries

`checkpoint NAME` stores a recorded input prefix. `rewind NAME` resets the entire
target to the same initial configuration and replays that prefix through normal
input and application code. This reproduces deterministic game state and policy
execution, including held input. It costs time proportional to the prefix. A target
must explicitly opt in and own deterministic reset, RNG seeds and external-effect
handling. Beacon opts in; TinyFarm continues using its existing save/load and semantic
replay paths, and explicitly rejects these reset/replay bookmarks.

The reset/replay target must also make its domain commands replay-safe. The shared
runner records successful commands as well as physical events. Beacon's commands
only inspect, mark, and assert; persisted writes or external requests need their
application's existing deterministic replay/effect handling before opting in.

`CapturePolicyCheckpoint` is a separate, native **policy-only** operation. The codec
supports bool, int, long, finite float/double, string and Guid. Aurelian rejects any
other value with the named agent and key, because the existing codec would skip it.
Captures must occur after pending child returns have been consumed.

Cold restore requires a freshly authored runtime with identical semantic names,
kernel IDs and roots. It restores native blackboards, clock and active paths, then
re-enters coroutine nodes. It does not restore an exact enumerator instruction,
utility commitment memory, game world, InputMan state or pending external effects.
In-flight actuations are explicitly rejected by this convenience API; applications
with an actuator host must use their existing completion replay path. Exact game
rewind is qualified through input replay, not by claiming a brain checkpoint captures
all application state.

## Qualification

The retained evidence lives in `artifacts/aurelian-playtesting-m0/qualification.json`:

- Release builds: `Aurelian.slnx` and `TinyFarm.slnx`.
- Full suites: 857 Aurelian and 477 TinyFarm tests, no failures or skips.
- Reflection-disabled JSON paths: four Beacon and fourteen TinyFarm playtest tests.
- Beacon: all 22 gameplay/inspection steps agree between headless and native Vulkan
  on screen, simulation hash, and policy hash. Native-only captures are omitted from
  this comparison. Rewind reproduces the held-input checkpoint and its later branch.
- The native run retains 484 kernel callbacks, including 236 utility reports, and
  120 blackboard changes. Title and arena PNGs were visually inspected.
- TinyFarm: all 24 steps of its existing input/menu/search/save/load/resize/focus
  script agree between headless and native on world/field hashes, screen, scene,
  position and minute.
- Existing native FPS/menu/depth proof still wins with nine kills, 24 shots, and
  health 100. Stdin emitted seven valid JSON observations through the shared shell.

This qualifies the reusable code and both existing game paths. It does not qualify
instant arbitrary-world snapshot rewind, coroutine instruction restoration, or
desktop focus/mouse feel.
