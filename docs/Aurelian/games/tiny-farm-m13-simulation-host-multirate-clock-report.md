# TinyFarm M13 — simulation host, multi-rate clock, and TSON control

## Outcome

Outcome A. The graphical application now advances the same authoritative `TinyFarmSession` used headlessly. `TinyFarmSimulationHost` accepts caller-supplied host deltas, derives renderer-independent locomotion opportunities and world minutes with integer accumulators, and exposes typed Pause, Play, FastForward, and AdvanceMinutes commands. The reuse classification is `TINYFARM_LOCAL_HOST_ONLY`: the missing seam is game policy, not a general clock framework.

## Mandatory infrastructure audit

The audit ran before host implementation across Aurelian, Dominatus, Joint Task Force documentation/solutions, TinyFarm, Machina.UI, Oblivion, and Marionette.

| Existing type/project | Semantics | Reusable directly? | Needed modification |
| --- | --- | --- | --- |
| `Aurelian.World.WorldClock` / `AurelianRuntime.Tick` | Ordinal `ulong` render/runtime tick | No | No elapsed-time, rate, pause, fixed-step, or batching law; unchanged |
| `AurelianFrameLoop` / runtime tick step | Renderer-neutral frame orchestration with one configured delta | No | It does not own a semantic simulation session or independent time domains; unchanged |
| `Dominatus.Core.AiClock` / `AiWorld.Tick(float)` | AI wait/TTL time in float seconds | No | Correct inside agent execution, but unsuitable as authoritative calendar/fixed-point host time; unchanged |
| `DominatusGameComponent` | MonoGame connector with `IsPaused` and float `TimeScale` | No | Directly couples render updates to `AiWorld.Tick`; using it would make the renderer time authority |
| Dominatus persistent TinyFarm schedule runtime | Event/observation-driven Required/Open selection | Yes | Winner-changing observation invalidation now rebuilds only that actor's derived flow runtime; same-winner warm calls remain cached |
| `FixedMovementStepper` | Exact 60 Hz integer-tick player locomotion accumulator | Yes | Retained for human held movement; the host uses the same 60 Hz law for semantic opportunity accounting |
| `TinyFarmSession`, `WaitIntent`, resolver | Sole authoritative mutations, minute Energy/crop/day progression, schedule decisions, active/inactive movement | Yes | Host batches due minutes through one-minute `WaitIntent` reductions; no second state |
| `TinyFarmFrame` / inspection JSON | Immutable renderer projection and diagnostic DTO | Partly | Frame stays presentation-only; a smaller versioned simulation transport DTO was added |
| Machina.UI / Oblivion / Marionette | UI dispatch, workbench execution timing, Skyrim pause event | No | No compatible simulation-clock ownership; unchanged |
| Joint Task Force topology | Ownership/migration documentation and solution boundaries | No | Confirms renderer/runtime separation but supplies no clock implementation |

## Ownership and time-domain law

```text
MonoGame GameTime / test-supplied TimeSpan
  -> TinyFarmSimulationHost (mode, rates, integer accumulators, catch-up policy)
  -> TinyFarmSession
  -> TinyFarmResolver + persistent Dominatus schedule adapter
  -> TinyFarmState
  -> TinyFarmFrame / TinyFarmSimulationSnapshot
  -> renderer, JSON, or canonical TSON
```

- Host time is an input only. The host accepts `TimeSpan.Ticks`; MonoGame supplies `ElapsedGameTime`, while headless tests supply deltas without sleeping.
- Render time is observational. `Draw` projects state and increments a diagnostic frame count; it never advances state.
- Locomotion time is a scaled 60 Hz integer numerator. The host owns the existing `FixedMovementStepper`; MonoGame supplies only cardinal input state, and spatial player steps explicitly skip NPC policy evaluation.
- World time uses an integer threshold of 50,000,000 host ticks per game minute. Play is 5 real seconds/minute; FastForward applies a 10× semantic multiplier.
- Agent decisions run once per authoritative world-minute reduction or another explicit semantic action, never per render frame or held-movement step. Goal changes, anchors, Energy, schedule boundaries, and active/inactive transitions remain observations of the existing flow.
- Narrative/LLM time remains event-driven and owns no clock.

Physics, combat, and projectiles are explicitly outside world-minute timing. A future implementation requires its own fixed semantic domain; M13 adds none.

## Modes, accumulation, and catch-up

`TinyFarmSimulationMode` is a typed enum. Paused accepts UI/render updates but contributes no accumulator time. Playing uses multiplier 1; FastForward uses multiplier 10. Switching modes preserves already-earned fractional semantic time. Pause contributes nothing, so resume cannot jump.

The host clamps each accepted update to five real seconds and discards excess explicitly; discarded time is reported and never retained as hidden backlog. This prevents an OS stall from producing hours of catch-up. World minutes are reduced one at a time, preserving event order, exact schedule boundaries, Energy changes, crops/day transitions, and active/inactive laws. Session replacement after load resets both fractional accumulators and retains the host mode safely.

`WaitIntent` remains explicit authoritative time advancement for actions and historical headless scripts. `AdvanceMinutesCommand` invokes that same reduction without wall-clock waiting and works while paused. In live mode it is an intentional immediate addition; it does not consume or duplicate the host's fractional accumulator.

## Graphical, CLI, and LLM controls

MonoGame owns one `TinyFarmSimulationHost`. `Update(GameTime)` routes elapsed time into it; `Draw` only projects. Keys `1`, `2`, and `3` issue Pause, Play, and FastForward commands. The HUD displays `PAUSED`, `PLAY`, or `FAST X10` beside the moving Day/Time value. Save/load replaces the hosted session atomically and resets catch-up state.

The headless control surface is:

```powershell
dotnet run --project src/TinyFarm/TinyFarm.Runner/TinyFarm.Runner.csproj -- --m13-control
play
host-ms 5000
fast-forward
advance 30
pause
snapshot-tson
```

The MonoGame `--llm-control` line protocol recognizes the same `pause`, `play`, `fast-forward`, and `advance <minutes>` commands semantically; no keyboard emulation is involved. Existing JSON inspection and `--m12-control` remain compatible.

## DTO

`TinyFarmSimulationSnapshot` is a projection, never save truth. Version `tiny-farm-simulation@1` contains mode, day, absolute minute, active scene, NPC actor IDs, scene IDs, fixed integer positions, Energy, Rest state, schedule regime, semantic goal, and state hash. Its C# representation retains typed IDs/enums. Canonical TSON declares nominal `ActorId`, `SceneId`, and `SceneAnchorId` records plus `SimulationMode` and `ScheduleRegime` enums. It contains no MonoGame type or wall-clock timestamp. Equal state/mode produces byte-identical TSON; parsing is verified through `TsonDocumentReader`.

## Deterministic qualification

The canonical 60-second Play interval produced 3,600 render observations, 3,600 locomotion opportunities, 12 world minutes, 36 NPC decision evaluations, and zero DotRecast queries for the inactive representative placement. The live fatigue/recovery trace separately exercised active navigation and records its DotRecast count in `timing.json`. 60 Hz and 144 Hz partitions have identical state hash, clock count, locomotion count, decision count, and path-query count. The irregular `16, 16, 50, 3, 91, 7, 33, 84 ms` pattern matches an even partition of the same total ticks.

The long-run test accepts 1,000 one-second updates and produces exactly 200 minutes and 60,000 locomotion opportunities. FastForward produces exactly ten minutes and 3,000 locomotion opportunities for five real seconds. A one-day and seven-day renderer-free hosted run completed deterministically.

Minute-granularity playback exposed and fixed one real M11 seam: at exactly Energy 5000, TinyFarm's expected-winner tie order used the personal bed anchor's logical order while Dominatus used its shared `FarmHome` option slot. The tie now uses the actual shared option order. When the expected winner later changes, only that actor's derived Dominatus flow runtime is rebuilt, ensuring a fresh bounded decision without replacing semantic state.

The canonical live sequence begins Paused, advances under Play and FastForward, freezes, resumes, saves/loads, and follows Mara from low Energy to her personal bed, Rest, recovery, winner change, departure, and a non-home Open goal. Required windows still bypass utility. Inactive NPCs retain coarse progression and do not invoke DotRecast each accelerated minute.

## Evidence, regression, and scope

`TinyFarmM13Tests` adds 15 tests covering pause/resume, exact rates, mode switching, 60/144 Hz equivalence, irregular partitions, long-run drift, catch-up, zero-allocation paused updates, host-owned held-movement stepping and decision separation, load reset, TSON determinism/parsing, explicit headless advancement, schedule/crop boundaries, and the shared-option tie. The complete TinyFarm suite and canonical M1/M2/M10/M11/M12 scenarios remain the regression authority; M12's canonical state hash remains exact.

Compact artifacts are under `artifacts/tiny-farm-m13/`: `proof.json`, `timing.json`, `rates.json`, `simulation-dto.tson`, and `manifest.json`. No screenshots, trace dumps, second scheduler, background thread, timer, combat system, new need, Machina.UI integration, or Oblivion integration were added.

## Recommended M14

The observed pressure is now spatial cadence, not another need: **TINY-FARM-M14 — active-NPC fixed-step locomotion realization**. Move current active NPC path-following from one spatial reduction per world minute into the already explicit locomotion domain while retaining event-triggered planning and minute-triggered utility. Do not add physics, combat, a generic scheduler, or renderer ownership.
