# TinyFarm: The Sleeping Spring

The opening slice: a garden, a home stove and bed, a shipping chest, a woodland crossing, and one attacking slime in
Old Burrow. Harvest a starter turnip, cook healing broth, plant and water, explore, fight or retreat, and come home to
your own harvest after sleeping.

Once Old Burrow is quiet and you are home again, Mara asks for a proper supper ("mushrooms, mint, and one fewer
slime"). That opens the river road: gather hen-of-the-woods and wild mint at Riverside, saute the mushrooms at your
stove with the recipe she gives you, plant a turnip for tomorrow, and bring it all to her. This chapter is the old
stand-alone supper prototype, rebuilt on the current game's stove, combat, journal and dialogue.

This is a review build of Gate A. The campaign, additional rooms, village shops and progression are unfinished, and
roads outside the opening route (and the river road, until Mara asks) are closed.

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
| E / right click | Talk, harvest, water, cook, open the chest, or enter a doorway |
| J | Swing the sword |
| Space | Dodge in your movement direction |
| 1–4 or mouse wheel, then K / left click | Select a tool, then use it (1 seeds, 3 axe) |
| R | Eat broth |
| B | Sleep (beside your bed) |
| I / C | Pockets and equipment / character |
| Mouse, or arrows, Enter, Escape | Click, scroll, navigate, confirm and close TinyFarm's menus |
| Click the search box (or Tab) | Type to filter pockets; Enter or Escape finishes |
| Click, Space, E, Escape | Advance, choose and leave conversations |
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
The world, Mara's dialogue portrait and the HUD are `INativeLayerPresenter`s on the shell's layer stack. The cursor
comes from the shell's pointer input (InputMan) and goes into the same Machina hit-testing the menus always used. Saves store TinyFarm's semantic
snapshot (validated against this build's definitions) inside the shell's save envelope.

## Proof

```powershell
dotnet test Games/TinyFarm/TinyFarm.Core.Tests
.\Play-TinyFarm.cmd --headless --playtest-script Games/TinyFarm/Playtests/shell-headless.json --output artifacts/local/tinyfarm-headless --save-root artifacts/local/tinyfarm-headless/saves
.\Play-TinyFarm.cmd --playtest-script Games/TinyFarm/Playtests/shell-native.json --output artifacts/local/tinyfarm-native --save-root artifacts/local/tinyfarm-native/saves
```

The shell tests drive TinyFarm through the shell's input path: new game and movement, Escape closing pockets before it
pauses, clicking pockets, typing a search, the wheel, right click and clicking through dialogue, a save round trip that
matches the semantic hash, and sleep checkpoints. `TinyFarmSupperChapterTests` plays the supper chapter end to end
through the resolver, with a mid-conversation save, a mid-quest save and replay parity. Facts under `tinyfarm.*`
(screen, scene, minute, status, journal, supper, search, semantic hash) can be asserted from playtest scripts.

The supper chapter has its own scripts, `supper-headless.json` and `supper-native.json`. They reach later scenes with
the `walk STAGE` command (`burrow`, `request`, `riverside`, `gather`, `cook`, `mara`, `finish`), which plays the
qualification walkthrough through ordinary resolver intents.

## Deferred to the next milestone

- **Riverside art.** The scene works and renders, but the Sleeping Spring presenter has no painterly treatment for it.
  The river is a flat blue field, reeds and mint are placeholder rectangles, and Elias's bench sits off-camera. The
  M21 painterly water exists in `TinyFarmLayers` behind the old `Legacy`/non-slice paths, which still need folding in
  or deleting.
- **The presenter's `Legacy` layout and non-slice branches** in `TinyFarmLayers`. Only the slice path is reachable
  now.
- **`TinyFarm.Presentation`**, the MonoGame-era Machina layer. Only its own tests use it.
- **Mouse in play beyond tools.** Click-to-walk and clicking the world (plots, Mara) are not wired. Left click uses
  the selected tool and right click interacts with what you face. Menus, search, the stats scrollbar and dialogue are
  clickable.
- **Gamepad through the shell.** TinyFarm's own profile has gamepad bindings, but the shell layout only declares
  keys and mouse.
- **Old TinyFarm saves** (`%LOCALAPPDATA%\TinyFarm\saves`) are not imported into the shell's slots.
- **Chapter save identity.** Each chapter changes the definition identity, so a shell save from an earlier build is
  rejected rather than migrated. A `GameSaveSchema` migration is the place for that once saves need to survive.

Earlier proof modes, milestone scenarios (M1–M25), the MonoGame frontend and the TinyFarm playtest CLI were removed when
TinyFarm moved onto the shell; git history keeps them. Gate A evidence is
in `artifacts/tinyfarm-gate-a/` and the [Gate A report](../../docs/milestones/tinyfarm-mvp-gate-a-report.md).
