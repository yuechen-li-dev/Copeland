# Beacon Run — geometric arena FPS

Survive three waves of stalkers, collect three gold beacons, then enter the green gate.
The arena uses placeholder boxes and diamonds to exercise engine capabilities.

From the Copeland root, run `beacon3d.cmd` (or double-click it). Requires the .NET 10 SDK,
a Vulkan-capable GPU/driver, and the repository's existing DXC shader toolchain.

| Control | Action |
| --- | --- |
| W / S | Forward / backward |
| A / D | Strafe |
| Left / Right arrows | Turn |
| Up / Down arrows | Look up / down |
| Mouse movement | Aim (captured during play) |
| Left mouse button | Fire bolts while held |
| Space | Jump on press |
| R | Reload the 12-bolt magazine |
| Escape | Pause / resume; back in menus |
| Up / Down in menus | Select an enabled entry |
| Enter / Space in menus | Confirm |
| Mouse click in menus | Activate a button |

The title bar displays the objective, collected count, and win message. The window is
fixed at 960×600. InputMan owns action maps, held axes, action edges, and focus reset;
the existing `SilkInputBridge` translates native events into `AurelianInputAdapter`.

The game starts at a title menu and provides pause, controls, and completion menus.
These use the [Aurelian game menu template](../../../docs/Aurelian/games/game-menu-template.md).
Menus freeze simulation and render native analytic shapes/MSDF text over the 3D scene.
Escape releases the cursor. Losing focus pauses the game and clears pending input;
returning to the window leaves the menu open. The pause menu provides Restart.

## Reproduce the proof

```powershell
.\beacon3d.cmd --proof
# Optional: watch the scripted playthrough in its window.
.\beacon3d.cmd --proof --visible
```

The proof injects physical keyboard states through InputMan. It walks the real game
to completion, renders each step, and presents it through a Vulkan swapchain. It
does not teleport the player or mutate collection state. Captures, hashes, shader
source output, device facts, and the collection trace are written to
`artifacts/aurelian-beacon3d/`. Use `--output <directory>` for another destination.

Before the playthrough, the proof checks title/controls/pause navigation, frozen game
state, matching mouse presses/releases, focus cancellation, InputMan context isolation,
and repeated-frame pixel/font-upload stability. Menu captures and results are recorded
alongside the depth and completion witnesses.

The FPS proof then injects mouse motion and held firing through InputMan, clears all
three waves, and completes the beacon route. It checks accumulated mouse events and
one-tick delta consumption, executes native cursor capture transitions, and records
living-creature brain ticks, kills, shots, health, and combat captures. It changes no gameplay
state directly. Use `--output artifacts/aurelian-beacon-fps-m0` to reproduce the FPS
qualification independently of the earlier 3D/menu witnesses.

The independent depth witness renders intersecting screen projections of two solid
cubes. It requires identical pixels in opposite submission orders with depth enabled,
and different pixels with depth disabled. The near green cube must cover the red cube
at the center while both objects remain visible elsewhere. A changed camera uniform
must change the pixels without changing the world vertices.

## Implementation boundary

`BeaconGame` owns movement, collision, jumping, collection, and the win condition.
It also owns projectile collision, reloads, damage, and wave progression. Bolts use
swept segment/sphere tests and stop at pillar bodies/caps, the floor, or arena bounds.
Stalkers chase using local collision-checked steering and attack with a cooldown.
The native HUD displays health, ammo, wave progress, kills, and objectives.

The [engine-owned agent authoring path](../../../docs/Aurelian/games/agent-authoring.md)
creates the player character, beacon objects, and creature agents. Positions, health,
and collection facts live in their typed game state. Persistent Dominatus creature
brains receive observations and emit intents; the game resolver changes state.
`BeaconScene` builds world-space triangle geometry from those facts. The camera is a
right-handed perspective transform with near/far 0.1/80 and depth [0,1]. The GPU applies
the transform and directional lighting in `Solid3D.v.ts`, compiled by the existing
Copeland GPU binder → VD-MIR → Aurelian HLSL/DXC → SPIR-V path.

`VulkanSolid3DRenderer` belongs to Aurelian.Graphics. It retains its vertex/camera
buffers, pipeline, descriptors, framebuffer, and D32 depth texture. It reuses the
existing allocator, render-pass factory, pipeline factory, draw encoder, submission,
native color target, and swapchain presenter. Each frame clears depth to 1 and uses
LESS depth test/write. The existing 2D paths keep their color-only descriptors.

This qualifies opaque untextured triangle rendering, GPU perspective, occlusion,
camera movement, native presentation, InputMan mouse aim, authored agents, combat,
and a playable loop. Mesh import, textured
materials, shadows, skeletal animation, resize, and general 3D physics are outside
this sample. Automated keyboard/mouse state injection proves the InputMan/game path;
human mouse feel and difficulty remain manual checks. Validation-layer availability is recorded without
claiming a counted debug-messenger error total.

## Playtesting and inspection

Beacon consumes the shared `Aurelian.Playtesting` runner extracted from TinyFarm.
Its named creature runtime exposes live Dominatus paths, blackboards, native utility
reports, child returns, and bounded change/trace history. JSON scripts and stdin
drive the same InputMan bindings and application/menu code in headless or Vulkan mode.

```powershell
dotnet run --project samples/Integrations/Aurelian.Beacon3D -c Release -- --headless --playtest-script samples/Integrations/Aurelian.Beacon3D/Playtests/inspection.json --output artifacts/beacon-inspection
```

Omit `--headless` for native rendering, add `--visible` to watch, or use
`--playtest-stdio` for commands such as `mouse-delta 80 -20`, `checkpoint encounter`,
`inspect brains`, and `rewind encounter`. See [the shared kit guide](../../../docs/Aurelian/aurelian-playtesting-inspection.md)
for API reuse, artifacts, exact replay and native policy checkpoint boundaries.

## Validation

```powershell
dotnet build Aurelian.slnx -c Release -m:1
dotnet test Aurelian.slnx -c Release -m:1
dotnet test tests/Copeland/Copeland.TS.Tests -c Release -m:1
```

The Aurelian suite includes an existing WebGPU translation test requiring Naga CLI.
If Naga is outside PATH, set `AURELIAN_NAGA` to its executable. The local qualification
tool can be installed with:

```powershell
cargo install naga-cli --version 30.0.1 --locked --root artifacts/aurelian-beacon3d/toolchain
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
```
