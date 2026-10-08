# Beacon Run — native Vulkan 3D proof

A small first-person game: collect the three gold beacons, then enter the gate.

From the Copeland root, run `beacon3d.cmd` (or double-click it). Requires the .NET 10 SDK,
a Vulkan-capable GPU/driver, and the repository's existing DXC shader toolchain.

| Control | Action |
| --- | --- |
| W / S | Forward / backward |
| A / D | Strafe |
| Left / Right arrows | Turn |
| Up / Down arrows | Look up / down |
| Space | Jump on press |
| R | Restart on press |
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

The independent depth witness renders intersecting screen projections of two solid
cubes. It requires identical pixels in opposite submission orders with depth enabled,
and different pixels with depth disabled. The near green cube must cover the red cube
at the center while both objects remain visible elsewhere. A changed camera uniform
must change the pixels without changing the world vertices.

## Implementation boundary

`BeaconGame` owns movement, collision, jumping, collection, and the win condition.
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
camera movement, native presentation, and a playable loop. Mesh import, textured
materials, shadows, skeletal animation, resize, and general 3D physics are outside
this sample. Automated key injection proves the InputMan/game path; human keyboard
play is a separate manual check. Validation-layer availability is recorded without
claiming a counted debug-messenger error total.

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
