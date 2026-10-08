# Beacon Run FPS M0 qualification

Outcome: **Success** for the basic FPS and shared agent creation capability.

## Engine capability

`Aurelian.World.Agents.AgentAuthoring` now owns template/spawn validation,
deterministic identity ordering, and typed state creation for character, object, and
creature agents. TinyFarm reuses this path while retaining its existing domain
models/save schema. Beacon Run creates one human character, three collectible object
agents, and nine creature agents across three waves. Each living stalker has a
persistent Dominatus brain ticked through Aurelian's existing runner. Observations
and intents cross the policy boundary; the game resolver owns movement and damage.
Defeated stalkers release their brains; their geometric remains can stay visible.

InputMan owns mouse deltas, aim axes, held firing, reload actions, and focus reset.
The native adapter now accumulates multiple motion events before a tick and ignores
input while unfocused. Silk handles only native events/cursor capture. Pause and
focus loss release the cursor; focus loss also pauses the application.

## Native proof

```powershell
samples/Integrations/Aurelian.Beacon3D/bin/Release/net10.0/Aurelian.Beacon3D.exe --proof --visible --output artifacts/aurelian-beacon-fps-m0
```

The visible Vulkan run on NVIDIA GeForce RTX 3070 presented 3,681 frames, cleared
three waves, killed nine stalkers with 24 bolts, collected all three beacons, and won.
The runner retained 100 health in the automated aiming case. The playthrough injects
physical InputMan controls and never mutates camera, position, health, kills, or wave
facts. `proof.json` records 3,543 living-creature brain ticks and zero remaining active
creature brains, along with device facts, menu checks, depth witnesses, and hashes.
`native-run.log` records successful completion.

`fps-combat.png`, `fps-cleared.png`, menu captures, and `win.png` were rendered by
the real Vulkan path. Visual inspection caught and repaired the original oversized
muzzle flash. The final weapon follows camera orientation, and the HUD reports
crosshair, health, ammunition, wave/kills, and objectives through native shapes/MSDF.

## Regression evidence

- Release `Aurelian.slnx` build succeeded. Existing third-party OpenFont warnings may
  appear on rebuild; there are no build errors.
- Full Aurelian suite: 850 passed across 24 projects, zero failures/skips. The existing
  repository-local Naga CLI was supplied via `AURELIAN_NAGA`.
- Release `TinyFarm.slnx` build succeeded; its suite passed 476 tests with zero
  failures/skips, including existing agent authoring/save cases.
- New tests cover three agent kinds, atomic batch validation, duplicate/existing IDs,
  unclamped accumulated mouse deltas, focus clearing, one-tick consumption, menu
  isolation, duration-independent aim, live chase/attack brains, swept hits, actual
  projectile/pillar collision, reload/rate limits, death/retry, and the complete fight.

## Scope

This is a geometric placeholder arena FPS. The game owns weapons, waves, health,
projectiles, and local creature steering. It adds no ECS, component bags, generic
3D navigation/physics engine, mesh import, animation rigs, or new save format. It
qualifies automated mouse/input/game/native rendering and cursor mode transitions;
human mouse feel and difficulty still need manual play. The separate previously
documented Machina glyph-threshold test mismatch was not changed or requalified here.
