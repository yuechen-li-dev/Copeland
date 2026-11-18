# Riverward

Launch with `riverward.cmd` from the repository root. Choose **New game**. The town starts paused so you can inspect the two existing home blocks and their street connection across the bridge.

| Control | Action |
| --- | --- |
| Click toolbar or 0–6 | Select a tool |
| Hover the map | Inspect a block or preview a placement. Green outlines show gains and red shows losses or blocked placement |
| Left click | Build or inspect. Streets follow hex edges |
| Right click | Return to Inspect |
| WASD or arrows, Q/E, wheel | Pan, orbit and zoom |
| Space, F | Pause city time or switch between 1× and 3×. A normal-speed month lasts 12 seconds |
| Escape | Open Aurelian’s shell menu to save, load or change settings |

Employ at least 24 households and run a monthly surplus for three consecutive months. Each connected workshop supplies 14 jobs. A home block holds eight households, or sixteen with at least +2 appeal. An adjacent park adds +2 and an adjacent workshop subtracts 2. The larger buildings appear as households move in. A connected market earns income from households on immediately adjacent, connected blocks. Streets connect at corners, and the network starts at Town Hall.

The placement preview calculates immediate effects using today’s households. Migration and the budget settle at the end of each month. Save/load restores the town and month progress, and reopens the town paused.

## Playtest

```powershell
dotnet test Games/Riverward/Riverward.Tests -c Release
.\riverward.cmd --playtest-script Games/Riverward/Playtests/river-town-native.json --output artifacts/local/riverward/native --save-root artifacts/local/riverward/native/saves
.\riverward.cmd --headless --playtest-script Games/Riverward/Playtests/river-town-growth.json --output artifacts/local/riverward/growth --save-root artifacts/local/riverward/growth/saves
```

The native run previews a park that raises capacity from 16 to 32, then places streets after rotating, zooming, and resizing. It verifies that a second workshop is connected after saving and loading. The growth run completes the scenario with 28 employed households and a $113 monthly surplus. The rules tests also complete the scenario by spreading three home blocks across more land without a park. These are scripted runs. Player onboarding and balance still need a human playtest.

Riverward uses the starter’s `IGame` / `GameApp` shell. Reusable hex coordinates and shared edge identities live in `Aurelian.Strategy`, with camera projection and picking in `Aurelian.Runtime`. Riverward owns its map, street access rules, economy, and save snapshot.
