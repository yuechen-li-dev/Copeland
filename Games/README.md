# Games

This directory contains playable applications and game-owned domain code. Games own their rules, authored agents, content, save schemas, and proof scenarios. Aurelian owns reusable runtime services under `src/Aurelian` and platform integrations under `src/Integrations`.

## Start a new game

For a new C# game, implement the [shared game shell](../docs/Aurelian/game-shell.md) contract, or use the [composable game starter](../docs/Aurelian/game-starter.md) for a first/third-person shooter. Reference `src/Integrations/Aurelian.Games/Aurelian.Games.csproj` for the shell; the FPS/TPS starter adds `Aurelian.Games.Humanoid` and selects concepts with `GameStarter.Run`. The shell's `IGame`/`GameApp.Run` path supplies menus, settings, save integration, audio, input, and native or headless playtesting. The checked-in [starter application](Starter/Aurelian.Starter/Program.cs) is the runnable example; use `starter.cmd` to launch it.

The concepts currently cover a first/third-person 3D shooter foundation. The starter guide lists supported fragments and their bounds. All eight applications below run on `IGame` and `GameApp`. Their normal launches share menus, persisted settings, resizable Vulkan windows, focus pause and playtesting. Specialized historical proof commands remain separate qualification tools.

## Run an existing application

Run launchers from the repository root. The table identifies the project entry point and the checked-in launcher where one exists.

| Game | Application | Launch |
| --- | --- | --- |
| Sunkill visual novel | `Sunkill/Aurelian.Ariadne.VnDemo` | `Play-Sunkill.cmd` |
| Beacon Run | `Beacon3D/Aurelian.Beacon3D` | `beacon3d.cmd` |
| Mossward strategy | `Strategy/Aurelian.StrategyDemo` | `dotnet run --project Games/Strategy/Aurelian.StrategyDemo -c Release` |
| TinyFarm | `TinyFarm/TinyFarm` | `Play-TinyFarm.cmd` |
| Composable starter | `Starter/Aurelian.Starter` | `starter.cmd` |
| Character animation lab | `CharacterLab/Aurelian.CharacterLab` | `characterlab.cmd` |
| Cloudstep 3D platformer | `Cloudstep/Cloudstep` | `cloudstep.cmd` |
| Hearth fire and smoke showcase | `Hearth/Hearth` | `hearth.cmd` |

Use `humanoid-starter.cmd` for the starter's playable humanoid variant. The [character guide](../docs/Aurelian/humanoid-animation.md) covers replaceable clips, named joint attachments, GPU presentation, and starter save/replay. Use `starter.cmd --asset-demo` for the textured GLB example; the [static asset guide](../docs/Aurelian/static-assets.md) covers Blender authoring, materials, instances, delivery, and explicit replacement.

## Graphics and playtesting

The [3D graphics starter guide](../docs/Aurelian/graphics-starter.md) documents the shared host's lighting, shadow, HDR, gallery, and proof paths. Run `graphics-gallery.cmd` from the repository root to compare the basic and modern rendering presets. The gallery and proof qualify specific local Vulkan paths; their presence does not imply support or performance on every device.

Each game's tests live alongside its application. TinyFarm's Oblivion adapter and shell playtest scripts also live under its game directory. For shared shell playtests, see the [game shell contract](../docs/Aurelian/game-shell.md); for other engine-owned playtest APIs, start with the [Aurelian documentation index](../docs/Aurelian/README.md). Project names and public namespaces are preserved.

## Shared shell qualification

Run the same script against any application; use a fresh save root for each run:

```powershell
dotnet run --project Games/Strategy/Aurelian.StrategyDemo -c Release -- --playtest-script Games/Playtests/common-shell.json --output artifacts/local/igame/mossward --save-root artifacts/local/igame/mossward/saves
```

The native script captures title, play, audio/display settings, an odd-resolution resize and focus pause. It asserts that simulation stays frozen while paused. Use `common-shell-headless.json` with `--headless` for the corresponding input/menu checks without captures. CharacterLab also requires `--body <gameplay-body.json>`.

Sunkill imports its old slot 1 into an empty shell slot and retains its typed Dominatus dialogue checkpoint. TinyFarm and Cloudstep retain their existing save schemas. Beacon Run, Mossward, CharacterLab and Hearth currently have no world save schema; their shell menus therefore hide save/load. Preferences are persisted for every app. See [migration details](../docs/Aurelian/game-shell.md#existing-applications).

Run all eight through the same script with `pwsh tools/Test-GameShells.ps1` (or add `-Headless`). The runner builds Release, uses a fresh per-run save root, records stdout/stderr and native captures, and emits `evidence.json`. Use `-NoBuild` only after building the current sources.
