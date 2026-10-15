# Games

Games own their rules, authored agents, content, save schemas and proof scenarios. Aurelian owns reusable runtime services under `src/Aurelian` and platform integrations under `src/Integrations`.

New games implement `IGame` and call `GameApp.Run(game, args)`. The [game shell](../docs/Aurelian/game-shell.md) supplies the window and resolution, title/pause/settings menus, key rebinding, audio volume, pause on focus loss, saves and headless/native playtesting. The starter runs on it; the older games below still have their own bootstraps.

| Game | Application | Launch |
| --- | --- | --- |
| Sunkill | `Sunkill/Aurelian.Ariadne.VnDemo` | `Play-Sunkill.cmd` |
| Beacon Run | `Beacon3D/Aurelian.Beacon3D` | `beacon3d.cmd` |
| Mossward strategy | `Strategy/Aurelian.StrategyDemo` | `dotnet run --project Games/Strategy/Aurelian.StrategyDemo -c Release` |
| TinyFarm | `TinyFarm/TinyFarm.Native` | `Play-TinyFarm.cmd` |
| Composable starter | `Starter/Aurelian.Starter` | `starter.cmd` |
| Character animation lab | `CharacterLab/Aurelian.CharacterLab` | `characterlab.cmd` |
| Cloudstep (3D platformer) | `Cloudstep/Cloudstep` | `cloudstep.cmd` |
| Hearth (fire and smoke showcase) | `Hearth/Hearth` | `hearth.cmd` |

Use `humanoid-starter.cmd` for the playable humanoid variant of the starter.
The [character guide](../docs/Aurelian/humanoid-animation.md) covers replaceable
clips, named joint attachments, GPU presentation and full starter save/replay.

Each game's tests live alongside its application. TinyFarm's CLI, playtesting adapter, Oblivion adapter and example scripts also live under its game directory. Project names and public namespaces are preserved.

See the [starter API guide](../docs/Aurelian/game-starter.md) for a small C# entry point, concept composition, customization and automated playtesting.

Use `starter.cmd --asset-demo` for the textured GLB example. The [static asset guide](../docs/Aurelian/static-assets.md) covers Blender authoring, materials, instances, delivery and explicit replacement.
