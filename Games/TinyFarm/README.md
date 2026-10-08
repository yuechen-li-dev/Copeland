# TinyFarm: The Sleeping Spring

The first playable MVP slice: a garden, a home stove and bed, a woodland crossing, and one attacking slime in Old Burrow. Harvest a starter turnip, cook healing broth, plant and water, explore, fight or retreat, and come home to your own harvest after sleeping.

This is a review build of Gate A. The campaign, additional rooms, village shops and progression are unfinished. Roads outside the opening route are closed. Fresh-player comprehension and combat feel still need human review.

## Play

On Windows with .NET 10 and a Vulkan-capable graphics driver, double-click **Play-TinyFarm.cmd** in the repository root, or run:

```powershell
dotnet run --project Games/TinyFarm/TinyFarm.Native -c Release
```

Press **Enter** to begin. **N** continues your save. Normal launch opens the opening slice at 1920×1080; no proof flag is needed.

## Controls

| Control | Action |
| --- | --- |
| WASD | Move, including normalized diagonal movement; face a nearby target |
| E | Talk, harvest, water, cook, sleep at your bed, or enter a doorway |
| J | Swing the sword; no target selection required |
| Space | Dodge in your movement direction, or forward while standing |
| 1 then K | Plant a turnip in an empty plot |
| 3 then K | Use the axe beside a tree |
| R | Eat broth; restores up to four health, consumes nothing at full health |
| I | Pockets and opening objectives; I, Escape or Enter closes them |
| Escape | Pause / return |
| Enter | Begin, resume, or advance dialogue |
| F / N | Save / load |
| F9 / F10 | Toggle HUD / inspector independently |
| F11 | Capture a clean gameplay PNG |
| Q | Quit from the title, pause or pockets screen |

Stand nearby and face an object. The local prompt shows the available action. Follow the east path across the wooden bridge, then north into Old Burrow. Amber dots show the slime's committed jump direction; dodge sideways and strike during recovery. Two connected swings defeat it. Defeat returns you to the entrance with half health and preserves inventory.

Watered turnips grow after one night's sleep. The starter harvest does not count as a crop you grew. Sleep restores health and saves the new morning. At 22:00 outdoors, the game brings you home for rest without a fee. Dungeon time, menus, dialogue, pause and focus loss freeze the calendar.

## Save and presentation

The opening save is separate from the earlier supper prototype: `%LOCALAPPDATA%\TinyFarm\saves\sleeping-spring-gate-a.dlv`. Loading replaces changes since that save. Manual save happens in the background; sleeping serializes its checkpoint after any earlier pending save. Failures are shown in the status message.

```powershell
Play-TinyFarm.cmd --width 2560 --height 1440
Play-TinyFarm.cmd --world-only --width 2560 --height 1440
```

The world has a uniform fixed projection and owns its camera viewport. The HUD overlays it. Resize uses framebuffer dimensions; painterly cutouts, floors and brushes use linear sampling. F9 hides normal chrome but retains the local prompt. F11 temporarily hides HUD and inspector, writes `artifacts/tinyfarm-captures/tinyfarm-<UTC timestamp>.png`, then restores their prior settings. It does not pause play or change semantic state. Modal screens remain usable.

Keyboard play is qualified. Controller bindings are declared, but physical gamepad play is not qualified. Existing tone-based audio remains provisional; the recorded proof video is silent.

## Developer verification

```powershell
dotnet build TinyFarm.slnx -c Release -m:1
dotnet test TinyFarm.slnx -c Release -m:1
dotnet run --project Games/TinyFarm/TinyFarm.Native -c Release -- --slice-proof
dotnet run --project Games/TinyFarm/TinyFarm.Native -c Release -- --slice-proof --width 2560 --height 1440
dotnet run --project Games/TinyFarm/TinyFarm.Native -c Release -- --window-smoke
```

`--slice-proof` needs `ffmpeg` on PATH to record a continuous native walkthrough. It injects keyboard events through the normal Silk/InputMan bridge, uses resolver-owned movement and interactions, demonstrates damage/dodge/healing, completes the home loop, and checks save/load equality. It never loads fixtures or writes player coordinates during play. The optimized 34-second script is not a human ten-minute playtest.

Evidence is in `artifacts/tinyfarm-gate-a/`; the report is [Gate A report](../../docs/milestones/tinyfarm-mvp-gate-a-report.md).

The earlier supper scenario remains accessible with `--legacy-supper`. Existing `--proof`, `--m24-proof` and `--m25-proof` retain their historical content and presentation paths. Those modes use their original controls and separate `supper` save slot.
