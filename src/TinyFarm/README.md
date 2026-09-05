# TinyFarm: A Little Mint of Kindness

A seed for tomorrow. A meal for today. Help Mara make a small corner of the world feel like home.

Plant a turnip, gather mushrooms beside the river, cook them in Hearth House, and shoo the slime out of Old Burrow. Pick up the wild mint by the farm plots and bring it to Mara when supper is ready. Your journal keeps track of every job. Expect a small, forgiving afternoon: roughly 5-10 minutes on a first visit, with no deadline and no grinding.

## Play

On Windows with .NET 10 and a Vulkan-capable graphics driver, double-click **Play-TinyFarm.cmd** in the repository root. Or run:

```powershell
dotnet run --project src/TinyFarm/TinyFarm.Native
```

Press **Enter** at the title. **N** continues your saved afternoon. This is the native Aurelian client.

## Controls

| Control | Action |
| --- | --- |
| WASD | Move and face an object |
| E | Talk, pick up, gather, cook, tend crops, or enter a doorway |
| 1 / 3 / 4 | Select seeds / axe / sword |
| Space | Use the selected seed or tool |
| I | Open your pockets; I, Escape, or Enter closes them |
| Escape | Pause; Escape or Enter resumes |
| Space / Enter | Advance Mara's dialogue |
| Up / Down, then Enter | Choose a reply |
| F | Save the current afternoon, including an open conversation |
| N | Load your save |
| Q | Quit from the title, pause, pockets, or completion panel |

Stand close and **face** an object. A contextual prompt tells you what the controls will do. Planting counts immediately; you do not need to wait for the turnip to grow. The slime takes one sword hit and cannot hurt you.

Follow the signed farm gate to the trail. Town is in the middle, the river to the right, and Old Burrow on the upper right. Hearth House is the doorway by the farmhouse. Mara is in town before noon and by the river afterward; the journal always shows her current scene. Elias has his own nearby meeting spot.

## Save and continue

There is one manual slot. **F** shows a success or failure message; **N** restores it. Saves live at `%LOCALAPPDATA%\TinyFarm\saves\supper.dlv`. Loading discards changes since the last save. A completed afternoon stays complete, and you can continue wandering afterward.

The game runs at a fixed 1280x720 size. Keyboard play is qualified. Logical gamepad mappings exist (left stick, South interact, West tool, D-pad slots, Start pause), but this Windows window does not yet collect physical gamepad events. Audio falls back to silence if no Windows output device is available.

## Developer verification

```powershell
dotnet run --project src/TinyFarm/TinyFarm.Native -- --proof
dotnet run --project src/TinyFarm/TinyFarm.Native -- --window-smoke
dotnet test TinyFarm.slnx -m:1
```

The proof follows real resolver movement and interactions, compiles the Visual TypeScript effect, captures native frames, restores and continues a saved session, and verifies semantic replay. See `docs/Aurelian/aurelian-full-game-slice-m9-report.md` for evidence and release limitations.
