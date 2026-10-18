# TinyFarm: The Sleeping Spring

The opening slice: a garden, a home stove and bed, a shipping chest, a woodland crossing, and one attacking slime in
Old Burrow. Harvest a starter turnip, cook healing broth, plant and water, explore, fight or retreat, and come home to
your own harvest after sleeping.

This is a review build of Gate A. The campaign, additional rooms, village shops and progression are unfinished, and
roads outside the opening route are closed.

## Play

TinyFarm runs on the Aurelian [game shell](../../docs/Aurelian/game-shell.md), on Windows and Linux with .NET 10 and a
Vulkan driver. From the repository root:

```powershell
.\Play-TinyFarm.cmd
dotnet run --project Games/TinyFarm/TinyFarm -c Release
```

The shell supplies the window, resolution and fullscreen, title and pause menus, settings, key rebinding, audio
volume and save slots. Escape pauses, or closes TinyFarm's own screens and conversations first.

| Control | Action |
| --- | --- |
| WASD / arrows | Move; face a nearby target |
| E | Talk, harvest, water, cook, open the chest, or enter a doorway |
| J | Swing the sword |
| Space | Dodge in your movement direction |
| 1–4, then K | Select a tool, then use it (1 seeds, 3 axe) |
| R | Eat broth |
| B | Sleep (beside your bed) |
| I / C | Pockets and equipment / character |
| Arrows, Enter, Escape | Navigate, confirm and close TinyFarm's menus |
| Space, E, Escape | Advance, choose and leave conversations |
| F9 / F10 | Toggle HUD / inspector |

Play keys are rebindable in Settings → Controls. Watered turnips grow after one night's sleep. Sleeping, and being
brought home at 22:00, checkpoint the game into the shell's save slot.

## Structure

| Project | Role |
| --- | --- |
| `TinyFarm.Core` | Rules, state, intents, inventory, crafting, combat moves |
| `TinyFarm.Runtime` | Simulation host and cadence, content, dialogue, schedules, navigation, semantic persistence |
| `TinyFarm.InputMan` | Application policy: TinyFarm's screens (pockets, character, stove, chest), dialogue, feedback |
| `TinyFarm.UI` | Machina UI presentation of the HUD and menus |
| `TinyFarm` | The `IGame` (`TinyFarmApp`): shell input contexts, saves and checkpoints, world and HUD native layers, assets |
| `TinyFarm.Oblivion` | Live inspection surfaces |

`TinyFarmApp` translates shell actions into the InputMan frames TinyFarm's game logic reads, so the rules are unchanged.
The world and HUD are two `INativeLayerPresenter`s on the shell's layer stack. Saves store TinyFarm's semantic
snapshot (validated against this build's definitions) inside the shell's save envelope.

## Proof

```powershell
dotnet test Games/TinyFarm/TinyFarm.Core.Tests
.\Play-TinyFarm.cmd --headless --playtest-script Games/TinyFarm/Playtests/shell-headless.json --output artifacts/local/tinyfarm-headless --save-root artifacts/local/tinyfarm-headless/saves
.\Play-TinyFarm.cmd --playtest-script Games/TinyFarm/Playtests/shell-native.json --output artifacts/local/tinyfarm-native --save-root artifacts/local/tinyfarm-native/saves
```

The shell tests drive TinyFarm through the shell's input path: new game and movement, Escape closing pockets before it
pauses, a save round trip that matches the semantic hash, and sleep checkpoints. Facts under `tinyfarm.*` (screen,
scene, minute, status, slice objectives, semantic hash) can be asserted from playtest scripts.

Earlier proof modes, milestone scenarios (M1–M25), the supper prototype's native bootstrap, the MonoGame frontend and
the TinyFarm playtest CLI were removed when TinyFarm moved onto the shell; git history keeps them. Gate A evidence is
in `artifacts/tinyfarm-gate-a/` and the [Gate A report](../../docs/milestones/tinyfarm-mvp-gate-a-report.md).
