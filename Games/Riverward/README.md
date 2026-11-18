# Riverward

Launch with `riverward.cmd` from the repository root. Choose **New game**. The town starts paused so you can inspect the two existing home blocks and their street connection across the bridge.

| Control | Action |
| --- | --- |
| Click toolbar or 0–9 | Select a tool. Use 7 for docks, 8 for fish markets, and 9 for boardwalks |
| Hover the map | Inspect a block or preview a placement. Green outlines show gains and red shows losses or blocked placement |
| Left click | Build or inspect. Streets follow hex edges |
| Right click | Return to Inspect |
| WASD or arrows, Q/E, wheel | Pan, orbit and zoom |
| Space, F | Pause city time or switch between 1× and 3×. A normal-speed month lasts 12 seconds |
| Escape | Open Aurelian’s shell menu to save, load or change settings |

Employ at least 24 households, keep average happiness at 72% or higher, and run a surplus of at least $30 per month for three consecutive months. Each connected workshop supplies 14 jobs. A home block holds eight households, or sixteen with at least +2 appeal. Adjacent parks and boardwalks add +2 each, and workshops subtract 2. The larger buildings appear as households move in. Happiness reflects employment, access, local amenities, and workshop noise.

## Waterfront

- **Docks** need a street along one edge. Two paved docks open the ferry line when at least one has a land route to Town Hall. The ferry links local street networks even across long stretches of river, opening access to jobs. Homes adjacent to a working dock gain 5 happiness points.
- **Fish markets** sell to adjacent households and ferry visitors. A fish market next to a working dock can trade through the ferry and earns $7 per sale. A market with street access can sell for $5 per sale before the ferry opens.
- **Boardwalks** give neighboring homes 18 happiness points and can earn $2 in concessions per ferry visitor. Several attractions at the same destination network share the riders. The simulation counts up to 12 visitor trips from each dock per month and charges a $1 fare per visitor. Job access uses the route connection.

Hover a proposed second dock to see its route and the projected change in jobs, happiness, and monthly cash flow. The placement preview uses today’s households. Migration and the budget settle at the end of each month. Save/load restores the town and month progress, and reopens the town paused. Older saves keep their town and start a fresh victory streak under the new goals.

## Playtest

```powershell
dotnet test Games/Riverward/Riverward.Tests -c Release
.\riverward.cmd --playtest-script Games/Riverward/Playtests/river-town-native.json --output artifacts/local/riverward/native --save-root artifacts/local/riverward/native/saves
.\riverward.cmd --playtest-script Games/Riverward/Playtests/river-town-waterfront.json --output artifacts/local/riverward/waterfront --save-root artifacts/local/riverward/waterfront/saves
.\riverward.cmd --headless --playtest-script Games/Riverward/Playtests/river-town-growth.json --output artifacts/local/riverward/growth --save-root artifacts/local/riverward/growth/saves
```

The waterfront run opens an isolated upstream neighborhood by ferry and turns its fish market from an $18 monthly cost into a $52 surplus. The completed town has 24 employed households, 85% happiness, and a $111 monthly surplus. The original park and road layouts also meet the goals. These are scripted runs. Player onboarding and balance still need a human playtest.

Riverward uses the starter’s `IGame` / `GameApp` shell. Reusable hex coordinates and shared edge identities live in `Aurelian.Strategy`, with camera projection and picking in `Aurelian.Runtime`. Riverward owns its map, street access rules, economy, and save snapshot.
